using System.Text.Json.Serialization;
using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

public sealed record RankDefinition(int Id, string NameKey, uint CommandCapacity, int RequiredTrust, int LoyaltyBonus,
    [property: JsonRequired] int Precedence)
{
    [JsonRequired] public bool PreferBalancedOfficeCandidates { get; init; }
    [JsonRequired] public bool IsUnranked { get; init; }
    [JsonRequired] public string? DescriptionKey { get; init; }
}
public sealed record TreasureDefinition(int Id, string NameKey, int Category, int AbilityBonus, int LoyaltyBonus, int? TacticId)
{
    [JsonRequired] public OfficerAbilities AbilityBonuses { get; init; }
    [JsonRequired] public bool EscapeMount { get; init; }
    [JsonRequired] public TreasureRetentionGroup RetentionGroup { get; init; }
    [JsonRequired] public bool AutomaticDiplomacyOfferAllowed { get; init; } = true;
    [JsonRequired] public ImmutableArray<int> FavoredOwnerIds { get; init; } = [];

    public TreasureRule ToRule() => new(Id, AbilityBonuses, LoyaltyBonus)
    {
        EscapeMount = EscapeMount, TacticId = TacticId, RetentionGroup = RetentionGroup,
        AutomaticDiplomacyOfferAllowed = AutomaticDiplomacyOfferAllowed, FavoredOwnerIds = FavoredOwnerIds
    };
}
public enum OfficeAppointmentGroup { Military, Civil }
public sealed record OfficeDefinition(int Id, string NameKey, uint CommandCapacity, int Salary)
{
    [JsonRequired] public OfficeAppointmentGroup AppointmentGroup { get; init; }
    [JsonRequired] public bool FillBeforeOrdinaryOffices { get; init; }
    [JsonRequired] public string? DescriptionKey { get; init; }
    [JsonRequired] public int GovernorPriority { get; init; }
    [JsonRequired] public OfficerAbilities AbilityBonuses { get; init; }
    [JsonRequired] public int? RequiredRankId { get; init; }
}
public record MilitaryCatalogDefinition(int Id, string NameKey)
{
    [JsonRequired] public string? DescriptionKey { get; init; }
}

public readonly record struct FormationSlot(int Column, int Row);
public sealed record FormationPresentation(int Columns, int Rows, ImmutableArray<FormationSlot> Slots,
    int VisionRadius, int VisionAngleDegrees, string? BackgroundRole, string? AvailableRole);
public sealed record FormationCatalogDefinition(int Id, string NameKey) : MilitaryCatalogDefinition(Id, NameKey)
{
    [JsonRequired] public FormationPresentation? Presentation { get; init; }
}

public sealed record InformationDefinitions(RankDefinition[] Ranks, OfficeDefinition[] Offices, TreasureDefinition[] Treasures,
    CorpsState[] Corps, TreasureState[] TreasureStates)
{
    [JsonRequired] public ImmutableArray<int> MapContextExcludedFacilityIds { get; init; } = [];
    [JsonRequired] public MilitaryCatalogDefinition[] Tactics { get; init; } = [];
    [JsonRequired] public FormationCatalogDefinition[] Formations { get; init; } = [];
    [JsonRequired] public CountryTitleDefinition[] CountryTitles { get; init; } = [];
    [JsonRequired] public ForceRelation[] Relations { get; init; } = [];
    public static InformationDefinitions Empty => new([], [], [], [], []);
    public InformationDefinitions Copy() => new(Ranks.ToArray(), Offices.ToArray(), Treasures.ToArray(), Corps.ToArray(), TreasureStates.ToArray()) { MapContextExcludedFacilityIds = MapContextExcludedFacilityIds, Relations = Relations.ToArray(), CountryTitles = CountryTitles.ToArray(), Tactics = Tactics.ToArray(), Formations = Formations.ToArray() };
    public void Validate(ScenarioInitialState scenario)
    {
        if (MapContextExcludedFacilityIds.IsDefault ||
            MapContextExcludedFacilityIds.Distinct().Count() != MapContextExcludedFacilityIds.Length ||
            MapContextExcludedFacilityIds.Any(id => scenario.Military?.Facilities.Any(f => f.Id == id) != true))
            throw new InvalidDataException("Invalid map context facility reference.");
        static void Unique(IEnumerable<int> ids)
        { var values = ids.ToArray(); if (values.Any(i => i < 0) || values.Distinct().Count() != values.Length) throw new InvalidDataException("Invalid information identities."); }
        if (Relations.Any(r => r.FirstId >= r.SecondId || r.Value is < 0 or > 100 || !scenario.Forces.Any(f => f.Id == r.FirstId) || !scenario.Forces.Any(f => f.Id == r.SecondId)) || Relations.Select(r => (r.FirstId, r.SecondId)).Distinct().Count() != Relations.Length)
            throw new InvalidDataException("Invalid force relations.");
        if (CountryTitles is null || CountryTitles.Any(t => t is null || t.Rule is null || string.IsNullOrWhiteSpace(t.NameKey) || t.FlagGlyph is < -1 or > 185))
            throw new InvalidDataException("Invalid country title definition.");
        Unique(CountryTitles.Select(t => t.Id));
        if (Tactics is null || Formations is null || Tactics.Concat(Formations).Any(d => d is null || string.IsNullOrWhiteSpace(d.NameKey) || d.DescriptionKey is not null && string.IsNullOrWhiteSpace(d.DescriptionKey)))
            throw new InvalidDataException("Invalid military catalog metadata.");
        foreach (var formation in Formations)
            if (formation.Presentation is { } p && (p.Columns <= 0 || p.Rows <= 0 || p.Slots.IsDefault ||
                p.Slots.Any(s => s.Column < 0 || s.Column >= p.Columns || s.Row < 0 || s.Row >= p.Rows) ||
                p.Slots.Distinct().Count() != p.Slots.Length || p.VisionRadius < 0 || p.VisionAngleDegrees is < 0 or > 360 ||
                p.BackgroundRole is not null && string.IsNullOrWhiteSpace(p.BackgroundRole) ||
                p.AvailableRole is not null && string.IsNullOrWhiteSpace(p.AvailableRole)))
                throw new InvalidDataException("Invalid formation presentation.");
        Unique(Tactics.Select(t => t.Id)); Unique(Formations.Select(f => f.Id));
        if (Tactics.Any(t => scenario.Military?.Tactics.Any(r => r.Id == t.Id) != true) ||
            Formations.Any(f => scenario.Military?.Formations.Any(r => r.Id == f.Id) != true))
            throw new InvalidDataException("Unknown military catalog rule reference.");
        if (Formations.Any(f => scenario.Military!.Formations.Single(r => r.Id == f.Id).RequiredTacticId is int tactic &&
            !Tactics.Any(t => t.Id == tactic)))
            throw new InvalidDataException("A catalog formation requires a named tactic reference.");
        if (Formations.Any(f => (scenario.Military!.Formations.Single(r => r.Id == f.Id).Tactics?.AutomaticTactics ?? [])
            .Any(id => !Tactics.Any(t => t.Id == id))))
            throw new InvalidDataException("A catalog formation requires named automatic tactic references.");
        var occupied = scenario.Forces.Where(f => f.CountryTitleId is not null).Select(f => f.CountryTitleId!.Value).ToArray();
        if (occupied.Distinct().Count() != occupied.Length || occupied.Any(id => !CountryTitles.Any(t => t.Id == id)))
            throw new InvalidDataException("Invalid initial country title ownership.");
        try
        {
            _ = new PoliticalRulebook(scenario.PoliticalEvents, Ranks.Select(r => new PeerageRule(r.Id, r.Precedence, r.RequiredTrust, r.LoyaltyBonus)),
                CountryTitles.Select(t => t.Rule), scenario.ProvinceCountryTitleIds);
            if (CountryTitles.Any(t => t.Rule.PreferredRulerIds.Any(id => !scenario.Officers.Any(p => p.State.Id == id))) ||
                scenario.ProvinceCountryTitleIds.Keys.Any(id => !scenario.CityProvinceIds.ContainsValue(id)))
                throw new ArgumentException("Unknown political reference.");
        }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid political content.", error); }
        Unique(Ranks.Select(r => r.Id)); Unique(Offices.Select(o => o.Id)); Unique(Treasures.Select(t => t.Id));
        if (scenario.Forces.Any(f => Corps.Count(c => c.OwnerId == f.Id && c.IsRoot) != 1))
            throw new InvalidDataException("Each initial force must have exactly one root corps.");
        Unique(Corps.Select(c => c.Id)); Unique(TreasureStates.Select(t => t.Id));
        if (Corps.Any(c => c.IsRoot ? c.Delegation is not GeneralDelegation : c.Delegation switch
            {
                GeneralDelegation or DomesticDelegation => false,
                DefeatForceDelegation target => target.ForceId == c.OwnerId || !scenario.Forces.Any(f => f.Id == target.ForceId),
                CaptureSettlementDelegation target => !scenario.Settlements.Any(s => s.Id == target.SettlementId),
                _ => true
            })) throw new InvalidDataException("Invalid corps delegation.");
        if (scenario.Forces.Any(f => f.RankId is int rank && !Ranks.Any(r => r.Id == rank)) ||
            scenario.Officers.Any(o => o.State.OfficeId is int office && !Offices.Any(d => d.Id == office)) ||
            Corps.Any(c => c.IsRoot != (scenario.Forces.SingleOrDefault(f => f.Id == c.OwnerId)?.LeaderId == c.LeaderId)))
            throw new InvalidDataException("Invalid rank, office or corps treasury reference.");
        if (Ranks.Count(r => r.IsUnranked) > 1 || Ranks.Any(r => r.DescriptionKey is not null && string.IsNullOrWhiteSpace(r.DescriptionKey)))
            throw new InvalidDataException("Invalid rank presentation metadata.");
        if (Ranks.Select(r => r.Precedence).Distinct().Count() != Ranks.Length)
            throw new InvalidDataException("Rank precedence must be unique.");
        if (Ranks.Any(r => string.IsNullOrWhiteSpace(r.NameKey) || r.RequiredTrust < 0 || r.LoyaltyBonus < 0 || r.Precedence < 0) ||
            Offices.Any(o => string.IsNullOrWhiteSpace(o.NameKey) || o.DescriptionKey is not null && string.IsNullOrWhiteSpace(o.DescriptionKey) || o.Salary < 0 || o.GovernorPriority < 0 ||
                !Enum.IsDefined(o.AppointmentGroup) || !o.AbilityBonuses.IsNonnegative || o.RequiredRankId is int rank && !Ranks.Any(r => r.Id == rank)) ||
            Treasures.Any(t => string.IsNullOrWhiteSpace(t.NameKey) || t.Category is < 0 or > 12 || !Enum.IsDefined(t.RetentionGroup) || t.AbilityBonus < 0 || !t.AbilityBonuses.IsNonnegative || t.LoyaltyBonus < 0 ||
                t.FavoredOwnerIds.IsDefault || t.FavoredOwnerIds.Distinct().Count() != t.FavoredOwnerIds.Length ||
                t.FavoredOwnerIds.Any(id => !scenario.Officers.Any(o => o.State.Id == id))) ||
            Treasures.Any(t => !TreasureStates.Any(state => state.Id == t.Id)) ||
            TreasureStates.Any(t => t.Discovery != TreasureDiscovery.Disabled && !Treasures.Any(d => d.Id == t.Id)) ||
            TreasureStates.Any(t => !t.IsValid ||
                t.DiscoveryRegionId is int region && !scenario.RegionNameKeys.ContainsKey(region) ||
                t.HolderId is int holder && !scenario.Officers.Any(o => o.State.Id == holder)) ||
            Corps.Any(c => c.Treasury is null || !scenario.Forces.Any(f => f.Id == c.OwnerId) ||
                (c.LeaderId is int leader ? !scenario.Officers.Any(o => o.State.Id == leader && o.State.OwnerId == c.OwnerId) : !c.IsRoot)))
            throw new InvalidDataException("Invalid information definition or relationship.");
    }
}
