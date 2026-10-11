using System.Text.Json;
using San9.Application;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Client.Godot.Persistence;

public sealed record SlotSaveHeader(int Version, int SaveVersion, string RulesVersion, int HashVersion,
    string ContentFingerprint, SaveSlotSummary Summary, SaveSlotFlag Flag);
public sealed record SlotSaveDocument(SlotSaveHeader Header, SaveEnvelope Save);
public sealed record SaveSlotFlag(int Color, int Glyph);
public sealed record SaveSlotListing(SaveSlotSummary? Summary, SaveException? Error, SaveSlotFlag? Flag = null);

/// <summary>One atomic JSON file: independently readable header first, authoritative save second.</summary>
public sealed class SlotSaveStore(string directory, ScenarioInitialState definition)
{
    private const int FormatVersion = 3;
    public const string UserDirectory = "user://saves/slots";
    private readonly string _contentFingerprint = GameSession.Fingerprint(definition);
    public string PathFor(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, 1);
        return Path.Combine(directory, $"slot-{slot:D2}.save.json");
    }
    public IReadOnlyDictionary<int, SaveSlotListing> ReadCatalog(int slotCount)
    {
        var entries = new Dictionary<int, SaveSlotListing>();
        for (int slot = 1; slot <= slotCount; slot++)
        {
            try
            {
                var header = ReadFile(slot, ReadPreview);
                entries.Add(slot, new(header.Summary, null, header.Flag));
            }
            catch (SaveException error) when (error.InnerException is FileNotFoundException or DirectoryNotFoundException) { }
            catch (SaveException error) { entries.Add(slot, new(null, error)); }
        }
        return entries;
    }
    public static byte[] Encode(GameSession session, SaveSlotSummary summary)
    {
        var save = SaveService.Capture(session);
        var force = session.View.Forces.Single(f => f.Id == session.View.PlayerForceId);
        var header = new SlotSaveHeader(FormatVersion, SessionProtocol.SaveFormatVersion, SessionProtocol.RulesVersion,
            CanonicalJson.Version, session.ContentFingerprint, summary, new(force.ColorCode, force.FlagGlyph));
        // Canonical order places "header" before "save", allowing a bounded prefix read.
        return CanonicalJson.Encode(new SlotSaveDocument(header, save));
    }
    public void Write(int slot, byte[] bytes)
    {
        string path = PathFor(slot);
        try { AtomicUserFile.Write(path, bytes); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new SaveException(SaveFailure.IoFailure, "Could not atomically write slot save.", error); }
    }
    public GameSession Load(int slot) => ReadFile(slot, stream =>
    {
        var document = StrictJson.Deserialize<SlotSaveDocument>(stream);
        ValidateHeader(document.Header);
        return SaveService.Load(document.Save, definition);
    });

    /// <summary>Reads only the leading header (plus at most one buffer of read-ahead), never the save body.</summary>
    public SlotSaveHeader ReadPreview(Stream stream)
    {
        byte[] buffer = new byte[1024];
        int length = 0;
        while (true)
        {
            int read = stream.Read(buffer, length, buffer.Length - length);
            length += read;
            var reader = new Utf8JsonReader(buffer.AsSpan(0, length), isFinalBlock: read == 0, state: default);
            if (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected slot object.");
                if (reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("header"))
                        throw new SaveException(SaveFailure.UnsupportedVersion, "Slot requires a leading preview header.");
                    if (JsonDocument.TryParseValue(ref reader, out var document))
                    {
                        using (document)
                        {
                            var header = StrictJson.Deserialize<SlotSaveHeader>(document.RootElement);
                            ValidateHeader(header);
                            return header;
                        }
                    }
                }
            }
            if (read == 0) throw new JsonException("Truncated slot header.");
            if (length == buffer.Length) Array.Resize(ref buffer, checked(buffer.Length * 2));
        }
    }
    private void ValidateHeader(SlotSaveHeader header)
    {
        if (header.Version != FormatVersion || header.SaveVersion != SessionProtocol.SaveFormatVersion ||
            header.HashVersion != CanonicalJson.Version)
            throw new SaveException(SaveFailure.UnsupportedVersion, "Unsupported slot or application save version.");
        if (string.IsNullOrWhiteSpace(header.RulesVersion))
            throw new JsonException("Missing slot rules version.");
        if (header.Summary.Players < 1 || header.Flag.Color < 0 || header.Flag.Glyph < -1)
            throw new JsonException("Invalid slot preview metadata.");
        if (header.ContentFingerprint != _contentFingerprint)
            throw new SaveException(SaveFailure.ContentMismatch, "Slot belongs to different gameplay content.");
    }
    private T ReadFile<T>(int slot, Func<Stream, T> read)
    {
        try
        {
            using var stream = File.OpenRead(PathFor(slot));
            return read(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new SaveException(SaveFailure.IoFailure, "Could not read slot save.", error); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException or OverflowException)
        { throw new SaveException(SaveFailure.InvalidFormat, "Invalid slot save document.", error); }
    }
}
