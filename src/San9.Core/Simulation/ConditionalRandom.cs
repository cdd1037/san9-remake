namespace San9.Core.Simulation;

internal enum ConditionalRandomPurpose { Recruitment, SearchDiscovery, FalseReport, Disorder, Inspire, Reinforcement, Burn }

/// <summary>Fixed-condition draws do not consume or depend on the current run's streams.</summary>
internal static class ConditionalRandom
{
    internal static int Percentile(ulong initialSeed, ConditionalRandomPurpose purpose, GameClock date, int actorId, int targetId)
    {
        string name = purpose switch
        {
            ConditionalRandomPurpose.Recruitment => "personnel-recruit-submission",
            ConditionalRandomPurpose.SearchDiscovery => "personnel-search-submission",
            ConditionalRandomPurpose.FalseReport => "strategy-false-report-unit-submission",
            ConditionalRandomPurpose.Disorder => "strategy-disorder-unit-submission",
            ConditionalRandomPurpose.Burn => "strategy-burn-submission",
            ConditionalRandomPurpose.Inspire => "strategy-inspire-unit-submission",
            ConditionalRandomPurpose.Reinforcement => "strategy-reinforcement-unit-submission",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };
        ulong seed = StableRandom.DeriveSeed(initialSeed,
            FormattableString.Invariant($"{name}:{date.DayNumber}:{actorId}:{targetId}"));
        return (int)(new StableRandom(seed).NextUInt64() % 100);
    }
}
