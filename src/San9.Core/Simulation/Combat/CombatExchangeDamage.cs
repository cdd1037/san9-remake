namespace San9.Core.Simulation;

/// <summary>Uncommitted ordinary losses, oriented from the current actor to its opponent.</summary>
public readonly record struct CombatExchangeDamage(uint Outgoing, uint Incoming, bool OutgoingContinuesSiege = false, bool IncomingContinuesSiege = false)
{
    public uint? OutgoingRanged { get; init; }
    public uint? IncomingRanged { get; init; }
    public CombatExchangeDamage Reverse() => new(Incoming, Outgoing, IncomingContinuesSiege, OutgoingContinuesSiege)
        { OutgoingRanged = IncomingRanged, IncomingRanged = OutgoingRanged };
}
