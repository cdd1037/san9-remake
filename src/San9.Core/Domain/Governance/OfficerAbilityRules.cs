using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Derives effective abilities from a supplied state. Holds definitions, never mutable world state or cached results.</summary>
public sealed class OfficerAbilityRules(TreasureRulebook treasures, GovernanceRulebook governance)
{
    internal GovernanceRulebook Governance => governance;
    internal TreasureRulebook Treasures => treasures;
    public static OfficerAbilityRules Empty { get; } = new(TreasureRulebook.Empty, new([]));

    public OfficerAbilities Evaluate(OfficerState person, IEnumerable<TreasureState> holdings) =>
        Calculate(person, treasures.HeldBonuses(holdings, person.Id));

    internal OfficerAbilities EvaluateWithTreasureBonus(OfficerState person, OfficerAbilities bonus) => Calculate(person, bonus);

    public ImmutableDictionary<int, OfficerAbilities> EvaluateMany(IEnumerable<OfficerState> people, IEnumerable<TreasureState> holdings)
    {
        var bonuses = holdings.Where(t => t.HolderId is not null && t.Discovery != TreasureDiscovery.Disabled)
            .GroupBy(t => t.HolderId!.Value).ToDictionary(g => g.Key,
                g => TreasureRulebook.MaximumBonuses(g.Select(t => treasures.Items[t.Id].AbilityBonuses)));
        return people.ToImmutableDictionary(p => p.Id, p => Calculate(p, bonuses.GetValueOrDefault(p.Id)));
    }

    private OfficerAbilities Calculate(OfficerState person, OfficerAbilities item)
    {
        var office = person.OfficeId is int id ? governance.Offices[id].AbilityBonuses : default;
        int Current(int value, int itemBonus, int officeBonus) =>
            checked((int)Math.Max(1, (long)value * (10 - person.Injury) / 10) + itemBonus + officeBonus);
        return new(Current(person.BaseAbilities.War, item.War, office.War),
            Current(person.BaseAbilities.Intelligence, item.Intelligence, office.Intelligence),
            Current(person.BaseAbilities.Politics, item.Politics, office.Politics),
            Current(person.BaseAbilities.Leadership, item.Leadership, office.Leadership));
    }
}
