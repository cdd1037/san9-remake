using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class UnitContentCompiler
{
    public static UnitState[] Compile(IEnumerable<San9ScenarioUnitRecord> records, Func<int?, int?> rootForce)
    {
        return records.Where(u => u.UnitStatus != San9ScenarioUnitStatus.Invalid).Select(u =>
        {
            if (u.UnitStatus is null || u.Morale > 100 || rootForce(u.ForceId) is not int owner ||
                u.XCoordinate is not int x || u.YCoordinate is not int y)
                throw new InvalidDataException("Invalid active field unit.");
            return new UnitState(u.Id, owner, MapCoordinates.Center(new(x, y)), (uint)u.LoadedSoldierCount)
            {
                CorpsId = u.ForceId, ServicePlaceId = u.HomeBaseId, FormationId = u.FormationId,
                NavalFormationId = !u.HasRuntimeFlagBit(0) && !u.HasRuntimeFlagBit(1) && u.FormationId is int formation &&
                    MilitaryContentCompiler.Mode(formation) == TravelMode.Land ? formation switch { 13 => 24, 14 => 25, _ => MilitaryContentCompiler.BasicNavalFormationId } : null,
                Policies = new(u.AttacksWhenEnemyApproaches, u.AllowsPursuit, u.AttacksAfterAction, u.AllowsAutonomousRetreat),
                ConditionDaysRemaining = u.StatusDurationDays,
                HighSpiritsMoralePenaltyPending = u.UnitStatus == San9ScenarioUnitStatus.HighSpirits && u.StatusDurationDays > 0,
                Wounded = (uint)u.LoadedWoundedSoldierCount, Morale = u.Morale,
                IsEmbedded = u.HasRuntimeFlagBit(0), IsPersonnel = u.HasRuntimeFlagBit(1),
                OrderClass = u.LoadedCommandCode switch
                {
                    -1 => UnitOrderClass.None,
                    >= 38 and <= 52 => UnitOrderClass.Military,
                    _ => UnitOrderClass.NonMilitary
                },
                Condition = u.UnitStatus switch
                {
                    San9ScenarioUnitStatus.Normal => UnitCondition.Normal,
                    San9ScenarioUnitStatus.Engaged => UnitCondition.Engaged,
                    San9ScenarioUnitStatus.Trap => UnitCondition.Trap,
                    San9ScenarioUnitStatus.Disguised => UnitCondition.Disguised,
                    San9ScenarioUnitStatus.NoFormation => UnitCondition.NoFormation,
                    San9ScenarioUnitStatus.Confused => UnitCondition.Confused,
                    San9ScenarioUnitStatus.Routed => UnitCondition.Routed,
                    San9ScenarioUnitStatus.HighSpirits => UnitCondition.HighSpirits,
                    _ => throw new InvalidDataException("Invalid active unit condition.")
                }
            };
        }).ToArray();
    }
}
