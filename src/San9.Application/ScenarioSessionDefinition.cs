using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal sealed class ScenarioSessionDefinition
{
    private readonly HashSet<int> _playerForces;
    internal ScenarioSessionDefinition(ScenarioInitialState definition, IReadOnlyCollection<int>? playerForceIds = null,
        SessionDefinitions? definitions = null)
    {
        Definitions = definitions ?? SessionDefinitions.Empty;
        Definitions.Validate();
        definition.Validate();
        _playerForces = (playerForceIds ?? (definition.PlayerForceId is int player ? [player] : [])).ToHashSet();
        if (_playerForces.Any(id => !definition.Forces.Any(f => f.Id == id) && !Definitions.Forces.Any(f => f.Id == id)) ||
            playerForceIds is not null && _playerForces.Count != playerForceIds.Count)
            throw new ArgumentException("Invalid player force selection.");
        InitialPlayerForceIds = _playerForces.Order().ToImmutableArray();
        definition = ActivatePlayerForces(definition, InitialPlayerForceIds);
        if (_playerForces.Any(id => definition.Forces.Any(f => f.Id == id && f.IsDormant)))
            throw new ArgumentException("Selected force cannot be activated.");
        definition = definition with { PlayerForceId = InitialPlayerForceIds.IsEmpty ? null : InitialPlayerForceIds[0] };
        if (definition.Day is not (1 or 11 or 21)) throw new ArgumentException("Expected a stable xun boundary.");
        Content = definition with
        {
            FictionalOfficerPlaceIds = definition.FictionalOfficerPlaceIds.ToArray(),
            Information = definition.Information.Copy(), Forces = definition.Forces.ToArray(),
            InitialEquipment = definition.InitialEquipment?.ToArray(),
            Officers = definition.Officers.ToArray(), Units = definition.Units.ToArray(),
            Settlements = definition.Settlements.Select(s => s with { Icons = s.Icons.ToArray() }).ToArray(),
            FacilityNameSuffixKeys = new(definition.FacilityNameSuffixKeys), RegionNameKeys = new(definition.RegionNameKeys), RegionCityIds = new(definition.RegionCityIds),
            ProvinceNameKeys = new(definition.ProvinceNameKeys), CityProvinceIds = new(definition.CityProvinceIds)
        };
        Rules = new(Content);
    }

    internal ScenarioInitialState Content { get; }
    internal SessionDefinitions Definitions { get; }
    internal ImmutableArray<int> InitialPlayerForceIds { get; }

    internal static ScenarioInitialState ActivatePlayerForces(ScenarioInitialState definition, IReadOnlyCollection<int> playerForceIds)
    {
        foreach (int id in playerForceIds) definition = ActivatePlayerForce(definition, id);
        return definition;
    }

    private static ScenarioInitialState ActivatePlayerForce(ScenarioInitialState definition, int forceId)
    {
        var activation = definition.SearchEvents.OfType<ForceActivationSearchEventDefinition>()
            .SingleOrDefault(d => d.ForceId == forceId);
        if (activation is null || !definition.Forces.Any(f => f.Id == activation.ForceId && f.IsDormant)) return definition;
        // Selecting a reserved force starts with its authored resources, without the exploration reward.
        return definition with
        {
            Forces = definition.Forces.Select(f => f.Id == activation.ForceId ? f with { IsDormant = false } : f).ToArray(),
            Settlements = definition.Settlements.Select(s => s.Id == activation.CityId || activation.AttachedSettlementIds.Contains(s.Id)
                ? s with { IsDormant = false } : s).ToArray(),
            Officers = definition.Officers.Select(o => activation.OfficerIds.Contains(o.State.Id)
                ? o with { State = o.State with { IsDormant = false } } : o).ToArray(),
            ConsumedEventKeys = definition.ConsumedEventKeys.Append(activation.ConsumptionKey).Distinct()
                .OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray()
        };
    }
    internal ScenarioRules Rules { get; }
    internal MilitaryRulebook? MilitaryRules => Rules.Military;
    internal StrategySnapshot CreatePreview()
    {
        var clock = new GameClock(Content.Year, Content.Month, Content.Day);
        var world = new WorldSnapshot(clock, new(clock, 0))
        {
            DefaultLoyalty = Content.DefaultLoyalty,
            Forces = Forces().ToArray(), Settlements = Settlements().ToArray(),
            Officers = PersonalPeerageMigration.Initialize(Content.Officers.Select(o => o.CreateState(Content.Year)), Content.Officers, clock), Units = Content.Units,
            Corps = Content.Information.Corps, Relations = Content.Information.Relations, Treasures = Content.Information.TreasureStates
        };
        return new StrategySnapshotSource(Content, Rules, Definitions).Project(world);
    }
    internal WorldState CreateWorld(ulong seed, int computerDamageBonusPercent = 0, bool battleDeathsEnabled = false,
        ControllerKind playerForceController = ControllerKind.Player, NewGameSettings? settings = null)
    {
        if (settings?.MixedTroopsEnabled == true &&
            (MilitaryRules is null || MilitaryRules.TroopTypes.IsEmpty || MilitaryRules.ResearchProjects.IsEmpty))
            throw new InvalidDataException("当前内容包缺少兵种与研究定义，请使用新版转换器重新准备游戏内容");
        if (settings?.MixedTroopsEnabled == true && MilitaryRules is { } military &&
            military.Formations.Values.Any(formation => formation.TravelMode == TravelMode.Water) &&
            !military.NavalTraining.Values.Any(training => training.ResearchProjectId is null))
            throw new InvalidDataException("当前内容包缺少基础水军训练，请使用新版转换器重新准备游戏内容");
        var forces = Forces().ToArray();
        var initialUnits = InitialUnits(settings).ToArray();
        var people = Content.Officers.Select(o => o.CreateState(Content.Year)).ToArray();
        people = settings?.PersonalPeerageEnabled == false ? people.Select(p => p with { PersonalPeerage = null }).ToArray() :
            PersonalPeerageMigration.Initialize(people, Content.Officers, new(Content.Year, Content.Month, Content.Day));
        if (settings?.FormationKnowledgeEnabled == true && MilitaryRules is { } formationRules)
        {
            people = FormationKnowledgeMigration.Initialize(people, forces, initialUnits, formationRules, newGame: true);
            var evidence = Content.Officers.Where(o => o.InitialFormationEvidence is not null)
                .ToDictionary(o => o.State.Id, o => o.InitialFormationEvidence!);
            people = people.Select(p => evidence.TryGetValue(p.Id, out var records)
                ? p with { FormationProficiencies = records.Aggregate(p.FormationProficiencies!,
                    (skills, record) => skills.SetItem(record.FormationId, record.Proficiency)) } : p).ToArray();
        }
        return WorldState.Create(new(Content.Year, Content.Month, Content.Day), seed,
        forces,
        BarbarianStartup.CreateSettlements(forces, Settlements(), people),
        people, initialUnits, Content.Information.Corps,
        Content.Information.TreasureStates, Content.Information.Relations,
        Content.Forces.Where(f => !f.IsDormant).OrderBy(f => _playerForces.Contains(f.Id) ? 0 : 1).ThenBy(f => f.Id)
            .Select(f => new ControlSeat(_playerForces.Contains(f.Id) && playerForceController == ControllerKind.Player ? "player" : $"ai:{f.Id}", f.Id,
                _playerForces.Contains(f.Id) ? playerForceController : ControllerKind.Automatic)), computerDamageBonusPercent, Content.Alliance,
        Content.AttackRequests.Select(r => new AttackRequestState(r.RequestedForceId, r.RequesterForceId, r.TargetSettlementId,
            Content.Settlements.Single(s => s.Id == r.TargetSettlementId).OwnerId!.Value, r.RemainingXun)), Content.AnnualAppearancesEnabled, settings?.NaturalDeathsEnabled ?? Content.NaturalDeathsEnabled, Content.FreeOfficerMigrationEnabled, battleDeathsEnabled, Content.OfficeRequestsEnabled, Content.ConsumedEventKeys, Content.ForceConsolidationEnabled, Content.ForceEmergenceEnabled, Content.CityIndependenceEnabled, Content.ImperialCourtEnded, Content.ThreeVisitsStarted, Content.FoodExchangeRate, Content.SettlementDefectionsEnabled, Content.UnitDefectionsEnabled,
        settings ?? new NewGameSettings { Difficulty = Content.OriginalAi.Advanced ? GameDifficulty.Advanced : GameDifficulty.Beginner }, Content.FictionalOfficerPlaceIds, _playerForces,
        Definitions.Forces.Select(f => f.Id).ToHashSet(), Definitions.Officers.Select(p => p.Id).ToHashSet(), Content.DefaultLoyalty,
        equipment: EquipmentMigration.UnequipNonServing(LegacyEquipmentMigration.Convert(Content.Information.Treasures,
            Content.Information.TreasureStates, Content.Information.Equipment).Instances.Concat(Content.InitialEquipment ?? []), people));
    }

    private IEnumerable<UnitState> InitialUnits(NewGameSettings? settings)
    {
        if (settings?.MixedTroopsEnabled != true) return Content.Units;
        var military = MilitaryRules!;
        var forces = Forces().ToDictionary(force => force.Id);
        var officers = Content.Officers.Select(o => o.CreateState(Content.Year)).ToArray();
        return Content.Units.Select(unit =>
        {
            if (unit.IsPersonnel || unit.IsEmbedded || unit.FormationId is not int formation ||
                military.Purpose(formation) != UnitPurpose.Combat) return unit;
            var siege = military.SiegeEquipment.Values.FirstOrDefault(item => item.FormationId == formation);
            if (siege is not null)
            {
                int count = officers.Count(person => person.IsServing && person.UnitId == unit.Id && person.OwnerId == unit.OwnerId);
                var replacement = military.Formations.Values.Where(candidate => candidate.TravelMode == TravelMode.Land &&
                        military.Purpose(candidate.Id) == UnitPurpose.Combat &&
                        !military.SiegeEquipment.Values.Any(item => item.FormationId == candidate.Id) &&
                        candidate.IsCommon && !candidate.MixedTroopsOnly && candidate.ResearchProjectId is null &&
                        candidate.SoldierCapacity >= unit.Soldiers && candidate.OfficerCapacity >= count &&
                        military.ValidFormationPair(candidate.Id, unit.NavalFormationId))
                    .OrderBy(candidate => candidate.Id == military.DefaultDeploymentFormationId ? 0 : 1)
                    .ThenBy(candidate => candidate.Id).FirstOrDefault();
                if (replacement is null) throw new InvalidDataException("初始攻城部队缺少容量足够的基础陆阵");
                if (unit.SiegeEquipmentId is int existing && existing != siege.Id)
                    throw new InvalidDataException("初始攻城阵与攻城器械不一致");
                unit = unit with { FormationId = replacement.Id, SiegeEquipmentId = siege.Id };
                formation = replacement.Id;
            }
            if (unit.NavalFormationId is not null || military.Formations[formation].TravelMode == TravelMode.Water)
            {
                int? training = military.NavalTraining.Values.Where(value => value.ResearchProjectId is null)
                    .OrderBy(value => value.Id).Select(value => (int?)value.Id).FirstOrDefault();
                if (training is null) throw new InvalidDataException("当前内容包缺少基础水军训练，请使用新版转换器重新准备游戏内容");
                unit = unit with { NavalTrainingId = unit.NavalTrainingId ?? training };
            }
            if (!unit.Detachments.IsEmpty) return unit;
            var members = officers.Where(person => person.IsServing && person.UnitId == unit.Id && person.OwnerId == unit.OwnerId)
                .OrderBy(person => unit.MemberPositions.TryGetValue(person.Id, out var position) ? position.Rank : BattleRank.Middle)
                .ThenBy(person => person.Id).ToArray();
            if (members.Length == 0) return unit;
            var tactics = members.ToDictionary(person => person.Id, person => person.EquippedTacticId);
            var initial = DetachmentAllocation.CreateDefault(members.Select(person => person.Id), tactics,
                Math.Max(unit.Soldiers, (uint)members.Length), forces[unit.OwnerId], military);
            if (initial.IsEmpty) throw new InvalidDataException("无法为初始部队分配兵种，请检查内容定义");
            return unit with { Detachments = DetachmentAllocation.Resize(initial, unit.Soldiers) };
        }).ToArray();
    }

    private IEnumerable<ForceState> Forces() => Content.Forces.Select(f => new ForceState(f.Id, f.Trust)
    {
        ColorCode = f.ColorCode,
        FormationGrants = f.FormationGrants.OrderBy(g => g.FormationId).ToImmutableArray(),
        ProvidedTactics = f.ProvidedTactics.Order().ToImmutableArray(), IsBarbarian = !_playerForces.Contains(f.Id) && f.IsBarbarian, IsDormant = f.IsDormant,
        EventHistory = f.EventHistory.OrderBy(k => k.Value, StringComparer.Ordinal).ToImmutableArray(), HasDeposedEmperor = f.HasDeposedEmperor, LeaderId = f.LeaderId, AdviserId = f.AdviserId, RankId = f.RankId, CountryTitleId = f.CountryTitleId, BaseTrust = f.BaseTrust, PersonalPeerageRegimeId = f.PersonalPeerageRegimeId
    });

    private IEnumerable<SettlementState> Settlements() => Content.Settlements.Select(s => new SettlementState(s.Id,
        s.OwnerId, s.Soldiers, s.Durability, s.MaxDurability, s.Morale, s.PublicOrder, s.Commerce, s.MaxCommerce,
        s.Cultivation, s.MaxCultivation)
    {
        EnabledTacticIds = MilitaryRules?.Places.GetValueOrDefault(s.Id)?.EnabledTacticIds ?? [],
        MerchantPresenceChance = s.MerchantPresenceChance, IsDormant = s.IsDormant, Kind = s.Kind, CorpsId = s.CorpsId, CanHostPersons = s.CanHostPersons, SoldierCapacity = s.SoldierCapacity, ConsumesFood = s.ConsumesFood, Population = s.Population, DraftPopulation = s.DraftPopulation, Wounded = s.Wounded
    });
}
