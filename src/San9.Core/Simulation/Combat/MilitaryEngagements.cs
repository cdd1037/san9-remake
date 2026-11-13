using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Directed engagements derived from current orders, ownership and attack reach.</summary>
internal static class MilitaryEngagements
{
    // Cache dependencies belong with the query. Settlements also block water contact and
    // rockfall, so changes to non-attacking buildings can change another actor's target.
    internal static bool SettlementChanged(SettlementState before, SettlementState after) =>
        before.IsDormant != after.IsDormant || before.OwnerId != after.OwnerId ||
        before.CanHostPersons != after.CanHostPersons || before.IsDestroyed != after.IsDestroyed ||
        before.Facility != after.Facility || FacilityCombat.Active(before) != FacilityCombat.Active(after) ||
        !before.EnabledTacticIds.SequenceEqual(after.EnabledTacticIds);

    internal static bool UnitChanged(UnitState before, UnitState after, MilitaryRulebook rules) =>
        before.OwnerId != after.OwnerId || before.Position != after.Position ||
        before.FormationId != after.FormationId || before.NavalFormationId != after.NavalFormationId ||
        before.IsEmbedded != after.IsEmbedded || before.IsPersonnel != after.IsPersonnel ||
        before.EffectiveOrder != after.EffectiveOrder || MilitaryCombat.CanInitiate(before) != MilitaryCombat.CanInitiate(after) ||
        before.FormationId is not null && after.FormationId is not null && rules.UnitFootprint(before) != rules.UnitFootprint(after);

    internal static EngagementRelations Build(IWorldQuery world, MilitaryRulebook military)
    {
        Dictionary<BattleSource, BattleSource> targets = [];
        HashSet<BattleSource> engaged = [];
        Dictionary<BattleSource, BattleSource> contacts = [];
        void Add(BattleSource source, BattleSource target)
        {
            targets.Add(source, target);
            if (source.Area(world, military).IntersectsExpanded(target.Area(world, military), MapCoordinates.UnitsPerCell))
            {
                contacts.Add(source, target);
                if (!source.IsSettlement) engaged.Add(source);
                engaged.Add(target);
            }
        }
        foreach (var unit in world.Units.Values)
            if (FindTarget(world, unit, military) is { } target) Add(new(unit.Id, false), target);
        if (world.Units.Count > 0)
            foreach (var settlement in world.Settlements.Values)
                if (FindFacilityTarget(world, settlement, military) is { } target) Add(new(settlement.Id, true), new(target.Id, false));
        // Prefer an entrant's own close target, then the first attacker in stable identity order.
        var opponents = contacts.Where(p => engaged.Contains(p.Key)).ToDictionary();
        foreach (var (source, target) in contacts.OrderBy(p => p.Key.IsSettlement).ThenBy(p => p.Key.Id))
            opponents.TryAdd(target, source);
        return new(targets, opponents);
    }

    // Movement consumes state retained from the previous combat phase. Only this pre-combat phase expires it.
    internal static void ClearExpired(WorldMutation world, MilitaryRulebook military)
    {
        HashSet<int> activeContacts = [], passiveContacts = [];
        foreach (var source in world.Units.Values)
        {
            if (source.FormationId is null || source.ContactTarget is not { } target) continue;
            if (!RetainsContact(world, source, target, military)) continue;
            activeContacts.Add(source.Id);
            if (!target.IsSettlement) passiveContacts.Add(target.Id);
        }
        foreach (var place in world.Settlements.Values.ToArray())
        {
            if (place.CombatTargetUnitId is not int target) continue;
            if (!place.IsDestroyed && military.TryPlace(place, out _) && world.Units.TryGetValue(target, out var unit) &&
                unit.FormationId is not null && military.SettlementArea(place).IntersectsExpanded(MilitaryCombat.Area(unit, military), MapCoordinates.UnitsPerCell))
                passiveContacts.Add(target);
            else world.Replace(place with { CombatTargetUnitId = null });
        }
        foreach (var unit in world.Units.Values.ToArray())
        {
            bool active = unit.ActiveEngagement && activeContacts.Contains(unit.Id);
            bool passive = unit.PassiveEngagement && passiveContacts.Contains(unit.Id);
            if (active != unit.ActiveEngagement || passive != unit.PassiveEngagement)
                world.Replace(unit with { ActiveEngagement = active, PassiveEngagement = passive });
        }
    }

    internal static void Open(WorldMutation world, BattleSource source, BattleSource target, MilitaryRulebook military)
    {
        bool close = source.Area(world, military).IntersectsExpanded(target.Area(world, military), MapCoordinates.UnitsPerCell);
        if (!source.IsSettlement)
        {
            var unit = world.Units[source.Id];
            var contact = new MapObjectReference(target.Id, target.IsSettlement);
            world.Replace(unit with { ActiveEngagement = unit.ActiveEngagement || close, ContactTarget = contact,
                Lookahead = unit.ContactTarget == contact ? unit.Lookahead : null });
        }
        else world.Replace(world.Settlements[source.Id] with { CombatTargetUnitId = target.Id });
        if (close && !target.IsSettlement) world.Replace(world.Units[target.Id] with { PassiveEngagement = true });
    }

    private static bool RetainsContact(IWorldQuery world, UnitState source, MapObjectReference target, MilitaryRulebook military)
    {
        MapOccupancy? area = target.IsSettlement
            ? world.Settlements.TryGetValue(target.Id, out var place) && !place.IsDestroyed && military.TryPlace(place, out _)
                ? military.SettlementArea(place) : null
            : world.Units.TryGetValue(target.Id, out var unit) && unit.FormationId is not null ? MilitaryCombat.Area(unit, military) : null;
        return source.FormationId is not null && area is { } other &&
            MilitaryCombat.Area(source, military).IntersectsExpanded(other, MapCoordinates.UnitsPerCell) &&
            (military.Formation(source).Attack?.RequiresWaterContact != true ||
                MilitaryCombat.UnitRange(world, source, MilitaryContacts.Attack(target), military) > 0);
    }

    private static BattleSource? FindTarget(IWorldQuery world, UnitState unit, MilitaryRulebook military)
    {
        if (!MilitaryCombat.CanInitiate(unit) || unit.EffectiveOrder is not { IsAttack: true } order ||
            !MilitaryCombat.ValidTarget(world, unit.OwnerId, order, military.AttackFormation(unit, againstSettlement: order.SettlementId is not null), military) ||
            !MilitaryCombat.Area(unit, military).IntersectsExpanded(MilitaryCombat.TargetArea(world, order, military),
                MilitaryCombat.UnitRange(world, unit, order, military))) return null;
        return new(order.UnitId ?? order.SettlementId!.Value, order.UnitId is null);
    }

    private static UnitState? FindFacilityTarget(IWorldQuery world, SettlementState settlement, MilitaryRulebook military)
    {
        if (!FacilityCombat.Active(settlement) || !military.TryPlace(settlement, out _)) return null;
        var attack = military.FacilityAttack(settlement);
        if (attack.Attack is null) return null;
        var area = new BattleSource(settlement.Id, true).Area(world, military);
        int maximumRange = military.MaximumFacilityAttackRange(attack);
        bool work = MilitaryDiplomacy.Work(world, settlement.Id, military);
        UnitState? closest = null;
        long distance = long.MaxValue;
        // Owners rarely differ unit by unit; the hostility of each owner is asked once per facility.
        Dictionary<int, bool>? hostile = null;
        foreach (var unit in world.Units.Values)
        {
            if (unit.IsPersonnel || unit.IsEmbedded || unit.OwnerId == settlement.OwnerId ||
                unit.FormationId is not int formation || military.Formation(unit).Combat is null) continue;
            var targetArea = MilitaryCombat.Area(unit, military);
            // A conservative bound only; exact rockfall, elevation and water rules still
            // decide reachable candidates, in the original selection order. Checked before hostility: both are pure.
            if (!targetArea.IntersectsExpanded(area, maximumRange)) continue;
            if (!work)
            {
                hostile ??= [];
                if (!hostile.TryGetValue(unit.OwnerId, out bool enemy))
                    hostile[unit.OwnerId] = enemy = MilitaryDiplomacy.AutomaticEnemy(world, settlement.OwnerId!.Value, unit.OwnerId);
                if (!enemy) continue;
            }
            long candidateDistance = targetArea.DistanceTo(area);
            if (closest is not null && (candidateDistance > distance || candidateDistance == distance && unit.Id >= closest.Id) ||
                !targetArea.IntersectsExpanded(area, military.FacilityAttackRange(settlement, unit.Position,
                    world.Settlements.Values, attack, area))) continue;
            closest = unit;
            distance = candidateDistance;
        }
        return closest;
    }

    internal static UnitState? FacilityTarget(WorldMutation world, SettlementState settlement, MilitaryRulebook military)
    {
        var target = world.Engagements(military).Target(new(settlement.Id, true));
        return target is { } value ? world.Units[value.Id] : null;
    }

    internal static int Intensity(WorldMutation world, BattleSource subject, BattleSource opponent, MilitaryRulebook military) =>
        world.Engagements(military).Intensity(subject, opponent);

    internal static bool Engaged(WorldMutation world, UnitState unit, MilitaryRulebook military) =>
        world.Engagements(military).Engaged(new(unit.Id, false));
}

// Rules and relations travel together through commits and forks. Mutations drop their own
// reference when an input changes; previews and discarded branches cannot invalidate their owner.
internal sealed record EngagementView(MilitaryRulebook Rules, EngagementRelations Relations);

/// <summary>An immutable derived view of identities; live actor state is read from the querying world.</summary>
internal sealed class EngagementRelations(Dictionary<BattleSource, BattleSource> targets, Dictionary<BattleSource, BattleSource> opponents)
{
    private readonly ILookup<BattleSource, BattleSource> attackers = targets.ToLookup(pair => pair.Value, pair => pair.Key);
    internal IEnumerable<BattleSource> Attackers(BattleSource subject) => attackers[subject];
    internal IEnumerable<BattleSource> Participants => opponents.Keys;
    internal BattleSource Opponent(BattleSource participant) => opponents[participant];
    internal BattleSource? Target(BattleSource source) => targets.TryGetValue(source, out var target) ? target : null;
    internal int IncomingCount(BattleSource subject) => attackers[subject].Count();
    internal bool Engaged(BattleSource subject) => opponents.ContainsKey(subject);
    internal int Intensity(BattleSource subject, BattleSource opponent) =>
        IncomingCount(subject) + (Target(opponent) == subject ? 0 : 1);
}
