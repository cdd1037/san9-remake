using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Navigation invalidation shared by authoritative mutations and isolated forecasts.</summary>
internal static class MilitaryUnitTransitions
{
    internal static UnitState Apply(UnitState previous, UnitState value, bool assignsNavigation = false)
    {
        if (previous.Condition != value.Condition && value.HasForcedReturn)
            value = value with { EngagementTargetId = null, ContactTarget = null, ActiveEngagement = false, Lookahead = null };
        if (assignsNavigation || previous.CommandOrder != value.CommandOrder || previous.OwnerId != value.OwnerId)
            value = value with { ContactTarget = null, Lookahead = null };
        if (value.EffectiveOrder is null || previous.EffectiveOrder != value.EffectiveOrder || previous.OwnerId != value.OwnerId ||
            previous.FormationId != value.FormationId || previous.NavalFormationId != value.NavalFormationId ||
            previous.Waypoint != value.Waypoint || previous.Policies != value.Policies || previous.Condition != value.Condition || previous.ContactTarget != value.ContactTarget)
            value = value with { Lookahead = null };
        if ((!assignsNavigation && previous.Order != value.Order) || previous.OwnerId != value.OwnerId)
            value = value with { Waypoint = null, DepartureDaysRemaining = 0 };
        if (value.EffectiveOrder is null || value.Condition == UnitCondition.Confused ||
            previous.FormationId != value.FormationId || previous.NavalFormationId != value.NavalFormationId || previous.OwnerId != value.OwnerId)
            value = value with { MovementRemainder = 0 };
        return value;
    }
}
