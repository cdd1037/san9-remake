using System.Collections.Immutable;
using System.Text.Json;
using San9.Content.Scenarios;
using San9.Core.Diagnostics;
using San9.Core.Simulation;

namespace San9.Application.Persistence;

public sealed record RecordingEnvelope(int FormatVersion, string RulesVersion, SessionRecording Recording);
public sealed record RecordingReplay(GameSession Session, GameClock Started, int Accepted, ImmutableArray<DomainEvent> Events);
public sealed class RecordingException(string message, long? sequence = null, Exception? inner = null) : Exception(message, inner)
{
    public long? Sequence { get; } = sequence;
}

/// <summary>Recordings replay operations; recorded events are compared only when explicitly requested.</summary>
public static class RecordingService
{
    public const int FormatVersion = 31;

    public static byte[] Encode(SessionRecording recording) => CanonicalJson.Encode(
        new RecordingEnvelope(FormatVersion, SessionProtocol.RulesVersion, recording));

    public static void Write(string path, SessionRecording recording) => AtomicFile.Write(path, Encode(recording));

    public static RecordingReplay Replay(string path, ScenarioInitialState definition, Action<RulesVersionWarning>? reportWarning = null,
        bool verifyEvents = false) =>
        Replay(File.ReadAllBytes(path), definition, GameSession.EngineFactory(definition), reportWarning, verifyEvents);

    internal static RecordingReplay Replay(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition,
        Func<WorldState, SimulationEngine> factory, Action<RulesVersionWarning>? reportWarning = null, bool verifyEvents = false)
    {
        RecordingEnvelope envelope;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("formatVersion").GetInt32() != FormatVersion)
                throw new RecordingException("Unsupported recording version.");
            envelope = StrictJson.Deserialize<RecordingEnvelope>(root);
            if (string.IsNullOrWhiteSpace(envelope.RulesVersion)) throw new RecordingException("Missing recording rules version.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException or FormatException)
        { throw new RecordingException("Invalid recording document.", inner: error); }
        var recording = envelope.Recording;
        if (recording.Operations.IsDefault) throw new RecordingException("Missing recorded operations.");
        GameSession session;
        try
        {
            var owned = new ScenarioSessionDefinition(definition);
            session = GameSession.Restore(owned, recording.Initial, factory);
            if (session.ReportRulesVersion(envelope.RulesVersion) is { } warning) reportWarning?.Invoke(warning);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { throw new RecordingException("Invalid recording origin.", inner: error); }
        var events = ImmutableArray.CreateBuilder<DomainEvent>();
        foreach (var entry in recording.Operations)
        {
            long sequence = entry.Request.Sequence;
            if (sequence != session.NextOperationSequence || entry.Events.IsDefault)
                throw new RecordingException("Recording sequence or events invalid.", sequence);
            SessionResult result;
            try { result = session.Apply(entry.Request); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or NotSupportedException or KeyNotFoundException)
            { throw new RecordingException("Recorded operation failed during execution.", sequence, error); }
            if (result.Disposition != SessionDisposition.Accepted)
                throw new RecordingException("Recorded operation diverged: " + result.Rejection, sequence);
            if (verifyEvents && EventDiagnostics.Compare(entry.Events, result.Events) is { } difference)
                throw new RecordingException($"Recorded events diverged at {difference.Path}: expected {difference.Expected}, actual {difference.Actual}.", sequence);
            events.AddRange(result.Events);
        }
        return new(session, recording.Initial.World.Clock, recording.Operations.Length, events.ToImmutable());
    }
}
