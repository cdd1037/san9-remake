using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed partial class SimulationEngine
{
    public IReadOnlyList<int> EditorCandidates(WorldEditorKind kind) => WorldEditing.Candidates(Snapshot, kind);

    /// <summary>Applies a complete editor draft as one validated, deterministic world transaction.</summary>
    public IReadOnlyList<DomainEvent> EditWorld(WorldEditorKind kind, ImmutableArray<WorldEdit> edits)
    {
        if (_world.IsExecuting || _world.Simulation.Boundary != SimulationBoundary.Stable)
            throw new InvalidOperationException("Editing requires a stable strategy boundary.");
        var candidates = EditorCandidates(kind).ToHashSet();
        if (!Enum.IsDefined(kind) || edits.IsDefaultOrEmpty || edits.Any(e => e is null || !candidates.Contains(e.Id)) ||
            edits.Select(e => e.Id).Distinct().Count() != edits.Length)
            throw new ArgumentException("Invalid editor targets.");
        _world.EnterExecution();
        try
        {
            var mutation = _world.BeginMutation(_rules.Abilities, _rules.Economy);
            foreach (var edit in edits)
            {
                switch (edit)
                {
                    case OfficerEdit p when kind == WorldEditorKind.Officers:
                        var person = mutation.Officers[p.Id];
                        if (ChangedOutside(p.Abilities.War, person.BaseAbilities.War, 1, 100) ||
                            ChangedOutside(p.Abilities.Intelligence, person.BaseAbilities.Intelligence, 1, 100) ||
                            ChangedOutside(p.Abilities.Politics, person.BaseAbilities.Politics, 1, 100) ||
                            ChangedOutside(p.Abilities.Leadership, person.BaseAbilities.Leadership, 1, 100) ||
                            p.Personality is < 0 or > 3 || p.Righteousness is < 0 or > 15 || p.Ambition is < 0 or > 15 ||
                            p.Affinity is < 0 or > 149 || ChangedOutside(p.LifespanTier, person.LifespanTier, 0, 7) ||
                            (person.OwnerId is null ? p.Loyalty is not null : p.Loyalty is null || ChangedOutside(p.Loyalty.Value, person.Loyalty, 0, 100)) ||
                            p.Proficiencies.IsDefault || p.Proficiencies.Length != 9 ||
                            p.Proficiencies.Where((v, i) => ChangedOutside(v, person.TacticProficiencies[i], 0, 1000)).Any() ||
                            p.MasteredCategories.IsDefault || p.MasteredCategories.Any(v => v is < 0 or > 8) ||
                            p.MasteredCategories.Distinct().Count() != p.MasteredCategories.Length ||
                            p.LearnedTactics is null ||
                            p.LearnedTactics.Any(id => !person.LearnedTactics.Contains(id) &&
                                !WorldEditing.OfficerTactics(_rules.Military).Contains(id)))
                            throw new ArgumentException("Invalid officer edit.");
                        mutation.Replace(p.ApplyTo(person));
                        break;
                    case CorpsEdit c when kind == WorldEditorKind.Corps:
                        var treasury = mutation.Corps[c.Id].Treasury;
                        if (ChangedOutside(c.Money, treasury.Money, 0, TreasuryRules.Capacity) || ChangedOutside(c.Food, treasury.Food, 0, TreasuryRules.Capacity))
                            throw new ArgumentException("Invalid treasury edit.");
                        mutation.Replace(c.ApplyTo(mutation.Corps[c.Id]));
                        break;
                    case SettlementEdit s when kind is WorldEditorKind.Cities or WorldEditorKind.Facilities or WorldEditorKind.Obstacles:
                        var place = mutation.Settlements[s.Id];
                        if (ChangedOutside(s.Durability, place.Durability, 1, s.MaxDurability) || s.Soldiers > place.SoldierCapacity ||
                            s.Morale is < 0 or > 100 || s.EnabledTacticIds.IsDefault ||
                            s.EnabledTacticIds.Distinct().Count() != s.EnabledTacticIds.Length)
                            throw new ArgumentException("Invalid settlement edit.");
                        if (kind == WorldEditorKind.Cities)
                        {
                            if (s.PublicOrder is < 0 or > 1000 || ChangedOutside(s.MaxCommerce, place.MaxCommerce, 100, 1000) ||
                                ChangedOutside(s.MaxCultivation, place.MaxCultivation, 100, 1000) || ChangedOutside(s.MaxDurability, place.MaxDurability, 100, 1000) ||
                                s.Commerce < 0 || s.Commerce > s.MaxCommerce || s.Cultivation < 0 || s.Cultivation > s.MaxCultivation ||
                                ChangedOutside(s.Population, place.Population, 3000, 1000000) || ChangedOutside(s.DraftPopulation, place.DraftPopulation, 0, 500000) ||
                                s.EnabledTacticIds.Any(id => !place.EnabledTacticIds.Contains(id) && !WorldEditing.CityTactics(_rules.Military, place).Contains(id)) ||
                                !s.EnabledTacticIds.SequenceEqual(place.EnabledTacticIds) &&
                                (s.EnabledTacticIds.Count(id => _rules.Military!.Tactics[id].Effect != TacticEffect.FacilityAttack) > 1 ||
                                s.EnabledTacticIds.Count(id => _rules.Military!.Tactics[id].Effect == TacticEffect.FacilityAttack) > 1))
                                throw new ArgumentException("Invalid city edit.");
                        }
                        else if (s.PublicOrder != place.PublicOrder || s.Commerce != place.Commerce || s.MaxCommerce != place.MaxCommerce ||
                            s.Cultivation != place.Cultivation || s.MaxCultivation != place.MaxCultivation || s.MaxDurability != place.MaxDurability ||
                            s.Population != place.Population || s.DraftPopulation != place.DraftPopulation || !s.EnabledTacticIds.SequenceEqual(place.EnabledTacticIds) ||
                            kind == WorldEditorKind.Obstacles && (s.Soldiers != place.Soldiers || s.Morale != place.Morale))
                            throw new ArgumentException("Fields are outside the selected editor.");
                        mutation.Replace(s.ApplyTo(place));
                        break;
                    default: throw new ArgumentException("Edit does not match the selected editor.");
                }
            }
            List<DomainEvent> events = [];
            mutation.MaintainDerivedState(events);
            WorldInvariants.Validate(mutation);
            _world.Commit(mutation);
            return events;
        }
        finally { _world.ExitExecution(); }
    }

    private static bool ChangedOutside(long value, long previous, long minimum, long maximum) =>
        value != previous && (value < minimum || value > maximum);
}

public static class WorldEditing
{
    public static IReadOnlyList<int> OfficerTactics(MilitaryRulebook? rules) => rules?.Tactics.Values
        .Where(t => t.Category is not null && (t.Selectable || t.Effect is TacticEffect.FormationTechnology or TacticEffect.Construction or TacticEffect.StoneFormation))
        .Select(t => t.Id).Order().ToArray() ?? [];

    public static IReadOnlyList<int> CityTactics(MilitaryRulebook? rules, SettlementState city)
    {
        if (rules is null || !rules.TryPlace(city, out var place) || place.FacilityTypeId is not int type) return [];
        var attacks = rules.Tactics.Values.Where(t => t.Effect == TacticEffect.FacilityAttack).Select(t => t.Id);
        var active = rules.Facilities[type].Tactics?.TacticPriority.Where(id => rules.Tactics[id].Effect is
            TacticEffect.Martial or TacticEffect.Confusion or TacticEffect.TrapSelection or TacticEffect.Trap or TacticEffect.Defection or TacticEffect.Illusion or TacticEffect.Sorcery) ?? [];
        return attacks.Concat(active).Distinct().Order().ToArray();
    }
    public static IReadOnlyList<int> Candidates(WorldSnapshot world, WorldEditorKind kind)
    {
        var forces = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated && !f.IsBarbarian).Select(f => f.Id).ToHashSet();
        return kind switch
        {
            WorldEditorKind.Officers => world.Officers.Where(p => !p.IsDormant && p.IsVisible).Select(p => p.Id).Order().ToArray(),
            WorldEditorKind.Corps => world.Corps.Where(c => forces.Contains(c.OwnerId)).Select(c => c.Id).Order().ToArray(),
            WorldEditorKind.Cities => world.Settlements.Where(s => Available(s) && s.Kind == SettlementKind.City && s.OwnerId is not null).Select(s => s.Id).Order().ToArray(),
            WorldEditorKind.Facilities => world.Settlements.Where(s => Available(s) && s.Kind == SettlementKind.Facility && s.CanHostPersons && s.OwnerId is not null).Select(s => s.Id).Order().ToArray(),
            WorldEditorKind.Obstacles => world.Settlements.Where(s => Available(s) && s.Kind == SettlementKind.Facility && !s.CanHostPersons).Select(s => s.Id).Order().ToArray(),
            _ => []
        };
    }

    private static bool Available(SettlementState s) => !s.IsDormant && !s.IsDestroyed && s.Facility?.BuilderUnitId is null;
}
