using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record CommandPreparationPreview(CommandRejection Rejection, GameClock? Arrival, int? GovernorId);
