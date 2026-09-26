namespace San9.Core.Domain;

/// <summary>Personal command limit; distinct from a formation's soldier storage capacity.</summary>
public static class CommanderCapacity
{
    public static uint Read(bool ruler, uint? rankCapacity, uint? officeCapacity) =>
        ruler ? rankCapacity ?? 20000u : officeCapacity ?? 10000u;
}
