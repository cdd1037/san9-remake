using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed partial class OriginalAiMilitary
{
    private Route? UnitTo(UnitState unit, int home) => RouteTo(unit, new(MilitaryOrderKind.Return, null, home), unit.Id);
    private UnitState[] Pressure(int home) => World.Units.Where(u => Departed(u) && KnownOrder(u) is
        { Kind: MilitaryOrderKind.AttackSettlement, SettlementId: int target } && target == home).ToArray();
    private long PressureNear(int home, int days) => Pressure(home).Where(u => UnitTo(u, home) is { } r && r.Days < days).Sum(u => (long)u.Soldiers);

    private bool BarbarianRecall(UnitState unit)
    {
        if (KnownMain(unit)?.IsAttack != true) return false;
        SettlementState? Building(MilitaryOrder? order) => World.Settlements.FirstOrDefault(s =>
            s.Id == order?.SettlementId && !s.IsDestroyed && !s.IsDormant);
        var target = Building(KnownOrder(unit)) ?? Building(KnownMain(unit));
        if (target is not null)
            return target.Kind == SettlementKind.City && target.Soldiers < 10000 &&
                !World.Simulation.Controls.Any(c => c.ControllerId == "player" && c.ForceId == target.OwnerId);
        return KnownMain(unit)?.UnitId is not int targetUnit || !World.Units.Any(u => u.Id == targetUnit && u.Soldiers > 0);
    }

    private void Retreat()
    {
        var processed = new HashSet<int>();
        foreach (var home in Places.Where(Fortified))
        {
            if (!More) break;
            var p = Pressure(home.Id);
            var q = World.Units.Where(u => Departed(u) && KnownMain(u) is { Kind: MilitaryOrderKind.AttackSettlement, SettlementId: int target } && target == home.Id).ToArray();
            var pressure = p.Concat(q).Select(u => u.Id).ToHashSet();
            var candidates = World.Units.Where(u => Direct(u) && Active(u) && !u.CommandedThisXun && !processed.Contains(u.Id))
                .Select(u => (Unit: u, Route: UnitTo(u, home.Id))).Where(c => c.Route is not null).ToArray();
            if (Force.IsBarbarian)
            {
                foreach (var c in candidates.Where(c => BarbarianRecall(c.Unit)))
                {
                    if (!More) break;
                    Recall(c.Unit, home.Id); processed.Add(c.Unit.Id);
                }
                continue;
            }
            long threshold = 6000 + Math.Min(20000, 4000L * p.Count(u => UnitTo(u, home.Id) is { Days: < 5 }));
            long middleBudget = home.Soldiers + World.Units.Where(u => Active(u) && u.OwnerId == Owner &&
                KnownOrder(u) is { Kind: MilitaryOrderKind.Return, SettlementId: int target } && target == home.Id &&
                UnitTo(u, home.Id) is { Days: < 10 }).Sum(u => u.Soldiers + (long)u.Wounded / 2);
            long firstBudget = home.Soldiers, halfPressure = p.Sum(u => (long)u.Soldiers) / 2;
            var middle = candidates.Where(c => pressure.Count > 0 && KnownOrder(c.Unit)?.SettlementId != home.Id && c.Route!.Days < 10 &&
                home.Soldiers < threshold).OrderBy(c => c.Route!.Days).ToArray();
            var third = candidates.Where(c => c.Unit.EngagementTargetId is null && c.Unit.ServicePlaceId == home.Id &&
                KnownMain(c.Unit)?.UnitId is int target && World.Units.Any(u => u.Id == target && !u.IsEmbedded) && c.Route!.Days >= 10).ToArray();
            var middleIds = middle.Select(c => c.Unit.Id).ToHashSet();
            var first = candidates.Where(c => pressure.Count > 0 && !middleIds.Contains(c.Unit.Id) && c.Unit.ServicePlaceId == home.Id &&
                KnownOrder(c.Unit)?.SettlementId != home.Id && !(KnownOrder(c.Unit)?.UnitId is int target && pressure.Contains(target)))
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
        var consumed = new HashSet<(int, int)>();
        var consumedRelief = new HashSet<(int Source, int Target)>();
        while (More)
        {
            bool chosen = false;
            var group0 = Places.Where(s => World.Units.Any(u => Departed(u) && Opponent(u.OwnerId) &&
                KnownOrder(u)?.SettlementId == s.Id)).Select(s => s.Id).ToHashSet();
            for (int group = 0; group < 2 && !chosen; group++)
            {
                foreach (var source in Places.Where(s => s.Soldiers >= 20000 && People(s.Id).Length >= 2 &&
                    (group == 0 ? group0.Contains(s.Id) : !group0.Contains(s.Id))))
                {
                    var people = Commanders(People(source.Id));
                    var basic = Basic([people[0]]);
                    if (basic is null) continue;
                    var targets = World.Units.Where(u => Departed(u) && Opponent(u.OwnerId) && !consumed.Contains((source.Id, u.Id)))
                        .Select(u => (Unit: u, Route: RouteFrom(source, u.FormationId!.Value, u.NavalFormationId,
                            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = u.Id })))
                        .Where(c => c.Route is not null && c.Route.Days + c.Unit.DepartureDaysRemaining < 20).ToArray();
                    long Assigned(UnitState target) => World.Units.Where(u => u.OwnerId == Owner && KnownOrder(u)?.UnitId == target.Id).Sum(u => (long)u.Soldiers);
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
                    var preferred = targets.Where(c => Assigned(c.Unit) == 0).ToArray();
                    var ranked = group == 0 ? preferred.Length > 0
                        ? preferred.OrderByDescending(c => Score(c.Unit) - c.Route!.Days - c.Unit.DepartureDaysRemaining).ToArray()
                        : targets.OrderBy(c => MapCoordinates.Distance(rules.Place(source).Position, c.Unit.Position)).ToArray()
                        : targets.OrderBy(c => c.Route!.Days + c.Unit.DepartureDaysRemaining).ToArray();
                    if (group == 1 && ranked.Length > 0)
                    {
                        int minimum = ranked[0].Route!.Days + ranked[0].Unit.DepartureDaysRemaining;
                        ranked = ranked.Where(c => c.Route!.Days + c.Unit.DepartureDaysRemaining == minimum && c.Unit.Soldiers > Assigned(c.Unit)).ToArray();
                    }
                    if (ranked.Length == 0) continue;
                    var candidate = ranked[0];
                    consumed.Add((source.Id, candidate.Unit.Id)); chosen = true;
                    int count = Math.Min(5, people.Length / 2);
                    var members = new[] { people[0] }.Concat(people.Skip(1).OrderBy(p => PersonnelRulebook.AffinityDistance(p, people[0])).Take(count - 1)).ToArray();
                    long related = World.Units.Count(u => Departed(u) && KnownOrder(u)?.SettlementId == source.Id && UnitTo(u, source.Id) is { Days: < 8 });
                    uint soldiers = (uint)Math.Max(0, Math.Min(source.Soldiers - (8000 + Math.Min(20000, 4000 * related)), Math.Min(25000, Capacity(people[0]))));
                    int wait = group0.Count > 0 ? Math.Max(0, candidate.Route!.Days + candidate.Unit.DepartureDaysRemaining - 10) : candidate.Unit.DepartureDaysRemaining;
                    Send(count < 2 || soldiers < 8000 ? null : Deployment(source, members, soldiers,
                        new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = candidate.Unit.Id }, null, candidate.Unit, true, delay: wait));
                    break;
                }
            }
            if (chosen) continue;
            // Group 2 has a different source order and a single-person plan, with no random selection.
            foreach (var target in Places.Where(s => Fortified(s) || rules.TryPlace(s, out var p) && p.FacilityTypeId is int type && rules.Facilities[type].IsHarbor &&
                Policy.HarborReliefTacticId is int tactic && Force.ProvidedTactics.Contains(tactic))
                .Select(s => (Place: s, Pressure: PressureNear(s.Id, 20) - s.Soldiers - World.Units.Where(u => u.OwnerId == Owner &&
                    KnownOrder(u)?.SettlementId == s.Id).Sum(u => (long)u.Soldiers)))
                .Where(c => c.Pressure > 0).OrderByDescending(c => c.Pressure + (c.Place.Kind == SettlementKind.City ? 500000 : 0)))
            {
                var candidates = Places.Where(s => s.Id != target.Place.Id && Near(s.Id, target.Place.Id) && s.Soldiers > 1000 &&
                    People(s.Id).Length > 0 && !consumedRelief.Contains((s.Id, target.Place.Id)))
                    .Select(s => (Place: s, Basic: Basic([People(s.Id)[0]], true))).Where(c => c.Basic is not null)
                    .Select(c => (c.Place, Route: RouteFrom(c.Place, c.Basic!.Value.Land, c.Basic.Value.Naval,
                        new(MilitaryOrderKind.Return, null, target.Place.Id))))
                    .Where(c => c.Route is not null && !c.Route.Obstacle).OrderBy(c => c.Route!.Steps.Length).ToArray();
                foreach (var candidate in candidates)
                {
                    long usable = candidate.Place.Soldiers - Math.Max(Fortified(candidate.Place) ? 10000 : 0, PressureNear(candidate.Place.Id, 20));
                    consumedRelief.Add((candidate.Place.Id, target.Place.Id));
                    if (usable <= 0) continue;
                    var person = People(candidate.Place.Id)[0];
                    uint amount = (uint)Math.Min(usable, Math.Min(10000, Capacity(person)));
                    Send(Deployment(candidate.Place, [person], amount, new(MilitaryOrderKind.Return, null, target.Place.Id), target.Place, null, true, true));
                    chosen = true; break;
                }
                if (chosen) break;
            }
            if (!chosen) break;
        }
    }

    private long TransportBudget(SettlementState place)
    {
        long amount = place.Soldiers + (long)place.Wounded / 2 - BuildingReserve(place);
        foreach (var u in World.Units.Where(u => u.OwnerId == Owner && Active(u)))
            if (KnownOrder(u) is { Kind: MilitaryOrderKind.Transport, SettlementId: int destination })
            { if (destination == place.Id) amount += (long)u.Soldiers + u.Wounded; }
            else if (u.ServicePlaceId == place.Id) amount += ((long)u.Soldiers + u.Wounded) / 2;
        return amount;
    }
    private void Transport()
    {
        bool Front(SettlementState s) => Related(s.Id).Any(p => p.OwnerId != Owner && p.OwnerId is int id &&
            !World.Forces.Single(f => f.Id == id).IsBarbarian);
        var geography = new OriginalAiGeography(content, rules, World);
        var budgets = Places.ToDictionary(s => s.Id, TransportBudget);
        var sources = Places.Where(s => budgets[s.Id] > 0 && (!Front(s) || !geography.Priority(s.Id, Owner)) && s.Morale >= 70 &&
            People(s.Id).Length > 0 && (s.Kind != SettlementKind.City || budgets[s.Id] >= 10000))
            .OrderByDescending(s => budgets[s.Id]).Select(s => s.Id).ToArray();
        var targets = Places.Where(s => budgets[s.Id] > 0 && Front(s) && geography.Priority(s.Id, Owner) &&
            (s.Kind == SettlementKind.City || World.Officers.Any(p => p.SettlementId == s.Id && p.IsServing)))
            .OrderBy(s => budgets[s.Id]).Select(s => s.Id).ToList();
        foreach (int home in sources)
        {
            if (!More || targets.Count == 0) break;
            var source = Places.FirstOrDefault(s => s.Id == home);
            if (source is null) continue;
            var person = People(home).OrderBy(p => (long)Ability(p).War + Ability(p).Intelligence + Ability(p).Politics + Ability(p).Leadership).FirstOrDefault();
            if (person is null) continue;
            foreach (int destination in targets.OrderBy(id => Near(home, id) ? 0 : 1).ToArray())
            {
                uint soldiers = Math.Min(10000, source.Soldiers);
                bool fallback = source.Kind != SettlementKind.City && soldiers == source.Soldiers;
                GameCommand? command;
                if (fallback) command = Deployment(source, [person], Math.Min(soldiers, Capacity(person)),
                    new(MilitaryOrderKind.Return, null, destination), World.Settlements.Single(s => s.Id == destination), null, false, true);
                else
                {
                    if (rules.TransportFormations is not { } pair || RouteFrom(source, pair.LandFormationId, pair.NavalFormationId,
                        new(MilitaryOrderKind.Transport, null, destination)) is not { Obstacle: false }) continue;
                    command = Command((id, owner, sequence) => new TransportUnitCommand(id, owner, sequence, home, [person.Id], soldiers, destination));
                }
                targets.Remove(destination);
                Send(command); break;
            }
        }
    }
}
