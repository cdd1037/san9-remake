namespace San9.Client.Godot.Persistence;

public enum SaveLoadMode { Save, Load }

/// <summary>Display metadata owned by the slot file; no world state or source-format fields.</summary>
public sealed record SaveSlotSummary(string Ruler, string? PortraitRole, string StartDate, string Scenario,
    string CurrentDate, DateTimeOffset SavedAt, int Players, bool? Historical, string Difficulty, string Note);

public sealed record SaveSlotRequest(SaveLoadMode Mode, int Slot, string Note, int? AutoSaveSlot = null);

/// <summary>Owns navigation and a pending selection. Confirmation never writes a save or alters a session.</summary>
public sealed class SaveLoadBrowser
{
    public const int PageSize = 5;
    private Dictionary<int, SaveSlotListing> _entries = [];
    public int SlotCount { get; }
    private IReadOnlyList<AutoSaveEntry> _autoSaves = [];
    public int AutoSavePage => (SlotCount + PageSize - 1) / PageSize;
    public int PageCount => AutoSavePage + 1;
    public bool IsAutoSavePage => Page == AutoSavePage;
    public int SlotAt(int row) => IsAutoSavePage ? SlotCount + row + 1 : Page * PageSize + row + 1;
    private SaveSlotListing? Listing(int slot) => slot > SlotCount
        ? _autoSaves.ElementAtOrDefault(slot - SlotCount - 1)?.Listing : _entries.GetValueOrDefault(slot);
    public void SetAutoSaves(IReadOnlyList<AutoSaveEntry> entries)
    {
        if (entries.Count > SlotSaveStore.AutoSaveCount) throw new ArgumentOutOfRangeException(nameof(entries));
        _autoSaves = entries.ToArray();
        PendingSlot = null;
    }
    public int Page { get; private set; }
    public SaveLoadMode Mode { get; private set; }
    public int? PendingSlot { get; private set; }
    public bool IsOpen { get; private set; }

    public SaveLoadBrowser(int slotCount = 40)
    {
        if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
        SlotCount = slotCount;
    }

    public SaveSlotSummary? Entry(int slot) => Listing(slot)?.Summary;
    public SaveSlotFlag? Flag(int slot) => Listing(slot)?.Flag;
    public string? Error(int slot) => Listing(slot)?.Error is { } error ? SaveFailureMessages.Describe(error) : null;
    public bool Occupied(int slot) => Listing(slot) is not null;
    public void SetEntries(IReadOnlyDictionary<int, SaveSlotListing> entries)
    {
        var replacement = entries.ToDictionary(p => p.Key, p => p.Value);
        if (replacement.Keys.Any(slot => slot < 1 || slot > SlotCount))
            throw new ArgumentOutOfRangeException(nameof(entries));
        _entries = replacement;
        PendingSlot = null;
    }
    public void Open(SaveLoadMode mode) { Mode = mode; PendingSlot = null; IsOpen = true; }
    public void Close() { IsOpen = false; PendingSlot = null; }
    public bool ChangePage(int page)
    {
        if (!IsOpen || PendingSlot is not null || page < 0 || page >= PageCount) return false;
        Page = page; return true;
    }
    public bool Select(int slot)
    {
        if (!IsOpen || PendingSlot is not null || slot < 1 || slot > SlotCount + SlotSaveStore.AutoSaveCount ||
            (slot > SlotCount ? AutoSavePage : (slot - 1) / PageSize) != Page ||
            Mode == SaveLoadMode.Save && slot > SlotCount || Mode == SaveLoadMode.Load && !Occupied(slot)) return false;
        PendingSlot = slot; return true;
    }
    public void CancelSelection() => PendingSlot = null;
    public SaveSlotRequest? Confirm(string note)
    {
        if (!IsOpen || PendingSlot is not int slot) return null;
        PendingSlot = null;
        return new(Mode, slot, Mode == SaveLoadMode.Save ? note : Entry(slot)?.Note ?? "",
            slot > SlotCount ? _autoSaves[slot - SlotCount - 1].Slot : null);
    }
}
