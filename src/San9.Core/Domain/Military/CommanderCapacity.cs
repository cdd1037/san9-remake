namespace San9.Core.Domain;

/// <summary>Personal command limit; distinct from a formation's soldier storage capacity.</summary>
public static class CommanderCapacity
{
    public static uint Read(ForceState force, OfficerState? commander, GovernanceRulebook governance) =>
        commander is null ? 0 : Read(force.LeaderId == commander.Id,
            force.RankId is int rank && governance.RankCommandCapacities.TryGetValue(rank, out uint capacity) ? capacity : null,
            commander.OfficeId is int office ? governance.Offices[office].CommandCapacity : null);

    public static uint Read(bool ruler, uint? rankCapacity, uint? officeCapacity) =>
        ruler ? rankCapacity ?? 20000u : officeCapacity ?? 10000u;
}
