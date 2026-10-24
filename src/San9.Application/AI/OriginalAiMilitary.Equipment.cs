using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

internal sealed partial class OriginalAiMilitary
{
    private TacticRule[] Tactics(OfficerState person)
    {
        var available = TacticAvailability.ForOfficer(person, Force, World.Treasures, Treasures);
        return rules.Tactics.Values.Where(t => t.Selectable && t.Category is 0 or 1 or 2 or 3 or 6 or 7 or 8 &&
            available.ContainsKey(t.Id)).OrderBy(t => t.Id).ToArray();
    }
    private TacticRule? Draw(IEnumerable<TacticRule> source)
    {
        var all = source.ToArray(); return all.Length == 0 ? null : all[random.Next(all.Length)];
    }
    private int? Tactic(OfficerState person, List<int> assigned, UnitState? target, bool slow, bool deployment, bool fallback, bool harbor)
    {
        var all = Tactics(person);
        TacticRule? Effect(TacticEffect effect) => all.FirstOrDefault(t => t.Effect == effect);
        bool Assigned(TacticEffect effect) => assigned.Any(id => rules.Tactics[id].Effect == effect);
        bool Equipped(params TacticEffect[] effects) => target is not null &&
            World.Officers.Any(p => p.UnitId == target.Id &&
                p.EquippedTacticId is int id && rules.Tactics.TryGetValue(id, out var tactic) && effects.Contains(tactic.Effect));
        if (fallback)
        {
            if (Effect(TacticEffect.Inspire) is { } inspire) return inspire.Id;
            var categories = new[] { 0, 1, 2, 3, 7, 8 }.Where(c => all.Any(t => t.Category == c))
                .OrderByDescending(c => person.TacticProficiencies[c]).ToArray();
            return categories.Length > 0 ? all.Where(t => t.Category == categories[0]).OrderByDescending(t => t.Id).First().Id : Draw(all)?.Id;
        }
        var rare = all.Where(t => t.Effect is TacticEffect.Sorcery or TacticEffect.Illusion or TacticEffect.Heal).ToArray();
        if (rare.Length > 0) return Draw(rare)!.Id;
        int chance = 2 * (Ability(person).Intelligence - Ability(person).War);
        var ordinary = all.Where(t => t.Effect is TacticEffect.Martial or TacticEffect.Illusion or TacticEffect.Heal or TacticEffect.Sorcery).ToArray();
        if (deployment)
        {
            if (Effect(TacticEffect.CounterTrap) is { } trap && !Assigned(TacticEffect.CounterTrap) && Equipped(TacticEffect.TrapSelection, TacticEffect.Trap) &&
                (slow || ordinary.Length == 0 || random.Percent(chance))) return trap.Id;
            if (Effect(TacticEffect.CounterConfusion) is { } counter && !Assigned(TacticEffect.CounterConfusion) &&
                Equipped(TacticEffect.Confusion, TacticEffect.Defection, TacticEffect.Illusion, TacticEffect.TrapSelection) &&
                (slow || ordinary.Length == 0 || random.Percent(chance))) return counter.Id;
            var strategic = new[] { TacticEffect.TrapSelection, TacticEffect.Defection, TacticEffect.Confusion, TacticEffect.Taunt }
                .Select(Effect).OfType<TacticRule>().ToArray();
            if (strategic.Length > 0 && (ordinary.Length == 0 || random.Percent(chance) || slow)) return strategic[0].Id;
            if (ordinary.Length > 0) return ordinary.OrderByDescending(t => t.Power)
                .ThenByDescending(t => person.TacticProficiencies[t.Category!.Value]).First().Id;
            return Draw(all.Where(t => t.Effect is TacticEffect.Inspire or TacticEffect.CounterConfusion or TacticEffect.CounterTrap))?.Id;
        }
        if (slow && harbor)
        {
            foreach (var effect in new[] { TacticEffect.CounterTrap, TacticEffect.CounterConfusion, TacticEffect.Defection,
                TacticEffect.Confusion, TacticEffect.TrapSelection, TacticEffect.Taunt })
                if (Effect(effect) is { } special && (effect is not (TacticEffect.CounterTrap or TacticEffect.CounterConfusion) || !Assigned(effect))) return special.Id;
        }
        var ranged = all.Where(t => t.Category is 2 or 3 && t.Effect == TacticEffect.Martial).ToArray();
        var strategicRandom = all.Where(t => t.Effect is TacticEffect.CounterConfusion or TacticEffect.Confusion or TacticEffect.Defection).ToArray();
        if (!Assigned(TacticEffect.CounterConfusion) && strategicRandom.Length > 0 && (ranged.Length == 0 || random.Percent(chance)))
            return Draw(strategicRandom)!.Id;
        if (ranged.Length > 0) return ranged.OrderByDescending(t => t.Power).First().Id;
        var subset = all.Where(t => t.Effect is TacticEffect.CounterTrap or TacticEffect.TrapSelection or TacticEffect.Taunt or TacticEffect.Inspire).ToArray();
        if (Ability(person).Intelligence > Ability(person).War && subset.Length > 0) return Draw(subset)!.Id;
        var remaining = ordinary.Concat(subset).Concat(all.Where(t => t.Effect == TacticEffect.CounterConfusion)).ToArray();
        if (remaining.Length == 0) return null;
        int maximumPower = remaining.Max(t => t.Power);
        return Draw(remaining.Where(t => t.Power == maximumPower))!.Id;
    }

    private (int Land, int? Naval)? Equipment(OfficerState[] people, IReadOnlyDictionary<int, int?> tactics,
        SettlementState? building, UnitState? target, Route route, bool deployment, bool fallback)
    {
        var land = AvailableFormations(people, false, Corps.Treasury.Money);
        FormationRule? Role(OriginalAiFormationRole role) => Policy.Formations.TryGetValue(role, out int id) ? land.FirstOrDefault(f => f.Id == id) : null;
        FormationRule? First(params OriginalAiFormationRole[] roles) => roles.Select(Role).OfType<FormationRule>().FirstOrDefault();
        FormationRule? selected = null;
        var categories = tactics.Values.OfType<int>().Select(id => rules.Tactics[id].Category).ToArray();
        bool harbor = building is not null && rules.TryPlace(building, out var place) && place.FacilityTypeId is int type && rules.Facilities[type].IsHarbor;
        if (fallback)
        {
            selected = Role(OriginalAiFormationRole.March);
            if (selected is null) return null;
        }
        else if (Force.IsBarbarian)
        {
            selected = land.FirstOrDefault(f => !f.IsCommon);
            if (selected is null) return null;
        }
        else if (deployment)
        {
            int f = categories.Count(c => c == 0), h = categories.Count(c => c == 1), b = categories.Count(c => c == 2), c = categories.Count(c => c == 3);
            if (f + h + b + c == 0) selected = Role(OriginalAiFormationRole.Defense);
            else if (h >= f && h >= h + b && h >= b + c && (target is null ||
                !World.Officers.Any(p => p.UnitId == target.Id && target.MemberRanks.GetValueOrDefault(p.Id) == BattleRank.Front) ||
                World.Officers.Where(p => p.UnitId == target.Id).Max(p => Ability(p).War) < people.Max(p => Ability(p).War))) selected = Role(OriginalAiFormationRole.Cavalry);
            if (selected is null && b + c >= f && b + c >= h && b + c >= h + b) selected = Role(OriginalAiFormationRole.MixedRanged);
            if (selected is null && h + b >= f && h + b >= h && h + b >= b + c) selected = Role(OriginalAiFormationRole.Mobile);
            if (selected is null && f >= h && f >= h + b && f >= b + c)
                selected = Role(random.Next(2) == 0 ? OriginalAiFormationRole.Infantry : OriginalAiFormationRole.Envelopment);
        }
        else
        {
            if (!route.Obstacle && route.Slow && harbor)
                selected = Role(OriginalAiFormationRole.March);
            else
            {
                bool siege = route.Obstacle;
                if (!siege && building is not null && First(OriginalAiFormationRole.Elephant, OriginalAiFormationRole.Ram, OriginalAiFormationRole.Catapult) is not null)
                    foreach (var u in World.Units.Where(u => Departed(u) && ProjectedOrder(u)?.Kind == MilitaryOrderKind.AttackSettlement &&
                        ProjectedOrder(u)?.SettlementId == building.Id && Policy.Formations.Any(p =>
                            p.Key is OriginalAiFormationRole.Elephant or OriginalAiFormationRole.Ram && p.Value == u.FormationId)))
                        siege = random.Percent((500 - building.Durability) / 5);
                if (siege) selected = First(OriginalAiFormationRole.Elephant, OriginalAiFormationRole.Ram, OriginalAiFormationRole.Catapult);
            }
            if (selected is null)
            {
                if (categories.Contains(3)) selected = First(OriginalAiFormationRole.SiegeTower, OriginalAiFormationRole.Catapult, OriginalAiFormationRole.MixedRanged);
                else if (categories.Contains(2)) selected = First(OriginalAiFormationRole.SiegeTower, OriginalAiFormationRole.Catapult, OriginalAiFormationRole.Mobile);
            }
        }
        selected ??= land.OrderByDescending(f => f.Combat?.AgainstGarrison ?? 0).FirstOrDefault();
        if (selected is null) return null;
        var water = AvailableFormations(people, true, Corps.Treasury.Money - (long)selected.DeploymentCost)
            .Where(f => rules.ValidFormationPair(selected.Id, f.Id)).ToArray();
        int? naval = !fallback && route.Slow && (deployment || harbor)
            ? Policy.NavalPriority.Select(id => water.FirstOrDefault(f => f.Id == id)).OfType<FormationRule>().FirstOrDefault()?.Id : null;
        naval ??= water.FirstOrDefault(f => f.Id == rules.BasicNavalFormationId)?.Id;
        if (Force.IsBarbarian && !fallback)
        {
            naval = water.FirstOrDefault(f => !f.IsCommon)?.Id;
            if (naval is null && rules.BasicNavalFormationId is not null) return null;
        }
        return (selected.Id, naval);
    }

    private DeployUnitCommand? Deployment(SettlementState source, OfficerState[] people, uint soldiers, MilitaryOrder order,
        SettlementState? building, UnitState? target, bool deployment, bool fallback = false, int delay = 0)
    {
        var basic = Basic(people, fallback);
        if (basic is null) return null;
        var initial = RouteFrom(source, basic.Value.Land, basic.Value.Naval, order, Policies(fallback));
        if (initial is null) return null;
        bool harbor = building is not null && rules.TryPlace(building, out var place) &&
            place.FacilityTypeId is int type && rules.Facilities[type].IsHarbor;
        var tactics = new Dictionary<int, int?>();
        var assigned = new List<int>();
        foreach (var p in people)
        {
            int? tactic = Tactic(p, assigned, target, initial.Slow, deployment, fallback, harbor);
            tactics.Add(p.Id, tactic); if (tactic is int id) assigned.Add(id);
        }
        var equipment = Equipment(people, tactics, building, target, initial, deployment, fallback);
        if (equipment is not { } pair) return null;
        var route = RouteFrom(source, pair.Land, pair.Naval, order, Policies(fallback));
        if (route is null) return null;
        soldiers = Math.Min(soldiers, rules.Formations[pair.Land].SoldierCapacity);
        if (pair.Naval is int naval) soldiers = Math.Min(soldiers, rules.Formations[naval].SoldierCapacity);
        uint minimum = fallback ? 1u : deployment ? 8000u : MinimumAttack(source);
        if (soldiers < minimum) return null;
        var ordered = people.OrderByDescending(p => Ability(p).War).ToArray();
        int start = Math.Clamp(Math.Max(0, 85 - Ability(ordered[0]).War) / 20, 0, 5 - ordered.Length);
        var formation = rules.Formations[pair.Land];
        var ranks = ordered.Select((p, i) => (p.Id, Rank: fallback ? formation.CaptureRanks.Length >= 5 ? formation.CaptureRanks[4] : BattleRank.Middle : formation.CaptureRanks.IsEmpty
            ? (BattleRank)Math.Min(2, (start + i) / 2) : formation.CaptureRanks[Math.Min(start + i, formation.CaptureRanks.Length - 1)]))
            .ToImmutableDictionary(p => p.Id, p => p.Rank);
        var command = (DeployUnitCommand)Command((id, owner, sequence) => new DeployUnitCommand(id, owner, sequence, source.Id,
            people.Select(p => p.Id).ToImmutableArray(), soldiers, pair.Land, order)
            { NavalFormationId = pair.Naval, Tactics = tactics.ToImmutableDictionary(), Ranks = ranks,
                DepartureDelayDays = Math.Clamp(delay, 0, 9), Policies = Policies(fallback), ConfirmAttack = order.IsAttack });
        return Legal(command) ? command : null;
    }
}
