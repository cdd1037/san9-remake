using System.Text.Json;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Application.Persistence;

public enum SaveFailure { InvalidFormat, UnsupportedVersion, ContentMismatch, InvalidState, IoFailure, NotStable }
public sealed class SaveException(SaveFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public SaveFailure Failure { get; } = failure;
}

public static class SaveService
{
    /// <summary>Player load: validate the save, then start new run streams. Saved task outcomes remain intact.</summary>
    public static GameSession Load(string path, ScenarioInitialState definition) => Load(ReadBytes(path), definition);

    public static GameSession Load(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition)
    {
        var factory = GameSession.EngineFactory(definition);
        return Decode(bytes, definition, factory, playerLoad: true);
    }

    public static GameSession Load(SaveEnvelope envelope, ScenarioInitialState definition)
        => Restore(envelope, definition, GameSession.EngineFactory(definition), playerLoad: true);

    public static byte[] Encode(GameSession session) => CanonicalJson.Encode(Capture(session));

    public static SaveEnvelope Capture(GameSession session)
    {
        SessionSnapshot snapshot;
        try { snapshot = session.Capture(); }
        catch (InvalidOperationException error) { throw new SaveException(SaveFailure.NotStable, "Cannot save before the current interaction and xun resolution finish.", error); }
        return CreateEnvelope(snapshot, session.ContentFingerprint);
    }

    internal static byte[] EncodeSnapshot(SessionSnapshot snapshot, string contentFingerprint)
        => CanonicalJson.Encode(CreateEnvelope(snapshot, contentFingerprint));

    private static SaveEnvelope CreateEnvelope(SessionSnapshot snapshot, string contentFingerprint)
    {
        if (!snapshot.World.Simulation.CanPersist)
            throw new SaveException(SaveFailure.NotStable, "Session is not at a save boundary.");
        return new SaveEnvelope(SessionProtocol.SaveFormatVersion, SessionProtocol.RulesVersion,
            CanonicalJson.Version, contentFingerprint, snapshot);
    }

    public static GameSession Decode(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition) => Decode(bytes, definition, GameSession.EngineFactory(definition));

    internal static GameSession Decode(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition, Func<WorldState, SimulationEngine> factory,
        bool playerLoad = false)
        => Restore(ReadEnvelope(bytes), definition, factory, playerLoad);

    private static GameSession Restore(SaveEnvelope envelope, ScenarioInitialState definition,
        Func<WorldState, SimulationEngine> factory, bool playerLoad)
    {
        if (envelope.FormatVersion != SessionProtocol.SaveFormatVersion ||
            envelope.HashVersion != CanonicalJson.Version)
            throw new SaveException(SaveFailure.UnsupportedVersion, "Unsupported save or hash version.");
        if (string.IsNullOrWhiteSpace(envelope.RulesVersion))
            throw new SaveException(SaveFailure.InvalidFormat, "Missing save rules version.");
        var owned = new ScenarioSessionDefinition(definition);
        if (envelope.ContentFingerprint != owned.Fingerprint)
            throw new SaveException(SaveFailure.ContentMismatch, "Save belongs to different gameplay content.");
        try
        {
            var session = playerLoad ? GameSession.Load(owned, envelope.Session, factory) : GameSession.Restore(owned, envelope.Session, factory);
            session.ReportRulesVersion(envelope.RulesVersion);
            return session;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { throw new SaveException(SaveFailure.InvalidState, "Invalid authoritative session state.", error); }
    }

    private static SaveEnvelope ReadEnvelope(ReadOnlyMemory<byte> bytes)
    {
        try { return StrictJson.Deserialize<SaveEnvelope>(bytes); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or NotSupportedException or ArgumentException)
        { throw new SaveException(SaveFailure.InvalidFormat, "Invalid save document.", error); }
    }

    public static void Write(string path, GameSession session) => Write(path, session, null);
    internal static void Write(string path, GameSession session, Action? beforeReplace)
    {
        long sequence = session.NextOperationSequence;
        byte[] bytes = Encode(session);
        try
        {
            AtomicFile.Write(path, bytes, beforeReplace);
            session.MarkSaved(sequence);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new SaveException(SaveFailure.IoFailure, "Could not atomically write save.", error); }
    }

    /// <summary>Exact restoration for checkpoints and diagnostics; player-facing loading must use Load.</summary>
    public static GameSession Read(string path, ScenarioInitialState definition) => Read(path, definition, GameSession.EngineFactory(definition));
    internal static GameSession Read(string path, ScenarioInitialState definition, Func<WorldState, SimulationEngine> factory)
        => Decode(ReadBytes(path), definition, factory);

    private static byte[] ReadBytes(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new SaveException(SaveFailure.IoFailure, "Could not read save.", error); }
        return bytes;
    }
}
