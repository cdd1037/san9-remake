namespace San9.Core.Domain;

public enum UnitOrderClass { None, NonMilitary, Military }
public enum UnitCondition { Normal, Engaged, Trap, Disguised, NoFormation, Confused, Routed, HighSpirits }

// Active field forces are separate from settlement garrisons. Movement rules are separate use cases.
public sealed record UnitState(int Id, int OwnerId, [property: System.Text.Json.Serialization.JsonRequired] WorldPosition Position, uint Soldiers)
{
    [System.Text.Json.Serialization.JsonIgnore] public MapCell Cell => MapCoordinates.Cell(Position);
    [System.Text.Json.Serialization.JsonRequired] public int? CorpsId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? ServicePlaceId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool IsEmbedded { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool IsPersonnel { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public UnitOrderClass OrderClass { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public UnitCondition Condition { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public UnitOrderClass EffectiveOrderClass => Condition is UnitCondition.Disguised or UnitCondition.Routed
        ? UnitOrderClass.Military : OrderClass;

    public bool FollowsServicePlace(bool creatingCorps) =>
        ServicePlaceId is not null && (IsPersonnel || EffectiveOrderClass == UnitOrderClass.Military) &&
        (!creatingCorps || !IsEmbedded);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Id >= 0 && OwnerId >= 0 && ServicePlaceId is not < 0 && Position.X >= 0 && Position.Y >= 0 && Soldiers > 0 &&
        Enum.IsDefined(OrderClass) && Enum.IsDefined(Condition);
}
