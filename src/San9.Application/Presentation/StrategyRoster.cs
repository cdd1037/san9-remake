namespace San9.Application.Presentation;

public enum RosterPage { Facilities, Officers }
public readonly record struct RosterKey(RosterPage Page, int Id);
public sealed record RosterEntry(RosterKey Key, string NameKey, int SettlementId, bool HasCityPrefix, bool IsAvailable);

public sealed class RosterPageState
{
    public const int PageSize = 13;
    public RosterEntry? Pinned { get; private set; }
    public IReadOnlyList<RosterEntry> Ordinary { get; private set; } = [];
    public int Offset { get; private set; }
    public int MaximumOffset => Math.Max(0, Ordinary.Count - PageSize);
    public IEnumerable<RosterEntry> Visible => Ordinary.Skip(Offset).Take(PageSize);

    internal void Refresh(RosterEntry? pinned, IEnumerable<RosterEntry> entries, bool reset)
    {
        Pinned = pinned;
        Ordinary = Array.AsReadOnly(entries.Where(e => e.Key != pinned?.Key).OrderBy(e => e.Key.Id).ToArray());
        ScrollTo(reset ? 0 : Offset);
    }

    public void ScrollTo(int offset) => Offset = Math.Clamp(offset, 0, MaximumOffset);
    public void Wheel(int direction) => ScrollTo(Offset + Math.Sign(direction));
}

/// <summary>Page state is independent of both map selection and UI keyboard focus.</summary>
public sealed class StrategyRoster
{
    public RosterPage Page { get; private set; }
    public int? CorpsId { get; private set; }
    public RosterPageState Facilities { get; } = new();
    public RosterPageState Officers { get; } = new();
    public RosterPageState Current => Page == RosterPage.Facilities ? Facilities : Officers;

    public void SelectPage(RosterPage page)
    {
        if (!Enum.IsDefined(page)) throw new ArgumentOutOfRangeException(nameof(page));
        Page = page;
    }

    public void Refresh(StrategySnapshot snapshot, int corpsId)
    {
        if (corpsId < 0) throw new ArgumentOutOfRangeException(nameof(corpsId));
        bool reset = CorpsId != corpsId;
        CorpsId = corpsId;
        var force = snapshot.Forces.Single(f => f.Id == snapshot.PlayerForceId);
        static RosterEntry Facility(SettlementView s) => new(new(RosterPage.Facilities, s.Id), s.NameKey,
            s.Id, s.CityMarkerColor is not null, s.HasAvailableOfficer);
        static RosterEntry Officer(OfficerView o) => new(new(RosterPage.Officers, o.Id), o.NameKey,
            o.SettlementId!.Value, o.HasCityPrefix, o.IsAvailable);
        var rulerFacility = snapshot.Settlements.SingleOrDefault(s => s.Id == force.LeaderSettlementId && s.IsFacilityListEntry);
        var ruler = snapshot.Officers.SingleOrDefault(o => o.Id == force.LeaderId);
        Facilities.Refresh(rulerFacility is null ? null : Facility(rulerFacility),
            snapshot.Settlements.Where(s => s.CorpsId == corpsId && s.IsFacilityListEntry).Select(Facility), reset);
        Officers.Refresh(ruler is null ? null : Officer(ruler),
            snapshot.Officers.Where(o => o.IsServing && o.CorpsId == corpsId && o.OwnerId == snapshot.PlayerForceId).Select(Officer), reset);
    }

    public RosterEntry? Find(RosterKey key)
    {
        if (key.Page != Page) return null;
        return Current.Pinned?.Key == key ? Current.Pinned : Current.Ordinary.SingleOrDefault(e => e.Key == key);
    }
}
