using System.Text.Json;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Application.Persistence;

public enum SaveFailure { InvalidFormat, UnsupportedVersion, InvalidState, IoFailure, NotStable }
public sealed class SaveException(SaveFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public SaveFailure Failure { get; } = failure;
}

public static class SaveService
{
    /// <summary>Player load: validate the save, then start new run streams. Saved task outcomes remain intact.</summary>
    public static GameSession Load(string path, ScenarioInitialState definition) => Load(ReadBytes(path), definition);

    public static GameSession Load(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition)
        => Decode(bytes, definition, null, playerLoad: true);

    public static GameSession Load(SaveEnvelope envelope, ScenarioInitialState definition)
        => Restore(envelope, definition, null, playerLoad: true);

    public static byte[] Encode(GameSession session) => CanonicalJson.Encode(Capture(session));

    public static SaveEnvelope Capture(GameSession session)
    {
        SessionSnapshot snapshot;
        try { snapshot = session.Capture(); }
        catch (InvalidOperationException error) { throw new SaveException(SaveFailure.NotStable, "Cannot save before the current interaction and xun resolution finish.", error); }
        return CreateEnvelope(snapshot);
    }

    internal static byte[] EncodeSnapshot(SessionSnapshot snapshot)
        => CanonicalJson.Encode(CreateEnvelope(snapshot));

    private static SaveEnvelope CreateEnvelope(SessionSnapshot snapshot)
    {
        if (!snapshot.World.Simulation.CanPersist)
            throw new SaveException(SaveFailure.NotStable, "Session is not at a save boundary.");
        return new SaveEnvelope(SessionProtocol.SaveFormatVersion, SessionProtocol.RulesVersion, snapshot);
    }

    public static GameSession Decode(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition) => Decode(bytes, definition, null);

    internal static GameSession Decode(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition, Func<WorldState, SimulationEngine>? factory,
        bool playerLoad = false)
        => Restore(ReadEnvelope(bytes), definition, factory, playerLoad);

    private static GameSession Restore(SaveEnvelope envelope, ScenarioInitialState definition,
        Func<WorldState, SimulationEngine>? factory, bool playerLoad)
    {
        if (envelope.FormatVersion != SessionProtocol.SaveFormatVersion)
            throw new SaveException(SaveFailure.UnsupportedVersion, "Unsupported save version.");
        if (string.IsNullOrWhiteSpace(envelope.RulesVersion))
            throw new SaveException(SaveFailure.InvalidFormat, "Missing save rules version.");
        try
        {
            var owned = new ScenarioSessionDefinition(definition, envelope.Session.InitialPlayerForceIds);
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
        var save = Capture(session);
        WriteEnvelope(path, save, beforeReplace);
        session.MarkSaved(save.Session.NextOperationSequence);
    }

    /// <summary>Encodes and writes an already captured snapshot without accessing a live session.</summary>
    public static void Write(string path, SaveEnvelope save) => WriteEnvelope(path, save, null);

    private static void WriteEnvelope(string path, SaveEnvelope save, Action? beforeReplace)
    {
        byte[] bytes = CanonicalJson.Encode(save);
        try
        {
            AtomicFile.Write(path, bytes, beforeReplace);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new SaveException(SaveFailure.IoFailure, "Could not atomically write save.", error); }
    }

    /// <summary>Exact restoration for checkpoints and diagnostics; player-facing loading must use Load.</summary>
    public static GameSession Read(string path, ScenarioInitialState definition) => Read(path, definition, null);
    internal static GameSession Read(string path, ScenarioInitialState definition, Func<WorldState, SimulationEngine>? factory)
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
