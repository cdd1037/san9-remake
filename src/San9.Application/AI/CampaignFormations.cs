using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Application.AI;

/// <summary>Task-specific seeds preserve speed, economy, siege and tactic advantages before detailed equipment search.</summary>
internal static class CampaignFormations
{
    internal static IEnumerable<FormationRule> Select(FormationRule[] formations, ImmutableArray<int> members,
        Func<int, ImmutableArray<TacticRule>> tactics)
    {
        var legal = formations.Where(f => f.OfficerCapacity >= Math.Max(1, members.Length) &&
            (f.RequiredTacticId is not int required || members.Any(p => tactics(p).Any(t => t.Id == required)))).ToArray();
        if (legal.Length == 0) yield break;
        var seeds = new[]
        {
            legal.OrderBy(f => f.DeploymentCost).ThenByDescending(f => f.Combat!.AgainstGarrison).ThenBy(f => f.Id).First(),
            legal.OrderByDescending(f => f.MovementBudget).ThenBy(f => f.DeploymentCost).ThenBy(f => f.Id).First(),
            legal.OrderByDescending(f => f.Combat!.AgainstGarrison * (long)f.SoldierCapacity).ThenBy(f => f.DeploymentCost).ThenBy(f => f.Id).First(),
            legal.OrderByDescending(f => f.Attack!.DamagesWalls ? f.Combat!.AgainstWalls * (long)f.SoldierCapacity : 0).ThenBy(f => f.DeploymentCost).ThenBy(f => f.Id).First(),
            legal.OrderByDescending(f => members.SelectMany(p => tactics(p)).Where(t => t.AffectsFacilities && t.ContactRange is null && t.CompatibleFormations.Contains(f.Id))
                .Select(t => t.Power).DefaultIfEmpty(0).Max()).ThenBy(f => f.DeploymentCost).ThenBy(f => f.Id).First()
        };
        foreach (var seed in seeds.DistinctBy(f => f.Id)) yield return seed;
    }
}
