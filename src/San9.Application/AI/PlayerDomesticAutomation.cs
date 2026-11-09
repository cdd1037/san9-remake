using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>Uses remaining direct-corps personnel once when a player ends control.</summary>
internal static class PlayerDomesticAutomation
{
    private static readonly OriginalAiWork[] WorkOrder =
    [
        OriginalAiWork.Train, OriginalAiWork.Repair, OriginalAiWork.Commerce, OriginalAiWork.Cultivation,
        OriginalAiWork.Patrol,
    ];

    internal static void Run(SimulationEngine engine, ScenarioSessionDefinition definition, ControlSeat seat,
        OriginalAiState ai, List<DomainEvent> events, List<OriginalAiWorkReport> reports)
    {
        var companion = engine.Companion;
        var world = engine.Snapshot;
        if (seat.Kind != ControllerKind.Player || companion.PlayerCorpsAutomation != PlayerCorpsAutomationMode.DomesticAndSearch || (!companion.PlayerAutoDomesticEnabled && !companion.PlayerAutoSearchEnabled) ||
            !world.Forces.Any(f => f.Id == seat.ForceId && !f.IsEliminated && !f.IsDormant && !f.IsBarbarian)) return;
        OriginalAiGeography? geography = null;
        var context = new CommandContext(seat.ControllerId);
        foreach (var corps in world.Corps.Where(c => c.OwnerId == seat.ForceId && c.IsRoot)
            .OrderByDescending(c => c.IsRoot).ThenBy(c => c.Id))
        {
            bool Submit(GameCommand command)
            {
                var result = engine.Execute(context, command);
                if (!result.Submission.Accepted) return false;
                events.AddRange(result.Events);
                AutomaticResponseRunner.Run(engine, events);
                return true;
            }
            if (companion.PlayerAutoDomesticEnabled)
            {
                geography ??= new OriginalAiGeography(definition.Content, definition.MilitaryRules, world);
                RunDomestic(engine, definition, corps, seat, ai, geography, Submit, reports);
            }
            if (companion.PlayerAutoSearchEnabled)
                reports.Add(SearchLocal(engine, corps, seat, Submit));
        }
    }

    private static void RunDomestic(SimulationEngine engine, ScenarioSessionDefinition definition, CorpsState corps,
        ControlSeat seat, OriginalAiState ai, OriginalAiGeography geography,
        Func<GameCommand, bool> submit, List<OriginalAiWorkReport> reports)
    {
        var world = engine.Snapshot;
        var force = ai.Forces.Single(f => f.ForceId == seat.ForceId);
        var policy = ai.Policy;
        bool advanced = world.Simulation.NewGame.Difficulty == GameDifficulty.Advanced;
        var random = new OriginalAiRandom(new StableRandom(StableRandom.DeriveSeed(world.Simulation.RunSeed,
            FormattableString.Invariant($"companion-player-domestic:{corps.Id}:{world.Clock.DayNumber}"))));
        var projection = new OriginalAiProjection(engine, definition.Content, corps.Id, geography, false);
        bool Develop(DevelopmentCommand development)
        {
            long sequence = engine.Snapshot.Simulation.NextSequence;
            return submit(new DevelopCommand(SimulationIdentity.Command(sequence, seat.ForceId), seat.ForceId, sequence, development));
        }
        foreach (var work in WorkOrder)
        {
            if (!policy.WorkRules.TryGetValue(work, out var rule) || !corps.IsRoot && !rule.AllowsChildCorps)
            {
                reports.Add(new(corps.Id, work, OriginalAiWorkEnd.Permission, 0, 0));
                continue;
            }
            if (!OriginalAiWorkGate.Calendar(world.Clock, world.Simulation.Started, force.SchedulingKey, rule, advanced))
            {
                reports.Add(new(corps.Id, work, OriginalAiWorkEnd.Calendar, 0, 0));
                continue;
            }
            long revision = engine.Snapshot.Revision;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            int limit = OriginalAiWorkGate.Attempts(rule, advanced, force.Group);
            var activity = work switch
            {
                OriginalAiWork.Train => DomesticActivity.Train,
                OriginalAiWork.Repair => DomesticActivity.Repair,
                OriginalAiWork.Commerce => DomesticActivity.Commerce,
                OriginalAiWork.Cultivation => DomesticActivity.Cultivation,
                OriginalAiWork.Patrol => DomesticActivity.Patrol,
                _ => throw new InvalidOperationException("Unsupported player automatic domestic work."),
            };
            var report = OriginalDomesticWork.Run(activity, limit, projection.Domestic, () => random.Next(100), Develop)
                .ToReport(corps.Id, work);
            reports.Add(report with
            {
                InputRevision = revision,
                ElapsedTimestampTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started,
                AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
            });
        }
    }

    private static OriginalAiWorkReport SearchLocal(SimulationEngine engine, CorpsState corps, ControlSeat seat,
        Func<GameCommand, bool> submit)
    {
        var world = engine.Snapshot;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var places = world.Settlements.Where(s => s.OwnerId == seat.ForceId &&
            (s.CorpsId == corps.Id || s.CorpsId is null && corps.IsRoot)).Select(s => s.Id).ToHashSet();
        var abilities = engine.GetAbilities(world);
        int minimumPolitics = engine.Companion.PlayerAutoSearchMinimumPolitics;
        var actors = world.Officers.Where(p => !p.IsDormant && p.IsServing && !p.Acted &&
            p.OwnerId == seat.ForceId && p.UnitId is null && p.SettlementId is int home && places.Contains(home) &&
            abilities[p.Id].Politics >= minimumPolitics)
            .OrderBy(p => p.Id).Select(p => p.Id).ToArray();
        int attempted = 0, submitted = 0, rejected = 0;
        foreach (int id in actors)
        {
            if (engine.Snapshot.Simulation.Boundary != SimulationBoundary.Stable) break;
            var actor = engine.Snapshot.Officers.Single(p => p.Id == id);
            if (actor.SettlementId is not int home ||
                !engine.GetPersonnelJourney(seat.ForceId, id, home, PersonnelJourneyKind.Search, includeAdvice: false).Available) continue;
            long sequence = engine.Snapshot.Simulation.NextSequence;
            attempted++;
            if (submit(new SearchCommand(SimulationIdentity.Command(sequence, seat.ForceId), seat.ForceId, sequence, [id], home))) submitted++;
            else rejected++;
        }
        return new(corps.Id, OriginalAiWork.Search, OriginalAiWorkEnd.NoCandidates, attempted, submitted)
        {
            Rejected = rejected, InputRevision = world.Revision,
            ElapsedTimestampTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started,
            AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
        };
    }
}
