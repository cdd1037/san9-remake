namespace San9.Core.Domain;

public sealed record ForceRelation(int FirstId, int SecondId, int Value);

public sealed record CorpsTreasury(uint Money, uint Food);
public enum CorpsControlMode { Direct, Automatic }
public sealed record CorpsState(int Id, int OwnerId, int LeaderId, CorpsTreasury? Treasury)
{
    [System.Text.Json.Serialization.JsonRequired] public CorpsControlMode ControlMode { get; init; }
}
public enum TreasureDiscovery { Disabled, Buried, Discovered }

public sealed record TreasureState(int Id, int? HolderId, TreasureDiscovery Discovery, int? DiscoveryRegionId)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool Discovered => Discovery == TreasureDiscovery.Discovered;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => Id >= 0 && HolderId is not < 0 &&
        DiscoveryRegionId is not < 0 && Enum.IsDefined(Discovery);
}
public sealed record ChronicleEntry(int Year, int Month, int Day, int ForceId, string Text);
