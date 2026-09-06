using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class TreasureContentCompiler
{
    public const int BuiltInCount = 100;

    public static TreasureState[] InitialStates(IEnumerable<San9ScenarioItemRecord> records) => records.Select(item =>
    {
        if (item.SerializedId >= BuiltInCount && !item.IsInvalid)
            throw new InvalidDataException("Configured expansion treasure requires expansion text support.");
        var discovery = item.DiscoveryState switch
        {
            San9ScenarioItemDiscoveryState.Invalid => TreasureDiscovery.Disabled,
            San9ScenarioItemDiscoveryState.NotUnearthed => TreasureDiscovery.Buried,
            San9ScenarioItemDiscoveryState.Unearthed => TreasureDiscovery.Discovered,
            _ => throw new InvalidDataException("Invalid treasure discovery state.")
        };
        return new TreasureState(item.SerializedId, item.HolderPersonId, discovery, item.DiscoveryLocationId);
    }).ToArray();
}
