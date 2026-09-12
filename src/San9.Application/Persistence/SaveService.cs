using System.Text.Json;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Application.Persistence;

public enum SaveFailure { InvalidFormat, UnsupportedVersion, ContentMismatch, IntegrityMismatch, InvalidState, IoFailure, NotStable }
public sealed class SaveException(SaveFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public SaveFailure Failure { get; } = failure;
}

public static class SaveService
{
    /// <summary>Player load: validate the save, then start new run streams. Saved task outcomes remain intact.</summary>
    public static GameSession Load(string path, ScenarioInitialState definition)
    {
        var factory = GameSession.EngineFactory(definition);
        return Decode(ReadBytes(path), definition, factory, playerLoad: true);
    }

    public static byte[] Encode(GameSession session)
    {
        SessionSnapshot snapshot;
        try { snapshot = session.Capture(); }
        catch (InvalidOperationException error) { throw new SaveException(SaveFailure.NotStable, "Cannot save during execution.", error); }
        return EncodeSnapshot(snapshot, session.ContentFingerprint);
    }

    internal static byte[] EncodeSnapshot(SessionSnapshot snapshot, string contentFingerprint)
    {
        var envelope = new SaveEnvelope(SessionProtocol.SaveFormatVersion, SessionProtocol.RulesVersion,
            CanonicalJson.Version, contentFingerprint, snapshot, "");
        return CanonicalJson.Encode(envelope with { IntegrityHash = Integrity(envelope) });
    }

    internal static string Integrity(SaveEnvelope envelope) => CanonicalJson.Hash(new
        { envelope.FormatVersion, envelope.RulesVersion, envelope.HashVersion, envelope.ContentFingerprint, envelope.Session });

    public static GameSession Decode(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition) => Decode(bytes, definition, GameSession.EngineFactory(definition));

    internal static GameSession Decode(ReadOnlyMemory<byte> bytes, ScenarioInitialState definition, Func<WorldState, SimulationEngine> factory,
        bool playerLoad = false)
    {
        SaveEnvelope envelope;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!root.TryGetProperty("formatVersion", out var format) || !root.TryGetProperty("rulesVersion", out var rules) ||
                !root.TryGetProperty("hashVersion", out var hash)) throw new JsonException("Missing version fields.");
            if (format.GetInt32() != SessionProtocol.SaveFormatVersion || rules.GetString() != SessionProtocol.RulesVersion ||
                hash.GetInt32() != CanonicalJson.Version)
                throw new SaveException(SaveFailure.UnsupportedVersion, "Unsupported save, rule or hash version.");
            envelope = StrictJson.Deserialize<SaveEnvelope>(bytes);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or NotSupportedException or ArgumentException)
        { throw new SaveException(SaveFailure.InvalidFormat, "Invalid save document.", error); }
        var owned = new ScenarioSessionDefinition(definition);
        if (envelope.ContentFingerprint != owned.Fingerprint)
            throw new SaveException(SaveFailure.ContentMismatch, "Save belongs to different gameplay content.");
        if (envelope.IntegrityHash != Integrity(envelope)) throw new SaveException(SaveFailure.IntegrityMismatch, "Save integrity mismatch.");
        try { return playerLoad ? GameSession.Load(owned, envelope.Session, factory) : GameSession.Restore(owned, envelope.Session, factory); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { throw new SaveException(SaveFailure.InvalidState, "Invalid authoritative session state.", error); }
    }

    public static void Write(string path, GameSession session) => Write(path, session, null);
    internal static void Write(string path, GameSession session, Action? beforeReplace)
    {
        byte[] bytes = Encode(session);
        try { AtomicFile.Write(path, bytes, beforeReplace); }
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
