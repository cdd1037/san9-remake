using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed partial class OriginalAiMilitary
{
    private Route? UnitTo(UnitState unit, int home) => RouteTo(unit, new(MilitaryOrderKind.Return, null, home), unit.Id);
    private UnitState[] Pressure(int home, bool includeNew = false) => MilitaryIndex.AttackCandidates.Where(u => Active(u) &&
        (includeNew || u.HasEnteredDailyCycle) && ProjectedOrder(u) is
        { Kind: MilitaryOrderKind.AttackSettlement, SettlementId: int target } && target == home).ToArray();
    private long PressureNear(int home, int days) => Pressure(home).Where(u => UnitTo(u, home) is { } r && r.Days < days).Sum(u => (long)u.Soldiers);

    private bool ClearRetreatPressureRoute(SettlementState home, UnitState unit)
    {
        var travel = rules.Travel(unit);
        paths++;
        var route = travel.Map.FindRouteIntoRange(rules.Place(home).Position,
            new(unit.Position, rules.UnitFootprint(unit)), travel.Shape, travel.Mode, []);
        if (!route.Reachable) return false;
        return !route.Steps.Any(point => BuildingAlongRoute(point, building => !building.IsDestroyed && !building.IsDormant &&
            rules.TryPlace(building, out var place) &&
            (place.FacilityTypeId is int type && Policy.RouteObstacleFacilityIds.Contains(type) || building.Id != home.Id &&
                building.OwnerId is int owner && OriginalAiStrategy.IsOpponent(World, unit.OwnerId, owner))) is not null);
    }

    private SettlementState? RetreatBuildingTarget(UnitState unit)
    {
        SettlementState? Building(MilitaryOrder? order) => World.Settlements.FirstOrDefault(s =>
            s.Id == order?.SettlementId && !s.IsDestroyed && !s.IsDormant);
        return Building(ProjectedOrder(unit)) ?? Building(unit.Order);
    }

    private bool BarbarianRecall(UnitState unit)
    {
        if (unit.Order?.Kind is not (MilitaryOrderKind.Move or MilitaryOrderKind.AttackUnit or MilitaryOrderKind.AttackSettlement)) return false;
        var target = RetreatBuildingTarget(unit);
        if (target is not null)
            return target.Kind == SettlementKind.City && target.Soldiers < 10000 &&
                !World.Simulation.Controls.Any(c => c.Kind == ControllerKind.Player && c.ForceId == target.OwnerId);
        return unit.Order?.UnitId is not int targetUnit || !World.Units.Any(u => u.Id == targetUnit && u.Soldiers > 0);
    }

    private void Retreat()
    {
        var processed = new HashSet<int>();
        foreach (var home in Places.Where(Fortified))
        {
            if (!More) break;
            var available = World.Units.Where(u => Direct(u) && Departed(u) &&
                (Corps.ControlMode != CorpsControlMode.Cooperative || !u.CommandedThisXun) &&
                u.Condition is (UnitCondition.Normal or UnitCondition.HighSpirits) && !processed.Contains(u.Id));
            if (Force.IsBarbarian)
            {
                foreach (var unit in available.Where(BarbarianRecall).Where(u => UnitTo(u, home.Id) is not null).ToArray())
                {
                    if (!More) break;
                    Recall(unit, home.Id); processed.Add(unit.Id);
                }
                continue;
            }
            var p = Pressure(home.Id);
            var q = World.Units.Where(u => Departed(u) && u.Order is { Kind: MilitaryOrderKind.AttackSettlement, SettlementId: int target } && target == home.Id).ToArray();
            var pressure = p.Concat(q).Select(u => u.Id).ToHashSet();
            MilitaryEngagementSnapshot? engagements = null;
            bool ThirdCandidate(UnitState unit) => unit.ServicePlaceId == home.Id &&
                unit.Order?.UnitId is int target && World.Units.Any(u => u.Id == target && !u.IsEmbedded) &&
                !(engagements ??= MilitaryEngagementQuery.Capture(World, rules)).Units.Contains(unit.Id);
            // Route time only matters after at least one group's non-spatial predicates pass.
            var candidates = available.Where(u => pressure.Count > 0 && RetreatBuildingTarget(u)?.Id != home.Id || ThirdCandidate(u))
                .Select(u => (Unit: u, Route: UnitTo(u, home.Id))).Where(c => c.Route is not null).ToArray();
            long threshold = 6000 + Math.Min(20000, 4000L * p.Count(u => UnitTo(u, home.Id) is { Days: < 5 } && ClearRetreatPressureRoute(home, u)));
            long middleBudget = home.Soldiers + (pressure.Count == 0 ? 0 : World.Units.Where(u => FieldUnit(u) && u.OwnerId == Owner &&
                u.Condition is (UnitCondition.Normal or UnitCondition.HighSpirits) && u.Order?.Kind == MilitaryOrderKind.Return &&
                RetreatBuildingTarget(u)?.Id == home.Id &&
                UnitTo(u, home.Id) is { Days: < 10 }).Sum(u => u.Soldiers + (long)u.Wounded / 2));
            long firstBudget = home.Soldiers, halfPressure = p.Sum(u => (long)u.Soldiers) / 2;
            var middle = candidates.Where(c => pressure.Count > 0 && RetreatBuildingTarget(c.Unit)?.Id != home.Id && c.Route!.Days < 10 &&
                home.Soldiers < threshold).OrderBy(c => c.Route!.Days).ToArray();
            var third = candidates.Where(c => ThirdCandidate(c.Unit) && c.Route!.Days >= 10).ToArray();
            var middleIds = middle.Select(c => c.Unit.Id).ToHashSet();
            var first = candidates.Where(c => pressure.Count > 0 && !middleIds.Contains(c.Unit.Id) && c.Unit.ServicePlaceId == home.Id &&
                RetreatBuildingTarget(c.Unit)?.Id != home.Id && !(ProjectedOrder(c.Unit)?.UnitId is int target && pressure.Contains(target)))
                .OrderBy(c => c.Route!.Days).ToArray();
            foreach (var c in middle)
            {
                if (!More || middleBudget >= threshold) break;
                if (!processed.Add(c.Unit.Id)) continue;
                long troops = c.Unit.Soldiers + (long)c.Unit.Wounded / 2;
                if (Recall(c.Unit, home.Id)) middleBudget += troops;
            }
            foreach (var c in third)
            {
                if (!More) break;
                if (processed.Add(c.Unit.Id)) Recall(c.Unit, home.Id);
            }
            foreach (var c in first)
            {
                if (!More || firstBudget >= halfPressure) break;
                if (!processed.Add(c.Unit.Id)) continue;
                if (Recall(c.Unit, home.Id)) firstBudget += c.Unit.Soldiers + (long)c.Unit.Wounded / 2;
            }
        }
    }
    private bool Recall(UnitState unit, int home) => Send(Command((id, owner, sequence) =>
        new OrderUnitCommand(id, owner, sequence, unit.Id, new(MilitaryOrderKind.Return, null, home)) { Policies = Policies() }));

    private void Deploy()
    {
        bool crossCorpsRelief = OriginalAiActionPermissions.CorpsOptions(World, engine.Companion, Owner).CrossCorpsRelief;
        var failedSources = new HashSet<int>();
        var rejectedPlans = new List<DeployUnitCommand>();
        var failureWorld = World;
        bool TryPlan(DeployUnitCommand? command)
        {
            if (command is not null && rejectedPlans.Any(previous => SameDeployment(previous, command))) return false;
            if (!Send(command) && command is not null && ReferenceEquals(failureWorld, World))
                rejectedPlans.Add(command);
            return true;
        }
        while (More)
        {
            if (!ReferenceEquals(failureWorld, World))
            {
                failureWorld = World;
                rejectedPlans.Clear();
                failedSources.Clear();
            }
            bool chosen = false;
            var group0 = Places.Where(s => s.ConfusionDaysRemaining == 0 && Pressure(s.Id, Advanced).Any(u => Opponent(u.OwnerId))).Select(s => s.Id).ToHashSet();
            for (int group = 0; group < 2 && !chosen; group++)
            {
                foreach (var source in Places.Where(s => !failedSources.Contains(s.Id) && s.ConfusionDaysRemaining == 0 && s.Soldiers >= 20000 && People(s.Id).Length >= 2 &&
                    (group == 0 ? group0.Contains(s.Id) : !group0.Contains(s.Id))))
                {
                    var people = Commanders(People(source.Id));
                    var basic = Basic([people[0]]);
                    if (basic is null) continue;
                    var regions = InterceptionRegions(source);
                    var targets = World.Units.Where(u => DeploymentCandidate(u) && Opponent(u.OwnerId) &&
                        (group == 0 ? ProjectedOrder(u) is { Kind: MilitaryOrderKind.AttackSettlement, SettlementId: int home } && home == source.Id
                            : u.EffectiveOrder?.Kind == MilitaryOrderKind.Construct) &&
                        rules.RegionAt(u.Position) is int region && regions.Contains(region))
                        .Select(u => (Unit: u, Route: group == 0 ? UnitTo(u, source.Id) :
                            RouteFrom(source, u.FormationId!.Value, u.NavalFormationId,
                                new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = u.Id })))
                        .Where(c => c.Route is not null && c.Route.Days + c.Unit.DepartureDaysRemaining < 20 &&
                            (group != 0 || RouteFrom(source, c.Unit.FormationId!.Value, c.Unit.NavalFormationId,
                                new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = c.Unit.Id }) is
                                { Obstacle: false, HostileBuilding: false })).ToArray();
                    int Score(UnitState target) => Policy.Formations.FirstOrDefault(p => p.Value == target.FormationId).Key switch
                    {
                        San9.Content.Scenarios.OriginalAiFormationRole.Mobile => 80,
                        San9.Content.Scenarios.OriginalAiFormationRole.Crossbow => 160,
                        San9.Content.Scenarios.OriginalAiFormationRole.MixedRanged => 120,
                        San9.Content.Scenarios.OriginalAiFormationRole.SiegeTower => 280,
                        San9.Content.Scenarios.OriginalAiFormationRole.Ram => 200,
                        San9.Content.Scenarios.OriginalAiFormationRole.Catapult => 240,
                        _ => 40,
                    };
                    var preferred = targets.Where(c => Assigned(c.Unit.Id) == 0).ToArray();
                    var ranked = group == 0 ? preferred.Length > 0
                        ? preferred.OrderByDescending(c => Score(c.Unit) - c.Route!.Days - c.Unit.DepartureDaysRemaining)
                            .Concat(targets.Where(c => Assigned(c.Unit.Id) != 0)
                                .OrderBy(c => MapCoordinates.Distance(rules.Place(source).Position, c.Unit.Position))).ToArray()
                        : targets.OrderBy(c => MapCoordinates.Distance(rules.Place(source).Position, c.Unit.Position)).ToArray()
                        : targets.OrderBy(c => c.Route!.Days + c.Unit.DepartureDaysRemaining).ToArray();
                    if (group == 1 && ranked.Length > 0)
                    {
                        int minimum = ranked[0].Route!.Days + ranked[0].Unit.DepartureDaysRemaining;
                        ranked = ranked.Where(c => c.Route!.Days + c.Unit.DepartureDaysRemaining == minimum && c.Unit.Soldiers > Assigned(c.Unit.Id)).ToArray();
                    }
                    if (ranked.Length == 0) continue;
                    int count = Math.Min(5, people.Length / 2);
                    var members = new[] { people[0] }.Concat(people.Skip(1).OrderBy(p => PersonnelRulebook.AffinityDistance(p, people[0])).Take(count - 1)).ToArray();
                    long related = Pressure(source.Id).Count(u => UnitTo(u, source.Id) is { Days: < 8 });
                    uint soldiers = (uint)Math.Max(0, Math.Min(source.Soldiers - (8000 + Math.Min(20000, 4000 * related)), Math.Min(25000, Capacity(people[0]))));
                    foreach (var candidate in ranked)
                    {
                        int wait = group0.Count > 0 ? Math.Max(0, candidate.Route!.Days + candidate.Unit.DepartureDaysRemaining - 10) : candidate.Unit.DepartureDaysRemaining;
                        if (soldiers < 8000) failedSources.Add(source.Id);
                        if (!TryPlan(soldiers < 8000 ? null : Deployment(source, members, soldiers,
                            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = candidate.Unit.Id }, null, candidate.Unit, true, delay: wait))) continue;
                        chosen = true;
                        break;
                    }
                    if (chosen) break;
                }
            }
            if (chosen) continue;
            // Group 2 has a different source order and a single-person plan, with no random selection.
            foreach (var target in World.Settlements.Where(s => s.OwnerId == Owner && (crossCorpsRelief || Direct(s)) &&
                !s.IsDormant && !s.IsDestroyed && s.ConfusionDaysRemaining == 0 &&
                s.Durability > 0 && s.CanHostPersons && s.Facility?.BuilderUnitId is null)
                .Where(s => Fortified(s) || rules.TryPlace(s, out var p) && p.FacilityTypeId is int type && rules.Facilities[type].IsHarbor &&
                Policy.HarborReliefTacticId is int tactic && Force.ProvidedTactics.Contains(tactic))
                .Select(s => (Place: s, Pressure: PressureNear(s.Id, 20) - ReliefDefense(s)))
                .Where(c => c.Pressure > 0).OrderByDescending(c => c.Pressure + (c.Place.Kind == SettlementKind.City ? 500000 : 0)))
            {
                var candidates = Places.Where(s => Transit.Find(s.Id, target.Place.Id) is not null && s.Soldiers > 1000 &&
                    People(s.Id).Length > 0)
                    .Select(s => (Place: s, Basic: Basic([Commanders(People(s.Id))[0]], true))).Where(c => c.Basic is not null)
                    .Select(c => (c.Place, Route: RouteFrom(c.Place, c.Basic!.Value.Land, c.Basic.Value.Naval,
                        new(MilitaryOrderKind.Return, null, target.Place.Id))))
                    .Where(c => c.Route is { Obstacle: false, HostileBuilding: false }).OrderBy(c => c.Route!.CellCount).ToArray();
                foreach (var candidate in candidates)
                {
                    long incoming = Pressure(candidate.Place.Id).Sum(u => (long)u.Soldiers);
                    long usable = candidate.Place.Soldiers - Math.Max(Fortified(candidate.Place) ? 10000 : 0, incoming);
                    if (usable <= 0) continue;
                    var person = Commanders(People(candidate.Place.Id))[0];
                    uint cap = Fortified(candidate.Place) ? Math.Min(10000, Capacity(person)) : Capacity(person);
                    uint amount = (uint)Math.Min(usable, cap);
                    if (!TryPlan(Deployment(candidate.Place, [person], amount,
                        new(MilitaryOrderKind.Return, null, target.Place.Id), target.Place, null, true, true))) continue;
                    chosen = true; break;
                }
                if (chosen) break;
            }
            if (!chosen) break;
        }
    }

    // Identity/sequence are envelopes; rejection reuse depends on the full plan and the exact world above.
    private static bool SameDeployment(DeployUnitCommand first, DeployUnitCommand second) =>
        first.SettlementId == second.SettlementId && first.Soldiers == second.Soldiers &&
        first.FormationId == second.FormationId && first.NavalFormationId == second.NavalFormationId &&
        first.Order == second.Order && first.DepartureDelayDays == second.DepartureDelayDays &&
        first.Waypoint == second.Waypoint && first.Policies == second.Policies && first.ConfirmAttack == second.ConfirmAttack &&
        first.OfficerIds.AsSpan().SequenceEqual(second.OfficerIds.AsSpan()) &&
        first.Tactics.Count == second.Tactics.Count && first.Tactics.All(p => second.Tactics.TryGetValue(p.Key, out var tactic) && tactic == p.Value) &&
        first.Positions.Count == second.Positions.Count && first.Positions.All(p => second.Positions.TryGetValue(p.Key, out var position) && position == p.Value);

    private long ReliefDefense(SettlementState place) => place.Soldiers +
        World.Units.Where(u => FieldUnit(u) &&
            u.EffectiveOrder is not null && u.ServicePlaceId == place.Id).Sum(u => (long)u.Soldiers) +
        World.Units.Where(u => FieldUnit(u) && u.OwnerId == Owner && u.EffectiveOrder is
            { Kind: MilitaryOrderKind.Return, SettlementId: int target } && target == place.Id).Sum(u => (long)u.Soldiers);

    private long TransportBudget(SettlementState place)
    {
        long amount = place.Soldiers + (long)place.Wounded / 2 - BuildingReserve(place);
        foreach (var u in World.Units.Where(u => u.OwnerId == Owner && FieldUnit(u) && u.EffectiveOrder is not null))
            if (u.EffectiveOrder is { Kind: MilitaryOrderKind.Construct, Construction: { } construction } &&
                Policy.OrdinaryFacilityIds.Contains(construction.FacilityTypeId)) continue;
            else if (u.EffectiveOrder is { Kind: MilitaryOrderKind.Transport, SettlementId: int destination })
            { if (destination == place.Id) amount += (long)u.Soldiers + u.Wounded; }
            else if (u.ServicePlaceId == place.Id) amount += ((long)u.Soldiers + u.Wounded) / 2;
        return amount;
    }
    private void Transport()
    {
        bool crossCorpsTransport = OriginalAiActionPermissions.CorpsOptions(World, engine.Companion, Owner).CrossCorpsTransport;
        bool Front(SettlementState s) => ReinforcementArea(s.Id).Any(p => p.OwnerId != Owner && p.OwnerId is int id &&
            !World.Forces.Single(f => f.Id == id).IsBarbarian);
        var geography = Geography;
        var receivers = World.Settlements.Where(s => s.OwnerId == Owner && !s.IsDormant && !s.IsDestroyed &&
            s.Durability > 0 && s.CanHostPersons && s.Facility?.BuilderUnitId is null).ToArray();
        var budgets = receivers.ToDictionary(s => s.Id, TransportBudget);
        var incoming = World.Units.Where(u => u.OwnerId == Owner && FieldUnit(u) && u.EffectiveOrder is
                { Kind: MilitaryOrderKind.Transport or MilitaryOrderKind.Return, SettlementId: not null })
            .GroupBy(u => u.EffectiveOrder!.SettlementId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(u => (long)u.Soldiers + u.Wounded));
        var capacity = receivers.ToDictionary(s => s.Id,
            s => Math.Max(0, (long)s.SoldierCapacity - s.Soldiers - s.Wounded - incoming.GetValueOrDefault(s.Id)));
        var sources = WorkSources.Where(s => budgets[s.Id] > 0 && (!Front(s) || !geography.Priority(s.Id, Owner)) && s.Morale >= 70 &&
            People(s.Id).Length > 0 && (s.Kind != SettlementKind.City || budgets[s.Id] >= 10000))
            .OrderByDescending(s => budgets[s.Id]).Select(s => s.Id).ToArray();
        var targets = receivers.Where(s => (crossCorpsTransport || Direct(s)) &&
            capacity[s.Id] > 0 && budgets[s.Id] > 0 && Front(s) && geography.Priority(s.Id, Owner) &&
            (s.Kind == SettlementKind.City || World.Officers.Any(p => p.SettlementId == s.Id && p.IsServing)))
            .OrderBy(s => budgets[s.Id]).Select(s => s.Id).ToList();
        foreach (int home in sources)
        {
            if (!More || targets.Count == 0) break;
            var source = WorkSources.FirstOrDefault(s => s.Id == home);
            if (source is null) continue;
            var person = People(home).OrderBy(p => (long)Ability(p).War + Ability(p).Intelligence + Ability(p).Politics + Ability(p).Leadership).FirstOrDefault();
            if (person is null) continue;
            foreach (int destination in targets.Where(id => Transit.Find(home, id) is not null)
                .OrderBy(id => Transit.Find(home, id)!.Reference.Days).ToArray())
            {
                uint soldiers = (uint)Math.Min(capacity[destination], Math.Min(10000, source.Soldiers));
                bool fallback = source.Kind != SettlementKind.City && soldiers == source.Soldiers;
                var transit = fallback ? Basic([person], true) : rules.TransportFormations is { } transport
                    ? (transport.LandFormationId, transport.NavalFormationId) : ((int Land, int? Naval)?)null;
                if (transit is not { } pair || RouteFrom(source, pair.Land, pair.Naval,
                    new(fallback ? MilitaryOrderKind.Return : MilitaryOrderKind.Transport, null, destination),
                    fallback ? Policies(true) : new()) is not { Obstacle: false, HostileRegion: false }) continue;
                GameCommand? command;
                if (fallback) command = Deployment(source, [person], Math.Min(soldiers, Capacity(person)),
                    new(MilitaryOrderKind.Return, null, destination), World.Settlements.Single(s => s.Id == destination), null, false, true);
                else
                {
                    command = Command((id, owner, sequence) => new TransportUnitCommand(id, owner, sequence, home, [person.Id], soldiers, destination));
                }
                targets.Remove(destination);
                Send(command); break;
            }
        }
    }
}
