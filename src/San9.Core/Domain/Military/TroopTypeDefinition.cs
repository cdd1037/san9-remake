using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record MixedTroopCombatProfile(int SpearDefensePercent, int ShieldCoverPercent,
    int MountedAttackPercent, int SpearBowPercent);

public sealed record TroopTypeDefinition(int Id, string Name, int MaximumLevel,
    ImmutableDictionary<int, int> TacticMinimumLevels)
{
    public ImmutableDictionary<int, string> LevelResearchProjects { get; init; } = ImmutableDictionary<int, string>.Empty;
    public ImmutableDictionary<int, int> EquipmentCostPerThousand { get; init; } = ImmutableDictionary<int, int>.Empty;
    public bool SupportsSpearDefense { get; init; }
    public bool SupportsShieldCover { get; init; }
    public bool IsMountedMelee { get; init; }
    public bool IsRangedInfantry { get; init; }
    public bool IsRangedAttack { get; init; }
}

public sealed partial class MilitaryRulebook
{
    /// <summary>Checks the equipment requirement for a member's land martial tactic.</summary>
    public bool SupportsDetachmentTactic(DetachmentState detachment, TacticRule tactic) =>
        detachment.Soldiers > 0 && TroopTypes.TryGetValue(detachment.TroopTypeId, out var troop) &&
        troop.TacticMinimumLevels.TryGetValue(tactic.Id, out int minimumLevel) && detachment.Level >= minimumLevel;
}
