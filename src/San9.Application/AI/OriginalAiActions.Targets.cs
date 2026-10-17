using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed partial class OriginalAiActions
{
    private IEnumerable<Target> Targets(OriginalAiWork work, OriginalAiState state, int? opponent)
    {
        var forces = World.Forces.Where(f => !f.IsDormant && !f.IsEliminated && f.Id != Owner).ToArray();
        int Rank(int id) => state.DiplomaticOrder.Contains(id) ? state.DiplomaticOrder.Length - state.DiplomaticOrder.IndexOf(id) : 0;
        bool Serving(int id) => World.Officers.Count(p => p.OwnerId == id && !p.IsDormant && p.IsServing) >= 2;
        bool Normal(SettlementState s) => !s.IsDormant && !s.IsDestroyed && s.Durability > 0 &&
            s.Facility?.BuilderUnitId is null && s.OwnerId == opponent &&
            (military is null || !military.TryPlace(s, out var place) || place.FacilityTypeId is not int type ||
                !content.OriginalAi.Military.RouteObstacleFacilityIds.Contains(type));
        bool Adjacent(int first, int second) => Geography.Places.Where(p => p.OwnerId == first)
            .Any(p => Geography.Neighbors(p.Id).Any(n => n.OwnerId == second));
        switch (work)
        {
            case OriginalAiWork.FalseReport:
            case OriginalAiWork.Confuse:
                return World.Units.Where(u => !u.IsPersonnel && !u.IsEmbedded && u.FormationId is not null && u.OwnerId != Owner &&
                        u.HasEnteredDailyCycle && u.Soldiers >= 5000 && u.Condition is not (UnitCondition.Disguised or UnitCondition.NoFormation or UnitCondition.Routed or UnitCondition.HighSpirits) &&
                        Threatens(u, work == OriginalAiWork.FalseReport) &&
                        HasFriendlyUnitTarget(u.Id, work == OriginalAiWork.FalseReport) == (work == OriginalAiWork.Confuse))
                    .OrderBy(u => World.Officers.Where(p => p.UnitId == u.Id && p.IsServing).Select(p =>
                        work == OriginalAiWork.FalseReport ? Ability(p).Intelligence : Ability(p).Leadership).DefaultIfEmpty(0).Max())
                    .Select(u => new Target(u.Id)).ToArray();
            case OriginalAiWork.Encourage:
                return World.Units.Where(u => Direct(u) && !u.IsPersonnel && !u.IsEmbedded && u.FormationId is not null &&
                        u.HasEnteredDailyCycle &&
                        u.Condition != UnitCondition.HighSpirits && u.Morale <= 80)
                    .OrderBy(u => u.Morale).Select(u => new Target(u.Id)).ToArray();
            case OriginalAiWork.Reinforce:
                return military is null ? [] : World.Units.Where(u => Direct(u) && !u.IsPersonnel && !u.IsEmbedded && u.FormationId is not null &&
                        u.HasEnteredDailyCycle &&
                        u.Morale >= 25 && u.Order?.Kind != MilitaryOrderKind.Transport && military.Capacity(u) >= (long)u.Soldiers + 3000)
                    .OrderBy(u => u.Soldiers).Select(u => new Target(u.Id)).ToArray();
            case OriginalAiWork.Alienate:
                return World.Officers.Where(p => p.OwnerId == opponent && p.IsVisible && p.IsServing && !p.IsDormant &&
                    p.Loyalty >= 70 && Ability(p).Intelligence <= 90).Select(p => new Target(p.Id)).ToArray();
            case OriginalAiWork.Burn:
            case OriginalAiWork.Seize:
                if (forces.FirstOrDefault(f => f.Id == opponent)?.IsBarbarian != false) return [];
                var buildings = World.Settlements.Where(s => Normal(s) && CommanderLeadership(s) <= 90 &&
                    (work != OriginalAiWork.Burn || s.Durability >= 50) && (work != OriginalAiWork.Seize || AccountMoney(s) > 0));
                return (work == OriginalAiWork.Burn ? buildings.OrderByDescending(s => s.Durability) :
                    buildings.OrderBy(CommanderLeadership)).Select(s => new Target(s.Id)).ToArray();
            case OriginalAiWork.Gossip:
                if (opponent is not int first || !Serving(first)) return [];
                int? second = forces.Where(f => f.Id != first && !f.IsBarbarian && Adjacent(first, f.Id) && Relation(first, f.Id) > 0)
                    .OrderBy(f => Relation(first, f.Id)).Select(f => (int?)f.Id).FirstOrDefault();
                if (second is not int other) return [];
                return OriginalAiStrategy.IsOpponent(World, Owner, other) && Serving(other)
                    ? [new(first, other), new(other, first)] : [new(first, other)];
            case OriginalAiWork.Gift:
                return forces.Where(f => !f.IsBarbarian && Rank(f.Id) > Rank(Owner) &&
                        World.Simulation.Alliance?.TargetForceId != f.Id &&
                        (World.Simulation.Alliance?.MemberForceIds.Contains(Owner) == true || Relation(Owner, f.Id) is >= 20 and < 60))
                    .OrderByDescending(f => Rank(f.Id)).Select(f => new Target(f.Id)).ToArray();
            case OriginalAiWork.Surrender:
                return forces.Where(f => !f.IsBarbarian && Rank(f.Id) < Rank(Owner) && Relation(Owner, f.Id) < 40)
                    .OrderBy(f => Rank(f.Id)).Select(f => new Target(f.Id)).ToArray();
            case OriginalAiWork.ReturnPrisoner:
                return World.Officers.Where(p => !p.IsDormant && p.Status == OfficerStatus.Captive && p.OwnerId == Owner)
                    .Select(p => (Person: p, Holder: Holder(p))).Where(p => p.Holder is int h && forces.Any(f => f.Id == h && !f.IsBarbarian))
                    .OrderByDescending(p => 460L * Best(p.Person) + Sum(p.Person)).Select(p => new Target(p.Person.Id, p.Holder)).ToArray();
            case OriginalAiWork.RequestAttack:
                if (opponent is not int attack) return [];
                return forces.Where(f => !f.IsBarbarian && f.Id != attack && Relation(Owner, f.Id) >= 40 &&
                        OriginalAiStrategy.IsOpponent(World, f.Id, attack) &&
                        !World.Simulation.AttackRequests.Any(r => r.Connects(Owner, f.Id) || r.RequestedForceId == f.Id && r.RemainingXun > 3) &&
                        World.Corps.Where(c => c.OwnerId == f.Id && c.IsRoot).Any(c => c.Treasury.Food >= 2000))
                    .OrderByDescending(f => Relation(f.Id, Owner) - Relation(f.Id, attack))
                    .SelectMany(f => World.Settlements.Where(s => Normal(s) && s.OwnerId == attack && s.Soldiers >= 10000 &&
                            Geography.Neighbors(s.Id).Any(n => n.OwnerId == Owner) && Geography.Neighbors(s.Id).Any(n => n.OwnerId == f.Id))
                        .OrderByDescending(BuildingPriority).Select(s => new Target(f.Id, s.Id))).ToArray();
            default: throw new ArgumentOutOfRangeException(nameof(work));
        }
    }
    private long AccountMoney(SettlementState place) => World.Corps.FirstOrDefault(c => c.OwnerId == place.OwnerId && c.IsRoot)?.Treasury.Money ?? 0;
    private int CommanderLeadership(SettlementState place)
    {
        var commander = World.Officers.FirstOrDefault(p => p.IsCommander && p.IsServing && !p.IsDormant &&
            p.OwnerId == place.OwnerId && p.SettlementId == place.Id && p.UnitId is null);
        return commander is null ? 0 : Ability(commander).Leadership;
    }
    private int? Holder(OfficerState p) => p.UnitId is int unit ? World.Units.FirstOrDefault(u => u.Id == unit)?.OwnerId :
        World.Settlements.FirstOrDefault(s => s.Id == p.SettlementId)?.OwnerId;
    private bool HasFriendlyUnitTarget(int targetId, bool falseReport) =>
        World.Units.Any(own => own.OwnerId == Owner && !own.IsPersonnel &&
            (falseReport ? Direct(own) : !own.IsEmbedded) && own.EffectiveOrder?.UnitId == targetId);
    private bool Threatens(UnitState unit, bool direct)
    {
        var order = unit.EffectiveOrder;
        if (order?.UnitId is int id)
        {
            var target = World.Units.FirstOrDefault(u => u.Id == id);
            return target is null || (direct ? Direct(target) : target.OwnerId == Owner);
        }
        if (order?.SettlementId is int home)
        {
            var target = World.Settlements.FirstOrDefault(s => s.Id == home);
            return target is null || (direct ? Direct(target) : target.OwnerId == Owner);
        }
        return false;
    }
    private int BuildingPriority(SettlementState s)
    {
        if (s.Kind == SettlementKind.City) return 7;
        // Facility roles are authored; executable category numbers do not enter the planner.
        if (military is not null && military.TryPlace(s, out var p) && p.FacilityTypeId is int type)
            return content.OriginalAi.DiplomacyFacilityPriorities.GetValueOrDefault(type);
        return 0;
    }
    private bool Spatial(OriginalAiWork work, OfficerState actor, Target target, OriginalAiWorkRule rule)
    {
        if (work == OriginalAiWork.Alienate || work is OriginalAiWork.Gift or OriginalAiWork.ReturnPrisoner or OriginalAiWork.Surrender) return true;
        if (work == OriginalAiWork.RequestAttack)
            return World.Settlements.Any(s => s.OwnerId == target.Id && s.Kind == SettlementKind.City && InRange(actor, PlacePosition(s.Id), rule));
        if (work == OriginalAiWork.Gossip) return true;
        int? region = work is OriginalAiWork.Burn or OriginalAiWork.Seize ? target.Id :
            World.Units.FirstOrDefault(u => u.Id == target.Id) is { } unit ? military?.RegionAt(unit.Position) : null;
        return region is int home && Near(actor.SettlementId!.Value, home);
    }
    private bool SourceInRange(OriginalAiWork work, OfficerState actor, Target target, OriginalAiWorkRule rule)
    {
        if (work is OriginalAiWork.Gift or OriginalAiWork.ReturnPrisoner or OriginalAiWork.Surrender or OriginalAiWork.Alienate) return true;
        if (work == OriginalAiWork.Gossip)
            return InRange(actor, RulerPosition(target.Id), rule) || InRange(actor, RulerPosition(target.Second!.Value), rule);
        if (work == OriginalAiWork.RequestAttack)
            return World.Settlements.Any(s => s.OwnerId == target.Id && s.Kind == SettlementKind.City && InRange(actor, PlacePosition(s.Id), rule));
        var position = work is OriginalAiWork.Burn or OriginalAiWork.Seize ? PlacePosition(target.Id) :
            World.Units.FirstOrDefault(u => u.Id == target.Id)?.Position;
        return InRange(actor, position, rule);
    }
    private bool ReliefSourceReady(OfficerState actor)
    {
        var source = World.Settlements.Single(s => s.Id == actor.SettlementId);
        return source.Soldiers >= 6000 && (!Geography.Frontier(source.Id, Owner) || source.Soldiers >= 13000);
    }

    private bool RetainsRelief(OfficerState actor)
    {
        if (military is null) return false;
        var source = World.Settlements.Single(s => s.Id == actor.SettlementId);
        int count = 0;
        foreach (var unit in World.Units.Where(u => !u.IsPersonnel && !u.IsEmbedded && u.FormationId is not null &&
                     u.HasEnteredDailyCycle && u.Condition is not (UnitCondition.Routed or UnitCondition.Disguised or UnitCondition.NoFormation) &&
                     u.EffectiveOrder is { Kind: MilitaryOrderKind.AttackSettlement } o && o.SettlementId == source.Id))
        {
            if (PlacePosition(source.Id) is not { } from) continue;
            var travel = military.Travel(unit);
            pathQueries++;
            var route = travel.Map.FindRouteIntoRange(from, new MapOccupancy(unit.Position, military.UnitFootprint(unit)), travel.Shape, travel.Mode, []);
            if (!route.Reachable) return false;
            bool excluded = false;
            foreach (var point in route.Steps)
            {
                var building = World.Settlements.FirstOrDefault(s => s.Id != source.Id && !s.IsDestroyed &&
                    military.SettlementArea(s).Overlaps(new(point, new(1, 1))));
                if (building is null) continue;
                if (military.TryPlace(building, out var place) && place.FacilityTypeId is int type &&
                    content.OriginalAi.Military.RouteObstacleFacilityIds.Contains(type) ||
                    building.OwnerId is int owner && OriginalAiStrategy.IsOpponent(World, unit.OwnerId, owner) ||
                    route.Cost >= 5L * travel.DailyBudget) { excluded = true; break; }
            }
            if (!excluded) count++;
        }
        return source.Soldiers >= Math.Max(10000L, 8000L + Math.Min(20000L, 4000L * count)) + 3000;
    }
}
