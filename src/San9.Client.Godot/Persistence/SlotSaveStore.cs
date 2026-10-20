using System.Text.Json;
using San9.Application;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Client.Godot.Persistence;

public sealed record SlotSaveHeader(int Version, int SaveVersion, string RulesVersion, SaveSlotSummary Summary, SaveSlotFlag Flag);
public sealed record SlotSaveDocument(SlotSaveHeader Header, SaveEnvelope Save);
public sealed record SaveSlotFlag(int Color, int Glyph);
public sealed record AutoSaveEntry(int Slot, SaveSlotListing Listing);
public sealed record SaveSlotListing(SaveSlotSummary? Summary, SaveException? Error, SaveSlotFlag? Flag = null);

/// <summary>One atomic JSON file: independently readable header first, authoritative save second.</summary>
public sealed class SlotSaveStore(string directory, ScenarioInitialState definition)
{
    private const int FormatVersion = 4;
    public const int AutoSaveCount = 10;
    private readonly BackgroundSaveQueue _writes = new();
    public string AutoSavePathFor(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, AutoSaveCount);
        return Path.Combine(directory, $"auto-{slot:D2}.save.json");
    }

    public static int AutoSaveSlotFor(SessionSnapshot checkpoint)
    {
        long elapsed = checkpoint.World.Clock.DayNumber - checkpoint.World.Simulation.Started.DayNumber;
        if (elapsed < 0 || !checkpoint.World.Clock.IsXunBoundary || checkpoint.World.Simulation.Boundary != SimulationBoundary.Stable)
            throw new SaveException(SaveFailure.NotStable, "Automatic saves require a stable xun-start checkpoint.");
        return (int)(elapsed / 10 % AutoSaveCount) + 1;
    }
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
                var header = ReadFile(PathFor(slot), ReadPreview);
                entries.Add(slot, new(header.Summary, null, header.Flag));
            }
            catch (SaveException error) when (error.InnerException is FileNotFoundException or DirectoryNotFoundException) { }
            catch (SaveException error) { entries.Add(slot, new(null, error)); }
        }
        return entries;
    }
    public static SlotSaveDocument Capture(GameSession session, SaveSlotSummary summary)
        => Capture(SaveService.Capture(session), summary, session.View);

    private static SlotSaveDocument Capture(SaveEnvelope save, SaveSlotSummary summary, StrategySnapshot view)
    {
        var force = view.Forces.SingleOrDefault(f => f.Id == view.PlayerForceId);
        var header = new SlotSaveHeader(FormatVersion, SessionProtocol.SaveFormatVersion, SessionProtocol.RulesVersion,
            summary, new(force?.ColorCode ?? 0, force?.FlagGlyph ?? -1));
        return new(header, save);
    }

    public static SlotSaveDocument CaptureAutoSave(SessionSnapshot checkpoint, StrategySnapshot context,
        Func<StrategySnapshot, SaveSlotSummary> summary)
    {
        var view = context.ForWorld(checkpoint.World);
        return Capture(new(SessionProtocol.SaveFormatVersion, SessionProtocol.RulesVersion, checkpoint), summary(view), view);
    }

    public static byte[] Encode(GameSession session, SaveSlotSummary summary) => Encode(Capture(session, summary));
    // Canonical order places "header" before "save", allowing a bounded prefix read.
    public static byte[] Encode(SlotSaveDocument document) => CanonicalJson.Encode(document);
    public void Write(int slot, byte[] bytes) => WriteFile(PathFor(slot), bytes);
    public Task WriteAsync(int slot, SlotSaveDocument document) => _writes.Enqueue(() => Write(slot, Encode(document)));

    public Task WriteAutoSaveAsync(SlotSaveDocument document)
    {
        int slot = AutoSaveSlotFor(document.Save.Session);
        return _writes.Enqueue(() => WriteFile(AutoSavePathFor(slot), Encode(document)));
    }

    public IReadOnlyList<AutoSaveEntry> ReadAutoSaveCatalog()
    {
        var entries = new List<AutoSaveEntry>();
        for (int slot = 1; slot <= AutoSaveCount; slot++)
        {
            try
            {
                var header = ReadFile(AutoSavePathFor(slot), ReadPreview);
                entries.Add(new(slot, new(header.Summary, null, header.Flag)));
            }
            catch (SaveException error) when (error.InnerException is FileNotFoundException or DirectoryNotFoundException) { }
            catch (SaveException error) { entries.Add(new(slot, new(null, error))); }
        }
        return entries.OrderByDescending(e => e.Listing.Summary?.SavedAt).ThenBy(e => e.Slot).ToArray();
    }
    private static void WriteFile(string path, byte[] bytes)
    {
        try { AtomicUserFile.Write(path, bytes); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new SaveException(SaveFailure.IoFailure, "Could not atomically write slot save.", error); }
    }
    public GameSession Load(int slot) => LoadFile(PathFor(slot));
    public GameSession LoadAutoSave(int slot) => LoadFile(AutoSavePathFor(slot));
    private GameSession LoadFile(string path) => ReadFile(path, stream =>
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
    private static void ValidateHeader(SlotSaveHeader header)
    {
        if (header.Version != FormatVersion || header.SaveVersion != SessionProtocol.SaveFormatVersion)
            throw new SaveException(SaveFailure.UnsupportedVersion, "Unsupported slot or application save version.");
        if (string.IsNullOrWhiteSpace(header.RulesVersion))
            throw new JsonException("Missing slot rules version.");
        if (header.Summary.Players < 0 || header.Flag.Color < 0 || header.Flag.Glyph < -1)
            throw new JsonException("Invalid slot preview metadata.");
    }
    private static T ReadFile<T>(string path, Func<Stream, T> read)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return read(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new SaveException(SaveFailure.IoFailure, "Could not read slot save.", error); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException or OverflowException)
        { throw new SaveException(SaveFailure.InvalidFormat, "Invalid slot save document.", error); }
    }
}
