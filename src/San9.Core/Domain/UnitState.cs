namespace San9.Core.Domain;

// Active field forces are separate from settlement garrisons. Movement rules are separate use cases.
public sealed record UnitState(int Id, int OwnerId, int X, int Y, uint Soldiers)
{
    public bool IsValid => Id >= 0 && OwnerId >= 0 && X is >= 0 and < 200 && Y is >= 0 and < 200 && Soldiers > 0;
}
