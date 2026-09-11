using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Validates business preconditions and stages development changes.</summary>
internal static class DomesticDevelopmentHandler
{
    internal static DevelopmentOutcome Apply(WorldMutation mutation, DevelopmentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Activity is not (DomesticActivity.Commerce or DomesticActivity.Cultivation))
            return new(DevelopmentRejection.UnsupportedActivity);

        if (!mutation.Forces.TryGetValue(command.ForceId, out var force) ||
            !mutation.Settlements.TryGetValue(command.SettlementId, out var settlement) ||
            !mutation.Officers.TryGetValue(command.OfficerId, out var officer))
            return new(DevelopmentRejection.UnknownObject);
        if (settlement.OwnerId != force.Id || officer.OwnerId != force.Id)
            return new(DevelopmentRejection.WrongOwner);
        if (!officer.IsServing || officer.Acted || officer.SettlementId != settlement.Id)
            return new(DevelopmentRejection.OfficerUnavailable);

        bool commerce = command.Activity == DomesticActivity.Commerce;
        if (commerce ? settlement.CommerceUsed : settlement.CultivationUsed)
            return new(DevelopmentRejection.AlreadyUsed);
        int current = commerce ? settlement.Commerce : settlement.Cultivation;
        int maximum = commerce ? settlement.MaxCommerce : settlement.MaxCultivation;
        if (current >= maximum) return new(DevelopmentRejection.AtMaximum);
        if (force.Money < 50) return new(DevelopmentRejection.InsufficientFunds);

        var result = DomesticDevelopment.CalculateResult(command.Activity, current, maximum,
            (int)Math.Min(force.Money, int.MaxValue), [officer.Abilities], false);
        mutation.Replace(force with { Money = force.Money - (uint)result.Cost });
        mutation.Replace(commerce
            ? settlement with { Commerce = settlement.Commerce + result.Increment, CommerceUsed = true }
            : settlement with { Cultivation = settlement.Cultivation + result.Increment, CultivationUsed = true });
        mutation.Replace(officer with { Acted = true });
        return new(DevelopmentRejection.None, result.Increment, result.Cost);
    }
}
