using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public sealed record ForceRelation(int FirstId, int SecondId, int Value);

public sealed record CorpsTreasury(uint Money, uint Food);
public sealed record CorpsState(int Id, int OwnerId, int? LeaderId, [property: JsonRequired] CorpsTreasury Treasury)
{
    [JsonRequired] public bool IsRoot { get; init; }
    [JsonRequired] public CorpsDelegation Delegation { get; init; } = new GeneralDelegation();
}
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OfficerTreasurePlacement), "officer")]
[JsonDerivedType(typeof(RegionTreasurePlacement), "region")]
public abstract record TreasurePlacement;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficerTreasurePlacement([property: JsonRequired] int OfficerId) : TreasurePlacement;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegionTreasurePlacement([property: JsonRequired] int RegionId) : TreasurePlacement;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TreasureState(int Id, [property: JsonRequired] TreasurePlacement Placement)
{
    [JsonIgnore] public int? HolderId => (Placement as OfficerTreasurePlacement)?.OfficerId;
    [JsonIgnore] public int? RegionId => (Placement as RegionTreasurePlacement)?.RegionId;
    [JsonIgnore] public bool IsValid => Id >= 0 && (Placement switch
    {
        OfficerTreasurePlacement held => held.OfficerId >= 0,
        RegionTreasurePlacement buried => buried.RegionId >= 0,
        _ => false
    });
}
public sealed record ChronicleEntry(int Year, int Month, int Day, int ForceId, string Text);
