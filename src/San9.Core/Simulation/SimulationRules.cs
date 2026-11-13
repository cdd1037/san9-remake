using San9.Core.Domain;

namespace San9.Core.Simulation;

// Rules are compiled domain code, not callbacks loaded from content or saves.
public class SimulationRules
{
    public OfficerGrowthRules OfficerGrowth { get; init; } = OfficerGrowthRules.Default;
    public GuanduDefinition? Guandu { get; init; }
    public ShanyangGongDefinition? ShanyangGong { get; init; }
    public RedCliffsDefinition? RedCliffs { get; init; }
    public ChainedPlotDefinition? ChainedPlot { get; init; }
    public ThreeVisitsDefinition? ThreeVisits { get; init; }
    public HanzhongKingDefinition? HanzhongKing { get; init; }
    public FiveTigersDefinition? FiveTigers { get; init; }
    public ExpeditionMemorialDefinition? ExpeditionMemorial { get; init; }
    public SunMarriageDefinition? SunMarriage { get; init; }
    public QiaoMarriageDefinition? QiaoMarriage { get; init; }
    public PoliticalRulebook Politics { get; init; } = PoliticalRulebook.Empty;
    public TerminalDefinition? Terminal { get; init; }
    public EconomyRulebook Economy { get; init; } = EconomyRulebook.Empty;
    public TreasureRulebook Treasures { get; init; } = TreasureRulebook.Empty;
    public EquipmentRulebook Equipment { get; init; } = EquipmentRulebook.Empty;
    public PersonnelRulebook Personnel { get; init; } = PersonnelRulebook.Empty;
    public GovernanceRulebook Governance { get; init; } = new([]);
    public MilitaryRulebook? Military { get; init; }
    internal OfficerAbilityRules Abilities => new(Treasures, Governance, OfficerGrowth, Equipment);
    internal virtual TaskRule? FindTask(string kind) => kind is PersonnelMissions.RecruitKind or PersonnelMissions.MoveKind or PersonnelMissions.SearchKind
        ? new PersonnelMissionRule(this, kind) : kind == PersonnelReturns.Kind ? new PersonnelReturnRule(Personnel, Military)
        : kind == UnitStrategies.Kind ? new UnitStrategyRule(this) : kind == BurnStrategies.Kind ? new BurnRule(this)
        : kind == AlienateStrategies.Kind ? new AlienateRule(this)
        : kind == SeizeStrategies.Kind ? new SeizeRule(this)
        : kind == GossipStrategies.Kind ? new GossipRule(this)
        : kind == GiftDiplomacy.Kind ? new GiftRule(this)
        : kind == PrisonerReturnDiplomacy.Kind ? new PrisonerReturnRule(this)
        : kind == AttackRequestDiplomacy.Kind ? new AttackRequestRule(this)
        : kind == SurrenderDiplomacy.Kind ? new SurrenderRule(this)
        : kind == TacticStudy.Kind ? new TacticStudyRule(this) : null;
    internal virtual void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events) { }
    internal void ValidateEvents(WorldMutation initial)
    {
        var rules = this;
        var peerageRegimes = Economy.PersonalPeerageRegimes;
        if (peerageRegimes is null || peerageRegimes.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null) ||
            initial.AllForces.Values.Any(f => f.PersonalPeerageRegimeId is string id && !peerageRegimes.ContainsKey(id)) ||
            Politics.Titles.Values.Any(t => t.PersonalPeerageRegimeId is string id && !peerageRegimes.ContainsKey(id)))
            throw new ArgumentException("Unknown personal peerage regime.");
        rules.QiaoMarriage?.Validate(initial.AllOfficers.Keys.ToHashSet(), rules.Personnel.CityProvinces);
        rules.SunMarriage?.Validate(initial.AllOfficers.Keys.ToHashSet(),
            rules.Military?.Tactics.Values.Where(t => t.Category is >= 0 and <= 8).Select(t => t.Id).ToHashSet() ?? []);
        foreach (var ceremony in rules.Personnel.SearchEvents.Values.OfType<ImperialCeremonySearchEventDefinition>())
            if (!rules.Politics.Ranks.ContainsKey(ceremony.RequiredRankId)) throw new ArgumentException("Unknown ceremony rank reference.");
        var officerIds = initial.AllOfficers.Keys.ToHashSet();
        rules.ChainedPlot?.Validate(officerIds);
        rules.ThreeVisits?.Validate(officerIds, rules.Personnel.Places.Keys.ToHashSet());
        rules.ThreeVisits?.ValidateStarted(initial.Simulation.ThreeVisitsStarted, initial.Clock, initial.Simulation.ConsumedEventKeys);
        if (rules.ThreeVisits is null && initial.Simulation.ThreeVisitsStarted is not null)
            throw new ArgumentException("Three Visits state requires a definition.");
        var cityIds = initial.AllSettlements.Values.Where(s => s.Kind == SettlementKind.City).Select(s => s.Id).ToHashSet();
        foreach (var activation in rules.Personnel.SearchEvents.Values.OfType<ForceActivationSearchEventDefinition>())
        {
            activation.ValidateReferences(initial.AllForces.Keys.ToHashSet(), cityIds, initial.AllSettlements.Keys.ToHashSet(),
                officerIds, rules.Military?.RegionIds.IsEmpty == false);
            var city = initial.AllSettlements[activation.CityId];
            var force = initial.AllForces[activation.ForceId];
            if (city.SoldierCapacity < activation.CitySoldiers || force.IsDormant &&
                (!city.IsDormant || city.OwnerId != force.Id || force.LeaderId is not int leader || !activation.OfficerIds.Contains(leader) ||
                 force.AdviserId is int adviser && !activation.OfficerIds.Contains(adviser) ||
                 activation.AttachedSettlementIds.Any(id => !initial.AllSettlements[id].IsDormant) ||
                 activation.OfficerIds.Any(id => !initial.AllOfficers[id].IsDormant || initial.AllOfficers[id].OwnerId != force.Id ||
                     initial.AllOfficers[id].SettlementId != city.Id)))
                throw new ArgumentException("Invalid dormant force activation state.");
        }
        rules.ShanyangGong?.Validate(officerIds, rules.Personnel.Places.Keys.ToHashSet(), rules.Politics.ImperialRankId is not null);
        rules.Guandu?.Validate(officerIds, rules.Personnel.Places.Keys.ToHashSet(), rules.Military?.RegionIds.IsEmpty == false);
        rules.RedCliffs?.Validate(officerIds, cityIds, rules.Personnel.Places.Keys.ToHashSet(), rules.Military?.RegionIds.IsEmpty == false);
        var tacticIds = rules.Military?.Tactics.Values.Where(t => t.Category is >= 0 and <= 8).Select(t => t.Id).ToHashSet() ?? [];
        rules.HanzhongKing?.Validate(officerIds, cityIds, rules.Politics.Ranks.Keys.ToHashSet());
        rules.FiveTigers?.Validate(officerIds, tacticIds);
        rules.ExpeditionMemorial?.Validate(officerIds, rules.Personnel.CityProvinces, cityIds, rules.Politics.Titles.Keys.ToHashSet(), tacticIds);
    }
}
