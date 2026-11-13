using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record CommandPreparationPreview(CommandRejection Rejection, GameClock? Arrival, int? GovernorId)
{
    /// <summary>Initial journey countdown, distinct from the calendar offset of resolution.</summary>
    public long? JourneyDays { get; init; }
    public System.Collections.Immutable.ImmutableArray<ArmamentResupplyLine> ArmamentResupply { get; init; } = [];
}
