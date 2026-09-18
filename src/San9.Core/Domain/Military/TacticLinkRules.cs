using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Authored relationships, independent of affinity and live parent references.</summary>
public sealed record TacticLinkPair(int FirstId, int SecondId, int Bonus, bool SuppressesSiblingBonus);

public sealed record TacticLinkRules(bool RelationshipsEnabled, int KinshipBonus, ImmutableArray<TacticLinkPair> Pairs)
{
    public static TacticLinkRules Empty { get; } = new(false, 0, []);

    internal void Validate()
    {
        if (KinshipBonus is < 0 or > 100 || Pairs.IsDefault ||
            Pairs.Any(p => p.FirstId < 0 || p.SecondId <= p.FirstId || p.Bonus is < 0 or > 100) ||
            Pairs.Select(p => (p.FirstId, p.SecondId)).Distinct().Count() != Pairs.Length)
            throw new ArgumentException("Invalid tactic link relationships.");
    }

    public int Chance(OfficerState source, OfficerState partner, FormationTactics formation) => Chance(source, partner, formation.LinkMultiplier);

    public int Chance(OfficerState source, OfficerState partner, int multiplier)
    {
        int bonus = 0;
        if (RelationshipsEnabled)
        {
            int first = Math.Min(source.Id, partner.Id), second = Math.Max(source.Id, partner.Id);
            var pair = Pairs.FirstOrDefault(p => p.FirstId == first && p.SecondId == second);
            bonus = pair?.Bonus ?? 0;
            if (source.ParentId is int parent && parent == partner.ParentId && pair?.SuppressesSiblingBonus != true ||
                source.ParentId == partner.Id || partner.ParentId == source.Id)
                bonus = KinshipBonus;
        }
        int affinity = PersonnelRulebook.AffinityDistance(source, partner);
        return bonus + partner.TacticLinkBonus + (25 + Math.Max(0, 5 * (5 - affinity))) * multiplier / 2;
    }
}
