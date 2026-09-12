using San9.Core.Domain;

namespace San9.Content.Scenarios;

public sealed record RankDefinition(int Id, string NameKey, uint CommandCapacity, int RequiredTrust, int LoyaltyBonus);
public sealed record TreasureDefinition(int Id, string NameKey, int Category, int AbilityBonus, int LoyaltyBonus, int? TacticId)
{
    [System.Text.Json.Serialization.JsonRequired] public OfficerAbilities AbilityBonuses { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool EscapeMount { get; init; }
}
public sealed record OfficeDefinition(int Id, string NameKey, uint CommandCapacity, int Salary)
{
    [System.Text.Json.Serialization.JsonRequired] public int GovernorPriority { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public OfficerAbilities AbilityBonuses { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? RequiredRankId { get; init; }
}
public sealed record InformationDefinitions(RankDefinition[] Ranks, OfficeDefinition[] Offices, TreasureDefinition[] Treasures,
    CorpsState[] Corps, TreasureState[] TreasureStates)
{
    [System.Text.Json.Serialization.JsonRequired] public ForceRelation[] Relations { get; init; } = [];
    public static InformationDefinitions Empty => new([], [], [], [], []);
    public InformationDefinitions Copy() => new(Ranks.ToArray(), Offices.ToArray(), Treasures.ToArray(), Corps.ToArray(), TreasureStates.ToArray()) { Relations = Relations.ToArray() };
    public void Validate(ScenarioInitialState scenario)
    {
        static void Unique(IEnumerable<int> ids)
        { var values = ids.ToArray(); if (values.Any(i => i < 0) || values.Distinct().Count() != values.Length) throw new InvalidDataException("Invalid information identities."); }
        if (Relations.Any(r => r.FirstId >= r.SecondId || r.Value is < 0 or > 100 || !scenario.Forces.Any(f => f.Id == r.FirstId) || !scenario.Forces.Any(f => f.Id == r.SecondId)) || Relations.Select(r => (r.FirstId, r.SecondId)).Distinct().Count() != Relations.Length)
            throw new InvalidDataException("Invalid force relations.");
        Unique(Ranks.Select(r => r.Id)); Unique(Offices.Select(o => o.Id)); Unique(Treasures.Select(t => t.Id));
        Unique(Corps.Select(c => c.Id)); Unique(TreasureStates.Select(t => t.Id));
        if (scenario.Forces.Any(f => f.RankId is int rank && !Ranks.Any(r => r.Id == rank)) ||
            scenario.Officers.Any(o => o.State.OfficeId is int office && !Offices.Any(d => d.Id == office)) ||
            Corps.Any(c => (c.Treasury is null) != (scenario.Forces.SingleOrDefault(f => f.Id == c.OwnerId)?.LeaderId == c.LeaderId)))
            throw new InvalidDataException("Invalid rank, office or corps treasury reference.");
        if (Ranks.Any(r => string.IsNullOrWhiteSpace(r.NameKey) || r.RequiredTrust < 0 || r.LoyaltyBonus < 0) ||
            Offices.Any(o => string.IsNullOrWhiteSpace(o.NameKey) || o.Salary < 0 || o.GovernorPriority < 0 ||
                !o.AbilityBonuses.IsNonnegative || o.RequiredRankId is int rank && !Ranks.Any(r => r.Id == rank)) ||
            Treasures.Any(t => string.IsNullOrWhiteSpace(t.NameKey) || t.Category is < 0 or > 12 || t.AbilityBonus < 0 || !t.AbilityBonuses.IsNonnegative || t.LoyaltyBonus < 0) ||
            Treasures.Any(t => !TreasureStates.Any(state => state.Id == t.Id)) ||
            TreasureStates.Any(t => t.Discovery != TreasureDiscovery.Disabled && !Treasures.Any(d => d.Id == t.Id)) ||
            TreasureStates.Any(t => !t.IsValid ||
                t.DiscoveryRegionId is int region && !scenario.RegionNameKeys.ContainsKey(region) ||
                t.HolderId is int holder && !scenario.Officers.Any(o => o.State.Id == holder)) ||
            Corps.Any(c => !scenario.Forces.Any(f => f.Id == c.OwnerId) || !scenario.Officers.Any(o => o.State.Id == c.LeaderId && o.State.OwnerId == c.OwnerId)))
            throw new InvalidDataException("Invalid information definition or relationship.");
    }
}
