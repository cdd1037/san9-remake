namespace San9.Application.Presentation;

public enum RosterPage { Facilities, Officers, Units }
public abstract record RosterLocation;
public sealed record RosterSettlementLocation(int Id) : RosterLocation;
public sealed record RosterUnitLocation(int Id) : RosterLocation;
public readonly record struct RosterKey(RosterPage Page, int Id);
public sealed record RosterEntry(RosterKey Key, string NameKey, RosterLocation Location, string MessageKey, bool IsAvailable)
{
    public string? NameSuffixKey { get; init; }
    public string ResolveName(San9.Content.Text.TextCatalog texts) => texts.Resolve(NameKey) + (NameSuffixKey is {} key ? texts.Resolve(key) : "") + (Key.Page == RosterPage.Units ? "队" : "");
}

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
    public RosterPageState Units { get; } = new();
    public RosterPageState Current => Page switch
    {
        RosterPage.Facilities => Facilities,
        RosterPage.Officers => Officers,
        RosterPage.Units => Units,
        _ => throw new InvalidOperationException("Unknown roster page.")
    };

    public void Refresh(StrategySnapshot snapshot) => Refresh(snapshot,
        snapshot.World.Corps.SingleOrDefault(c => c.OwnerId == snapshot.PlayerForceId && c.IsRoot)?.Id);

    public void SelectPage(RosterPage page)
    {
        if (!Enum.IsDefined(page)) throw new ArgumentOutOfRangeException(nameof(page));
        Page = page;
    }

    public void Refresh(StrategySnapshot snapshot, int? corpsId)
    {
        if (corpsId < 0) throw new ArgumentOutOfRangeException(nameof(corpsId));
        bool reset = CorpsId != corpsId;
        CorpsId = corpsId;
        if (snapshot.PlayerForceId is null)
        {
            Facilities.Refresh(null, [], true); Officers.Refresh(null, [], true); Units.Refresh(null, [], true);
            return;
        }
        var force = snapshot.Forces.Single(f => f.Id == snapshot.PlayerForceId);
        static RosterEntry Facility(SettlementView s) => new(new(RosterPage.Facilities, s.Id), s.NameKey,
            new RosterSettlementLocation(s.Id), s.CityMarkerColor is not null ? San9.Content.Text.NameMessages.City : San9.Content.Text.NameMessages.Indented, s.HasAvailableOfficer) { NameSuffixKey = s.NameSuffixKey };
        static RosterEntry Officer(OfficerView o) => new(new(RosterPage.Officers, o.Id), o.NameKey,
            o.UnitId is int unit ? new RosterUnitLocation(unit) : new RosterSettlementLocation(o.SettlementId!.Value), o.HasCityPrefix ? San9.Content.Text.NameMessages.City : San9.Content.Text.NameMessages.Indented, o.IsAvailable);
        int? rulerServicePlace = snapshot.World.Officers.SingleOrDefault(o => o.Id == force.LeaderId) is { UnitId: int rulerUnitId }
            ? snapshot.World.Units.Single(u => u.Id == rulerUnitId).ServicePlaceId
            : force.LeaderSettlementId;
        var rulerFacility = snapshot.Settlements.SingleOrDefault(s => s.Id == rulerServicePlace && s.OwnerId == snapshot.PlayerForceId && s.IsFacilityListEntry);
        var serving = snapshot.Officers.Where(o => o.IsServing && o.OwnerId == snapshot.PlayerForceId &&
            (o.SettlementId is not null || o.UnitId is not null)).ToArray();
        var ruler = serving.SingleOrDefault(o => o.Id == force.LeaderId);
        Facilities.Refresh(rulerFacility is null ? null : Facility(rulerFacility),
            snapshot.Settlements.Where(s => corpsId is not null && s.CorpsId == corpsId && s.OwnerId == snapshot.PlayerForceId && s.IsFacilityListEntry).Select(Facility), reset);
        Officers.Refresh(ruler is null ? null : Officer(ruler),
            serving.Where(o => corpsId is not null && o.CorpsId == corpsId).Select(Officer), reset);
        var officers = snapshot.Officers.ToDictionary(o => o.Id);
        RosterEntry Unit(San9.Core.Domain.UnitState u) => new(new(RosterPage.Units, u.Id),
            u.CommanderId is int commander && officers.TryGetValue(commander, out var leader) ? leader.NameKey : force.NameKey,
            new RosterUnitLocation(u.Id), San9.Content.Text.NameMessages.Indented, u.Order is null && !u.AwaitingPostAction);
        var units = snapshot.World.Units.Where(u => u.OwnerId == snapshot.PlayerForceId && !u.IsEmbedded && !u.IsPersonnel).ToArray();
        var rulerUnit = units.SingleOrDefault(u => u.Id == ruler?.UnitId);
        Units.Refresh(rulerUnit is null ? null : Unit(rulerUnit),
            units.Where(u => corpsId is not null && u.CorpsId == corpsId).Select(Unit), reset);
    }

    public RosterEntry? Find(RosterKey key)
    {
        if (key.Page != Page) return null;
        return Current.Pinned?.Key == key ? Current.Pinned : Current.Ordinary.SingleOrDefault(e => e.Key == key);
    }
}
