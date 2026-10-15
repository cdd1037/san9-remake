using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Expected martial contribution, scaled by 10000. Never substitutes for a conservative feasibility check.</summary>
public sealed record CampaignTacticEstimate(long Damage, long OrdinaryHits, long OfficerExposure)
{
    public const int Scale = 10000;
}

internal sealed record CampaignEquipment(ImmutableDictionary<int, int?> Tactics,
    ImmutableDictionary<int, BattleRank> Ranks, CampaignTacticEstimate Estimate);

/// <summary>Own tactic availability in one frozen observation, shared by field and campaign planning.</summary>
internal sealed class KnownTactics(SimulationEngine engine, WorldSnapshot world, int forceId, MilitaryRulebook rules)
{
    private readonly ForceState force = world.Forces.Single(f => f.Id == forceId);
    private readonly ILookup<int, TreasureState> treasures = world.Treasures.Where(t => t.Discovered && t.HolderId is not null)
        .ToLookup(t => t.HolderId!.Value);
    private readonly Dictionary<int, ImmutableArray<TacticRule>> known = [];

    internal ImmutableArray<TacticRule> For(OfficerState person)
    {
        if (known.TryGetValue(person.Id, out var tactics)) return tactics;
        return known[person.Id] = person.LearnedTactics.Concat(force.ProvidedTactics)
            .Concat(treasures[person.Id].Select(t => engine.GetTreasureDefinition(t.Id)?.TacticId).OfType<int>())
            .Distinct().Order().Where(rules.Tactics.ContainsKey).Select(id => rules.Tactics[id]).ToImmutableArray();
    }
}

/// <summary>Task-specific equipment search over mixed and common-category crews and rank placements.
/// Only own known capabilities are inputs; enemy resistance and future activations are not known.</summary>
internal sealed class CampaignEquipmentSearch(ImmutableArray<OfficerState> members,
    ImmutableDictionary<int, OfficerAbilities> abilities, ImmutableDictionary<int, ImmutableArray<TacticRule>> available,
    FormationRule formation, TacticLinkRules links, uint soldiers, uint defenders,
    ImmutableArray<TacticRule> automaticTactics = default, bool settlementTarget = true)
{
    internal CampaignEquipment Best { get; private set; } = new(
        members.ToImmutableDictionary(p => p.Id, _ => (int?)null),
        members.ToImmutableDictionary(p => p.Id, _ => BattleRank.Rear), new(0, 0, 0));

    internal IEnumerable<Action> Steps()
    {
        long bestValue = long.MinValue;
        var choices = members.ToDictionary(p => p.Id, p => available[p.Id].Where(t => t.Selectable &&
            t.CompatibleFormations.Contains(formation.Id) && (!settlementTarget || t.AffectsFacilities && t.ContactRange is null)).ToArray());
        // Independent equipment retains mixed capabilities; shared categories expose links that individual power sorting misses.
        foreach (int? category in new int?[] { null }.Concat(choices.Values.SelectMany(ts => ts)
            .Where(t => t.Effect == TacticEffect.Martial).Select(t => t.Category).Distinct().Order()))
        {
            var tactics = members.ToImmutableDictionary(p => p.Id, p => choices[p.Id]
                .OrderByDescending(t => category is not null && t.Category == category ? 1 : 0)
                .ThenByDescending(t => MartialValue(p, t)).ThenBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefault());
            if (formation.Tactics is null)
            {
                yield return () => Consider(tactics, members.ToImmutableDictionary(p => p.Id, _ => BattleRank.Rear));
                yield break;
            }
            foreach (var rank in Enum.GetValues<BattleRank>())
            {
                var ranks = members.ToImmutableDictionary(p => p.Id, _ => rank);
                yield return () => Consider(tactics, ranks);
            }
            foreach (var primary in members)
            {
                var ranks = members.ToImmutableDictionary(p => p.Id, p => p.Id == primary.Id ? BattleRank.Front : BattleRank.Rear);
                yield return () => Consider(tactics, ranks);
            }
        }

        void Consider(ImmutableDictionary<int, int?> tactics, ImmutableDictionary<int, BattleRank> ranks)
        {
            var estimate = Estimate(members, abilities, available, formation, links, tactics, ranks, automaticTactics, settlementTarget);
            // Ten combat days are a ranking horizon, not a guarantee of activation or victory.
            long value = estimate.Damage * 10 / CampaignTacticEstimate.Scale -
                estimate.OfficerExposure * defenders / Math.Max(1L, (long)soldiers + defenders);
            if (value <= bestValue) return;
            bestValue = value;
            Best = new(tactics, ranks, estimate);
        }

        long MartialValue(OfficerState person, TacticRule tactic) => tactic.Effect == TacticEffect.Martial
            ? TacticMath.MartialDamage([new(tactic.Power, person.TacticProficiencies[tactic.Category!.Value],
                abilities[person.Id].War, false)], false, 0, 10, false, uint.MaxValue).Calculated / Math.Max(1, tactic.MinimumInterval)
            : 0;
    }

    internal static CampaignTacticEstimate Estimate(ImmutableArray<OfficerState> members,
        ImmutableDictionary<int, OfficerAbilities> abilities, ImmutableDictionary<int, ImmutableArray<TacticRule>> available,
        FormationRule formation, TacticLinkRules links, ImmutableDictionary<int, int?> tactics,
        ImmutableDictionary<int, BattleRank> ranks, ImmutableArray<TacticRule> automaticTactics = default, bool settlementTarget = true)
    {
        long exposure = members.Sum(p => (long)BattleCaptureRules.Chance(abilities[p.Id].War, 1,
            ranks[p.Id], true, false, false) * (abilities[p.Id].Leadership + abilities[p.Id].Politics + abilities[p.Id].Intelligence));
        if (formation.Tactics is not { } profile) return new(0, 0, exposure);
        bool automatic = !profile.AutomaticTactics.IsEmpty;
        var selected = members.Select(p => (Person: p, Tactic: (automatic ? automaticTactics : available[p.Id])
            .Where(t => (automatic ? profile.AutomaticTactics.Contains(t.Id) : tactics[p.Id] == t.Id) &&
                t.CompatibleFormations.Contains(formation.Id) && (!settlementTarget || t.AffectsFacilities && t.ContactRange is null))
            .OrderByDescending(t => t.Power).ThenBy(t => t.Id).FirstOrDefault()))
            .Where(p => p.Tactic?.Effect == TacticEffect.Martial && p.Person.Injury == 0)
            .OrderBy(p => ranks[p.Person.Id]).ThenBy(p => p.Person.Id).ToArray();
        decimal remaining = 1, damage = 0, ordinary = 0;
        foreach (var (person, rule) in selected)
        {
            var tactic = rule!;
            decimal allocation = automatic ? 1m / members.Length :
                Math.Clamp(TacticMath.AllocationChance(ranks[person.Id], members.Length,
                    profile.FavoredCategories.Contains(tactic.Category!.Value)), 0, 100) / 100m;
            decimal chance = (automatic ? 1 : remaining) * allocation * Math.Clamp(profile.OpportunityChance, 0, 100) / 100m;
            // A renewal-rate approximation includes the primary cooldown, rather than assuming a proc each day.
            chance /= 1 + chance * Math.Max(0, tactic.MinimumInterval - 1);
            if (!automatic) remaining *= 1 - allocation;
            int proficiency = person.TacticProficiencies[tactic.Category!.Value];
            decimal amount = TacticMath.MartialDamage([new(tactic.Power, proficiency, abilities[person.Id].War, false)],
                false, 0, 10, false, uint.MaxValue).Calculated;
            foreach (var (partner, partnerRule) in selected.Where(p => p.Person.Id != person.Id && p.Tactic!.Category == tactic.Category))
            {
                decimal link = Math.Clamp(links.Chance(person, partner, profile), 0, 100) / 100m;
                amount += link * TacticMath.MartialDamage([new(partnerRule!.Power,
                    partner.TacticProficiencies[tactic.Category.Value], abilities[partner.Id].War, true)],
                    false, 0, 10, false, uint.MaxValue).Calculated;
            }
            amount *= 1 + Math.Clamp(1 + proficiency / 100, 0, 100) / 100m;
            damage += chance * amount;
            ordinary += chance;
        }
        return new((long)(damage * CampaignTacticEstimate.Scale), (long)(ordinary * CampaignTacticEstimate.Scale), exposure);
    }
}
