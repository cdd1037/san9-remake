namespace San9.Core.Domain;

// Active field forces are separate from settlement garrisons. Movement rules are separate use cases.
public sealed record UnitState(int Id, int OwnerId, [property: System.Text.Json.Serialization.JsonRequired] WorldPosition Position, uint Soldiers)
{
    [System.Text.Json.Serialization.JsonIgnore] public MapCell Cell => MapCoordinates.Cell(Position);
    [System.Text.Json.Serialization.JsonRequired] public int? CorpsId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Id >= 0 && OwnerId >= 0 && Position.X >= 0 && Position.Y >= 0 && Soldiers > 0;
}
