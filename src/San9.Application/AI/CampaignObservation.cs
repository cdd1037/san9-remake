using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Projects legal own actions and public military facts. The planner receives no world or engine handle.</summary>
internal static class CampaignObservationCapture
{
    internal static CampaignObservation Capture(SimulationEngine engine, ControlSeat seat,
        ImmutableArray<(string ControllerId, EconomicObservation Observation)> economies,
        SupportObservation support, MilitaryRulebook rules, AllocationWork work, ArrivalProjectionCache routes,
        ImmutableArray<CampaignGoal> commitments = default)
    {
        var world = engine.Snapshot;
        var abilities = engine.GetAbilities(world);
        var settlementsById = world.Settlements.ToDictionary(s => s.Id);
        var officersById = world.Officers.ToDictionary(p => p.Id);
        var residents = world.Officers.Where(p => p.IsServing).ToLookup(p => p.SettlementId);
        var traveling = world.Simulation.Tasks.Where(t => t.Journey is not null).Select(t => t.OfficerId).ToHashSet();
        var force = world.Forces.Single(f => f.Id == seat.ForceId);
        var occupancyByOwner = new Dictionary<int, ImmutableArray<MapOccupancy>>();
        ImmutableArray<MapOccupancy> Occupancy(int owner)
        {
            if (occupancyByOwner.TryGetValue(owner, out var saved)) return saved;
            return occupancyByOwner[owner] = MilitaryRouteQuery.Occupancy(world, rules, owner, new(), includeUnits: false).ToImmutableArray();
        }
        var accounts = economies.SelectMany(e => e.Observation.Accounts).DistinctBy(a => a.Id).ToImmutableArray();
        var controlled = economies.SelectMany(e => e.Observation.Domestic.Settlements.Select(s => (e.ControllerId, Place: s))).ToArray();
        var places = ImmutableArray.CreateBuilder<CampaignPlace>();
        var forces = ImmutableArray.CreateBuilder<CampaignForce>();
        var waves = ImmutableArray.CreateBuilder<CampaignWave>();
        var returns = ImmutableArray.CreateBuilder<AllocationCandidate>();
        var consolidation = ImmutableArray.CreateBuilder<AllocationCandidate>();
        bool analysisReady = false;
        var unresolvedUnits = world.Units.Where(u => u.OwnerId == seat.ForceId && !u.IsEmbedded && !u.IsPersonnel &&
            u.FormationId is not null && rules.Formation(u).Combat is not null).Select(u => u.Id).ToHashSet();
        bool complete = support.Complete || support.DefenseComplete;
        bool Spend() { if (work.Spend()) return true; complete = false; return false; }
        CampaignObservation Result() => new(world.Clock, seat.ForceId, places.ToImmutable(), forces.ToImmutable(), waves.ToImmutable(),
            returns.ToImmutable(), consolidation.ToImmutable(), accounts, complete)
            { AnalysisReady = analysisReady, UnresolvedUnitIds = unresolvedUnits.Order().ToImmutableArray(), ActiveUnitIds = world.Units.Where(u => u.OwnerId == seat.ForceId).Select(u => u.Id).ToImmutableArray(),
                Purchases = economies.SelectMany(e => e.Observation.Purchases.Select(p => new CampaignPurchase(e.ControllerId, p))).ToImmutableArray(),
                WaitingSources = world.Simulation.Tasks.Where(t => t.ForceId == seat.ForceId && t.Kind == "personnel-move")
                    .Select(t => t.Journey?.DestinationPlaceId).OfType<int>().Distinct().Order().ToImmutableArray() };
        bool Enemy(int? owner) => owner != seat.ForceId && (owner is not int other ||
            world.Simulation.Alliance?.Protects(seat.ForceId, other) != true &&
            !world.Simulation.AttackRequests.Any(r => r.Connects(seat.ForceId, other)) &&
            (world.Relations.FirstOrDefault(r => r.FirstId == Math.Min(other, seat.ForceId) && r.SecondId == Math.Max(other, seat.ForceId))?.Value ?? 0) < 40);
        bool Authorized(string controller, SettlementState target)
        {
            var delegation = controller.StartsWith("corps:", StringComparison.Ordinal)
                ? world.Corps.Single(c => CorpsControl.ControllerId(c.Id) == controller).Delegation : new GeneralDelegation();
            return delegation switch { DomesticDelegation => false, CaptureSettlementDelegation d => target.Id == d.SettlementId,
                DefeatForceDelegation d => target.OwnerId == d.ForceId, _ => true };
        }
        var eligibility = new Dictionary<string, Dictionary<GameCommand, bool>>(StringComparer.Ordinal);
        Guid? queryId = null;
        bool Legal(string controller, GameCommand intent)
        {
            if (!Spend()) return false;
            if (!eligibility.TryGetValue(controller, out var commands))
                eligibility.Add(controller, commands = new(PlanningIntentEquality.Instance));
            if (commands.TryGetValue(intent, out bool accepted)) return accepted;
            queryId ??= new Guid(Convert.FromHexString(CanonicalJson.Hash(new
                { seat.ForceId, world.Clock, world.Simulation.NextSequence, Purpose = "campaign-query" })).AsSpan(0, 16));
            var query = intent with { CommandId = queryId.Value,
                Sequence = world.Simulation.NextSequence };
            return commands[intent] = engine.GetPreparationEligibility(new(controller), query) == CommandRejection.None;
        }
        ArrivalEstimate Route(WorldPosition from, int formation, int? naval, SettlementState target, int owner,
            int preparationDays = 0, int remainder = 0, WorldPosition? waypoint = null, NavigationLookahead? lookahead = null)
        {
            if (!Spend()) return new(ArrivalEstimateStatus.Incomplete);
            // Close approach is required to finish a siege; maximum ranged reach alone is insufficient.
            var navigation = work;
            var estimate = routes.Evaluate(new(rules.Travel(formation, naval), from, rules.SettlementArea(target),
                Occupancy(owner),
                PreparationDays: preparationDays, MovementRemainder: remainder, Waypoint: waypoint, Lookahead: lookahead,
                AllowTargetOverlap: target.OwnerId == owner), navigation);
            if (estimate.Status == ArrivalEstimateStatus.Incomplete) complete = false;
            return estimate;
        }
        var publicPlaces = world.Settlements.Where(s => !s.IsDormant && !s.IsDestroyed && s.CanHostPersons &&
            (s.OwnerId == seat.ForceId || Enemy(s.OwnerId)) && rules.TryPlace(s, out var p) && p.Combat is not null).OrderBy(s => s.Id).ToArray();
        var activeTargets = commitments.IsDefault ? [] : commitments.Where(g => g.ForceId == seat.ForceId).Select(g => g.TargetId).ToArray();
        var ongoing = commitments.IsDefault ? [] : commitments.Where(g => g.ForceId == seat.ForceId &&
            g.Phase != CampaignPhase.CoolingDown).ToArray();
        bool NeedsNewForce(SettlementState target) => ongoing.Length == 0 || ongoing.Any(g =>
            g.TargetId == target.Id && g.ExpectedOwnerId == target.OwnerId &&
            g.Phase is not (CampaignPhase.Withdrawing or CampaignPhase.Consolidating));
        // Bound expensive route comparisons by useful nearby objectives, retaining every existing commitment.
        var frontier = publicPlaces.Where(s => Enemy(s.OwnerId)).OrderBy(s => controlled.Select(c =>
            MapCoordinates.Distance(rules.Place(settlementsById[c.Place.Id]).Position, rules.Place(s).Position)).DefaultIfEmpty(long.MaxValue).Min())
            .ThenByDescending(s => s.Commerce + s.Cultivation).ThenBy(s => s.Id).Take(6).Select(s => s.Id).ToHashSet();
        var targets = publicPlaces.Where(s => s.OwnerId == seat.ForceId || frontier.Contains(s.Id) || activeTargets.Contains(s.Id)).ToArray();
        long NearestControlled(SettlementState target) => controlled.Select(c =>
            MapCoordinates.Distance(rules.Place(settlementsById[c.Place.Id]).Position,
                rules.Place(target).Position)).DefaultIfEmpty(long.MaxValue).Min();
        foreach (var target in targets.OrderBy(NearestControlled).ThenBy(s => s.Id))
        {
            if (!Spend()) return Result();
            // Foreign bases expose base abilities of visible members, never treasure modifiers or private tasks.
            int leadership = world.Officers.Where(p => p.IsServing && p.SettlementId == target.Id &&
                (target.OwnerId == seat.ForceId || p.IsVisible)).Select(p => target.OwnerId == seat.ForceId
                    ? abilities[p.Id].Leadership : p.BaseAbilities.Leadership).DefaultIfEmpty(50).Max();
            places.Add(new(target.Id, target.OwnerId, target.Soldiers, target.Morale, target.Durability, target.MaxDurability,
                target.PublicOrder, leadership, rules.Place(target).Combat!, 15000L + target.Commerce * 10L + target.Cultivation * 10L)
                { CanRetaliate = target.OwnerId is not null && rules.FacilityAttack(target).Attack is not null });
        }
        var formations = rules.Formations.Values.Where(f => f.Combat is not null && f.Attack is not null &&
            rules.CanSelectFormation(force, f.Id))
            .OrderBy(f => f.DeploymentCost).ThenBy(f => f.Id).ToArray();
        var threats = routes.Situation(world, seat.ForceId, rules).Project(targets, rules, work, includeGarrisons: true, attackRange: false);
        foreach (var threat in threats.Arrivals)
            waves.Add(new(threat.TargetId, threat.EarliestDays, threat.Source.Soldiers));
        if (!threats.Complete) { complete = false; return Result(); }
        analysisReady = support.Complete || support.DefenseComplete;
        foreach (var (controller, home) in controlled.Where(c => activeTargets.Contains(c.Place.Id)))
        {
            var people = home.OfficerIds;
            var account = accounts.Single(a => a.Id == home.AccountId);
            // Capture survivors become normal residents; repair and patrol use exactly the same public commands.
            foreach (var activity in new[] { DomesticActivity.Patrol, DomesticActivity.Repair, DomesticActivity.Train })
            {
                if (!Spend()) return Result();
                if (people.Length == 0 || home.UnavailableActivities.Contains(activity) || activity == DomesticActivity.Patrol && home.PublicOrder >= 500 ||
                    activity == DomesticActivity.Repair && home.Durability >= home.MaxDurability / 2 ||
                    activity == DomesticActivity.Train && home.Morale >= 65) continue;
                int actor = people.OrderByDescending(id => activity == DomesticActivity.Train ? abilities[id].Leadership : abilities[id].Politics).First();
                var command = new DevelopCommand(Guid.Empty, seat.ForceId, 0, new(seat.ForceId, home.Id, [actor], activity));
                if (Legal(controller, command)) consolidation.Add(new(controller, account.Id, command, [actor], 1, 1000, activity == DomesticActivity.Train ? 0 : 150, 0,
                    home.Id, home.Id, Purpose: "consolidate"));
            }
        }
        foreach (var unit in world.Units.Where(u => u.OwnerId == seat.ForceId && !u.IsEmbedded && !u.IsPersonnel &&
            u.FormationId is not null && rules.Formation(u).Combat is not null).OrderBy(u => u.Id))
        {
            if (!Spend()) return Result();
            var scope = economies.FirstOrDefault(e => CorpsControl.CanControlUnit(world, seat, new(e.ControllerId), unit.Id));
            if (scope.Observation is null) { unresolvedUnits.Remove(unit.Id); continue; }
            bool unitComplete = true;
            int accountId = unit.CorpsId ?? world.Corps.Single(c => c.OwnerId == seat.ForceId && c.IsRoot).Id;
            bool returning = unit.Order is { Kind: MilitaryOrderKind.Return, SettlementId: int destination } &&
                targets.Any(t => t.Id == destination && t.OwnerId == seat.ForceId);
            var consolidationTargets = commitments.IsDefault ? [] : commitments.Where(g => g.ForceId == seat.ForceId &&
                g.UnitIds.Contains(unit.Id)).Select(g => g.TargetId).ToArray();
            foreach (var home in targets.Where(t => t.OwnerId == seat.ForceId && !returning && consolidationTargets.Length > 0)
                .OrderBy(t => consolidationTargets.Contains(t.Id) ? 0 : 1)
                .ThenBy(t => MapCoordinates.Distance(unit.Position, rules.Place(t).Position)).ThenBy(t => t.Id))
            {
                if (!Spend()) return Result();
                var retreat = new OrderUnitCommand(Guid.Empty, seat.ForceId, 0, unit.Id, new(MilitaryOrderKind.Return, null, home.Id));
                if (!Legal(scope.ControllerId, retreat)) continue;
                var estimate = Route(unit.Position, unit.FormationId!.Value, unit.NavalFormationId, home, seat.ForceId);
                if (estimate.Status != ArrivalEstimateStatus.Known) continue;
                returns.Add(new(scope.ControllerId, accountId, retreat, [], 0, unit.Soldiers, 0, 0, unit.ServicePlaceId, home.Id, estimate,
                    Purpose: "withdraw:" + unit.Id));
                break;
            }
            if (unit.Order is not null && unit.Order.Kind != MilitaryOrderKind.AttackSettlement)
            { unresolvedUnits.Remove(unit.Id); continue; }
            foreach (var target in targets.Where(t => Enemy(t.OwnerId) && Authorized(scope.ControllerId, t) &&
                (unit.Order is null || t.Id == unit.Order.SettlementId)))
            {
                bool needsOrder = unit.Order is null;
                var repair = new OrderUnitCommand(Guid.Empty, seat.ForceId, 0, unit.Id, new(MilitaryOrderKind.AttackSettlement, null, target.Id));
                if (needsOrder && !Legal(scope.ControllerId, repair)) continue;
                var arrival = Route(unit.Position, unit.FormationId!.Value, unit.NavalFormationId, target, seat.ForceId,
                    Math.Max(unit.DepartureDaysRemaining, unit.Condition == UnitCondition.Confused ? unit.ConditionDaysRemaining : 0),
                    unit.MovementRemainder, unit.Waypoint, unit.Lookahead);
                if (arrival.Status == ArrivalEstimateStatus.Incomplete) unitComplete = false;
                if (arrival.Status != ArrivalEstimateStatus.Known) continue;
                var action = new AllocationCandidate(scope.ControllerId, accountId, needsOrder ? repair : new WaitCommand(Guid.Empty, seat.ForceId, 0), [], 2, 0, 0, 0,
                    unit.ServicePlaceId, target.Id, arrival, Purpose: "campaign");
                int leadership = world.Officers.Where(p => p.UnitId == unit.Id).Select(p => abilities[p.Id].Leadership).DefaultIfEmpty(50).Max();
                forces.Add(new(action, unit.Soldiers, unit.Morale, leadership, rules.Formation(unit).Combat!, rules.Formation(unit).Attack?.DamagesWalls == true,
                    unit.Id, NeedsOrder: needsOrder));
            }
            if (unitComplete) unresolvedUnits.Remove(unit.Id);
        }
        // The planner cannot open another front while a commitment is active. Observe only reinforcements
        // it can actually request; consolidation and retreat have already been handled above.
        foreach (var target in targets.Where(t => Enemy(t.OwnerId) && NeedsNewForce(t))
            .OrderBy(t => activeTargets.Contains(t.Id) ? 0 : 1).ThenBy(NearestControlled).ThenBy(t => t.Id))
        foreach (var (controller, home) in controlled.Where(c => Authorized(c.ControllerId, target))
            .OrderBy(c => MapCoordinates.Distance(rules.Place(settlementsById[c.Place.Id]).Position, rules.Place(target).Position))
            .ThenBy(c => c.Place.Id).Take(3))
        {
            if (!Spend()) return Result();
            var source = settlementsById[home.Id];
            var account = accounts.Single(a => a.Id == home.AccountId);
            var people = home.OfficerIds.OrderByDescending(id => abilities[id].Leadership).ThenBy(id => abilities[id].Politics).ThenBy(id => id).ToArray();
            int administrator = residents[source.Id].Where(p => p.OwnerId == seat.ForceId && !traveling.Contains(p.Id))
                    .OrderByDescending(p => abilities[p.Id].Politics).ThenBy(p => p.Id).Select(p => p.Id).FirstOrDefault(-1);
            foreach (var formation in formations)
            {
                if (!Spend()) return Result();
                int? naval = rules.NavalFormationForDeployment(formation.Id);
                if (!rules.CanAttackTarget(formation.Attack, rules.Place(target).Position, true, rules.IsHarbor(target)) &&
                    (naval is not int shipAttack || !rules.CanAttackTarget(rules.Formations[shipAttack].Attack, rules.Place(target).Position, true, rules.IsHarbor(target)))) continue;
                int memberCapacity = Math.Min(formation.OfficerCapacity, naval is int ship ? rules.Formations[ship].OfficerCapacity : formation.OfficerCapacity);
                // Preserve the best administrator; use a leader and, where possible, a complementary tactic user.
                var members = people.Where(id => id != administrator).Take(Math.Min(2, memberCapacity)).ToImmutableArray();
                // Multiple departures must retain the same defensive commander used by the reserve calculation.
                if (members.Length > 1 && !world.Officers.Any(p => p.IsServing && p.OwnerId == seat.ForceId && p.SettlementId == home.Id &&
                    !members.Contains(p.Id) && abilities[p.Id].Leadership >= members.Skip(1).Max(id => abilities[id].Leadership))) members = [members[0]];
                GameCommand? next = null;
                int preparationDays = 0;
                if (members.IsEmpty)
                {
                    if (world.Simulation.Tasks.Any(t => t.Kind == "personnel-move" && t.Journey?.DestinationPlaceId == source.Id)) continue;
                    foreach (var donor in controlled.Where(d => d.ControllerId == controller && d.Place.Id != source.Id && d.Place.OfficerIds.Length >= 2))
                    {
                        if (!Spend()) return Result();
                        int person = donor.Place.OfficerIds.OrderByDescending(id => abilities[id].Leadership).ThenBy(id => id).First();
                        var donorNeed = support.GarrisonRequirements.FirstOrDefault(r => r.SettlementId == donor.Place.Id && r.DepartingOfficerId == person);
                        if (donorNeed is null || donorNeed.Soldiers > donor.Place.Soldiers) continue;
                        var journey = engine.GetPersonnelJourney(seat.ForceId, person, source.Id, PersonnelJourneyKind.Move);
                        if (!journey.Available || journey.Days is not int days) continue;
                        members = [person];
                        preparationDays = Math.Max(10, (days + 9) / 10 * 10);
                        next = new MoveOfficersCommand(Guid.Empty, seat.ForceId, 0, members, source.Id);
                        break;
                    }
                }
                if (members.IsEmpty) continue;
                uint reserve = support.GarrisonRequirements.Where(r => r.SettlementId == source.Id &&
                    (r.DepartingOfficerId is null || members.Contains(r.DepartingOfficerId.Value))).Select(r => r.Soldiers).DefaultIfEmpty(source.Soldiers).Max();
                // A near mobilization that can beat our departure's return window remains at the original base.
                reserve = (uint)Math.Min(uint.MaxValue, Math.Max(reserve, waves.Where(w => w.TargetId == source.Id && w.Days <= 12).Sum(w => (long)w.Soldiers)));
                uint soldiers = (uint)Math.Min(Math.Max(0, source.Soldiers - (long)reserve), Math.Min(formation.SoldierCapacity,
                    naval is int n ? rules.Formations[n].SoldierCapacity : formation.SoldierCapacity));
                uint projectedGarrison = source.Soldiers;
                long preparationCost = 0;
                uint recruitment = next is null && source.Kind == SettlementKind.City && !source.RecruitUsed
                    ? (uint)DomesticDevelopment.RecruitCount(source, checked((uint)Math.Min(uint.MaxValue, account.Money)), members.Select(id => abilities[id]).ToArray()) : 0;
                if (soldiers < 1000 && recruitment > 0)
                {
                    projectedGarrison += recruitment;
                    soldiers = (uint)Math.Min(Math.Max(0, projectedGarrison - (long)reserve), Math.Min(formation.SoldierCapacity,
                        naval is int shipCapacity ? rules.Formations[shipCapacity].SoldierCapacity : formation.SoldierCapacity));
                    next = new DevelopCommand(Guid.Empty, seat.ForceId, 0, new(seat.ForceId, source.Id, members, DomesticActivity.Recruit) { RecruitAmount = recruitment });
                    preparationDays = 10;
                    preparationCost = (recruitment + 9L) / 10;
                }
                if (soldiers < 1000) continue;
                if (source.Morale < 65 && next is null)
                {
                    if (home.UnavailableActivities.Contains(DomesticActivity.Train)) continue;
                    next = new DevelopCommand(Guid.Empty, seat.ForceId, 0, new(seat.ForceId, source.Id, members, DomesticActivity.Train));
                    int morale = source.Morale;
                    while (morale < 65)
                    {
                        if (!Spend()) return Result();
                        morale += DomesticDevelopment.TrainingIncrement(source.Soldiers, morale, force.Trust,
                            members.Select(id => abilities[id]).ToArray());
                        preparationDays += 10;
                    }
                }
                var tactics = members.ToImmutableDictionary(id => id, id => rules.Tactics.Values.Where(t => t.Selectable &&
                    t.AffectsFacilities && t.CompatibleFormations.Contains(formation.Id) &&
                    (officersById[id].LearnedTactics.Contains(t.Id) || force.ProvidedTactics.Contains(t.Id)))
                    .OrderByDescending(t => t.Power).ThenBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefault());
                var command = new DeployUnitCommand(Guid.Empty, seat.ForceId, 0, source.Id, members, soldiers, formation.Id,
                    new(MilitaryOrderKind.AttackSettlement, null, target.Id)) { NavalFormationId = naval, Tactics = tactics,
                    Policies = new(AutonomousRetreat: true), Ranks = members.ToImmutableDictionary(id => id, _ => BattleRank.Middle) };
                if (!Legal(controller, next ?? command)) continue;
                // Preparation never bypasses a technology qualification on the future formation.
                if (next is not null && new[] { formation, naval is int shipRule ? rules.Formations[shipRule] : formation }.Any(f =>
                    !rules.CanSelectFormation(force, f.Id) ||
                    f.RequiredTacticId is int required && !members.Any(id => officersById[id].LearnedTactics.Contains(required) ||
                        force.ProvidedTactics.Contains(required)))) continue;
                var occupancy = Occupancy(seat.ForceId);
                var start = MilitaryRouteQuery.DeploymentPosition(rules.Travel(formation.Id, naval), rules.SettlementArea(source), occupancy);
                if (start is null) continue;
                var estimate = Route(start.Value, formation.Id, naval, target, seat.ForceId, preparationDays);
                if (estimate.Status != ArrivalEstimateStatus.Known) continue;
                reserve = (uint)Math.Min(uint.MaxValue, Math.Max(reserve,
                    waves.Where(w => w.TargetId == source.Id && w.Days <= estimate.Days + 12).Sum(w => (long)w.Soldiers)));
                soldiers = Math.Min(soldiers, (uint)Math.Max(0, projectedGarrison - (long)reserve));
                if (soldiers < 1000) continue;
                command = command with { Soldiers = soldiers };
                long cost = (long)formation.DeploymentCost + (naval is int vessel ? rules.Formations[vessel].DeploymentCost : 0);
                long food = FoodSupplyForecast.DeploymentExpenditure(projectedGarrison, soldiers, account.HorizonDays, source.Wounded, source.ConsumesFood) +
                    FoodSupplyForecast.ReinforcementExpenditure(source.Soldiers, projectedGarrison - source.Soldiers, account.HorizonDays, source.Wounded, source.ConsumesFood);
                var candidate = new AllocationCandidate(controller, account.Id, next ?? command, members, 2, 0, cost, food,
                    source.Id, target.Id, estimate, Purpose: "campaign")
                    { OfficerOpportunityCost = members.Sum(id => (long)abilities[id].Politics) * Math.Max(1, estimate.Days!.Value / 10), MoneyCost = cost + preparationCost };
                int projectedMorale = next is DevelopCommand { Development.Activity: DomesticActivity.Train } ? 65 :
                    projectedGarrison > source.Soldiers ? (int)((source.Soldiers * (long)source.Morale + recruitment * (long)EconomyMath.RecruitMorale(force.Trust)) / projectedGarrison) : source.Morale;
                forces.Add(new(candidate, soldiers, projectedMorale, members.Max(id => abilities[id].Leadership),
                    formation.Combat!, formation.Attack!.DamagesWalls, TacticValue: tactics.Values.OfType<int>().Sum(id => (long)rules.Tactics[id].Power)));
                if (next is null && recruitment > 0 && soldiers < formation.SoldierCapacity)
                {
                    uint futureGarrison = checked(source.Soldiers + recruitment);
                    uint futureSoldiers = Math.Min(formation.SoldierCapacity, futureGarrison - reserve);
                    if (naval is int navalCapacity) futureSoldiers = Math.Min(futureSoldiers, rules.Formations[navalCapacity].SoldierCapacity);
                    var recruit = new DevelopCommand(Guid.Empty, seat.ForceId, 0, new(seat.ForceId, source.Id, members, DomesticActivity.Recruit) { RecruitAmount = recruitment });
                    if (Legal(controller, recruit)) forces.Add(new(candidate with { Intent = recruit, MoneyCost = cost + (recruitment + 9L) / 10,
                        Arrival = estimate with { Days = estimate.Days + 10 }, AdditionalFood = FoodSupplyForecast.DeploymentExpenditure(futureGarrison,
                            futureSoldiers, account.HorizonDays, source.Wounded, source.ConsumesFood) + FoodSupplyForecast.ReinforcementExpenditure(source.Soldiers,
                            recruitment, account.HorizonDays, source.Wounded, source.ConsumesFood) }, futureSoldiers,
                        (int)((source.Soldiers * (long)source.Morale + recruitment * (long)EconomyMath.RecruitMorale(force.Trust)) / futureGarrison),
                        members.Max(id => abilities[id].Leadership), formation.Combat!, formation.Attack!.DamagesWalls));
                }
            }
        }
        // Position value is optional and runs only after actionable forces and existing commitments.
        foreach (var target in targets.Where(t => t.OwnerId != seat.ForceId && ongoing.Length == 0))
        {
            long mobility = 0;
            if (target.OwnerId != seat.ForceId && rules.TransportFormations is { } transport)
                foreach (var home in targets.Where(s => s.OwnerId == seat.ForceId && s.Id != target.Id)
                    .OrderBy(s => MapCoordinates.Distance(rules.Place(target).Position, rules.Place(s).Position)).ThenBy(s => s.Id).Take(3))
                {
                    if (!Spend()) return Result();
                    // Position value only ranks potential targets; it neither proves reachability nor funds an action.
                    int days = NavigationArrivalForecast.EarliestDays(rules.Travel(transport.LandFormationId, transport.NavalFormationId),
                        rules.Place(target).Position, rules.SettlementArea(home), movementRemainder: 0);
                    mobility += Math.Max(0, 30 - days) * 200L;

                }
            int index = Enumerable.Range(0, places.Count).First(i => places[i].Id == target.Id);
            places[index] = places[index] with { Value = places[index].Value + mobility };
        }
        return Result();
    }
}
