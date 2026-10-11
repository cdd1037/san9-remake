using System.Collections.Immutable;
using System.Text.Json;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Application.Persistence;

public sealed record RecordingEnvelope(int FormatVersion, string RulesVersion, int HashVersion,
    SessionRecording Recording, string IntegrityHash);
public sealed record RecordingReplay(GameSession Session, GameClock Started, int Accepted, ImmutableArray<DomainEvent> Events);
public sealed class RecordingException(string message, long? sequence = null, Exception? inner = null) : Exception(message, inner)
{
    public long? Sequence { get; } = sequence;
}

/// <summary>Explicit debug recordings are separate artifacts, never part of a player save.</summary>
public static class RecordingService
{
    public const int FormatVersion = 14;

    public static byte[] Encode(SessionRecording recording)
    {
        var envelope = new RecordingEnvelope(FormatVersion, SessionProtocol.RulesVersion, CanonicalJson.Version, recording, "");
        return CanonicalJson.Encode(envelope with { IntegrityHash = Integrity(envelope) });
    }

    internal static string Integrity(RecordingEnvelope envelope) => CanonicalJson.Hash(new
        { envelope.FormatVersion, envelope.RulesVersion, envelope.HashVersion, envelope.Recording });

    public static void Write(string path, SessionRecording recording) => AtomicFile.Write(path, Encode(recording));

    public static RecordingReplay Replay(string path, ScenarioInitialState definition) =>
        Replay(File.ReadAllBytes(path), definition, GameSession.EngineFactory(definition));

    internal static RecordingReplay Replay(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition,
        Func<WorldState, SimulationEngine> factory)
    {
        RecordingEnvelope envelope;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("formatVersion").GetInt32() != FormatVersion ||
                root.GetProperty("rulesVersion").GetString() != SessionProtocol.RulesVersion ||
                root.GetProperty("hashVersion").GetInt32() != CanonicalJson.Version)
                throw new RecordingException("Unsupported recording, rules or hash version.");
            envelope = StrictJson.Deserialize<RecordingEnvelope>(bytes);
            if (envelope.IntegrityHash != Integrity(envelope)) throw new RecordingException("Recording integrity mismatch.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException or FormatException)
        { throw new RecordingException("Invalid recording document.", inner: error); }
        var recording = envelope.Recording;
        if (recording.Operations.IsDefault) throw new RecordingException("Missing recorded operations.");
        GameSession session;
        try
        {
            var owned = new ScenarioSessionDefinition(definition);
            if (recording.ContentFingerprint != owned.Fingerprint) throw new RecordingException("Recording content mismatch.");
            session = GameSession.Restore(owned, recording.Initial, factory);
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
            if (result.Disposition != SessionDisposition.Accepted ||
                !result.Events.Select(e => CanonicalJson.Text<DomainEvent>(e)).SequenceEqual(entry.Events.Select(e => CanonicalJson.Text<DomainEvent>(e))))
                throw new RecordingException("Recorded operation diverged: " + result.Rejection, sequence);
            events.AddRange(result.Events);
        }
        if (session.Capture().StateHash != recording.FinalHash)
            throw new RecordingException("Recording final state mismatch.", session.NextOperationSequence);
        return new(session, recording.Initial.World.Clock, recording.Operations.Length, events.ToImmutable());
    }
}
