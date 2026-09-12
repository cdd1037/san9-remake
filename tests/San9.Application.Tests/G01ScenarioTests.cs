using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class G01ScenarioTests
{
    internal static ScenarioInitialState Fixture() => new(16, "g01-public", 190, 1, 1, 1,
        [new(1, "force.1", 1, 1000, 2000, 50) { LeaderId = 1 }, new(2, "force.2", 2, 1000, 2000, 50) { LeaderId = 4 }],
        [new(1, "city.1", 1, 1, 1, 100, 100, 100, 50, []) { Commerce = 300, MaxCommerce = 500, Cultivation = 200, MaxCultivation = 500 },
         new(2, "city.2", 2, 2, 2, 100, 100, 100, 50, []) { Commerce = 300, MaxCommerce = 500, Cultivation = 200, MaxCultivation = 500 }], [], [])
    {
        Officers = [new("officer.1", new(1, 1, 1, new(60, 70, 80, 90), false)),
            new("officer.2", new(2, 1, 1, new(50, 60, 100, 70), false)), new("officer.3", new(3, 1, 1, new(40, 80, 70, 60), false)),
            new("officer.4", new(4, 2, 2, new(60, 70, 80, 90), false)), new("officer.5", new(5, 2, 2, new(50, 60, 100, 70), false)),
            new("officer.6", new(6, 2, 2, new(40, 80, 70, 60), false))]
    };

    [Fact]
    public void Headless36XunScenarioPreservesStateEventsAndQueriesAcrossRestore()
    {
        string? contentPath = Environment.GetEnvironmentVariable("SAN9_G01_SCENARIO");
        string? reportDirectory = Environment.GetEnvironmentVariable("SAN9_G01_REPORT_DIR");
        var timer = Stopwatch.StartNew();
        string? fingerprint = null;
        string? finalHash = null;
        string? failure = null;
        int completedXun = 0, accepted = 0, rejected = 0, restoreCount = 0;
        string label = string.IsNullOrWhiteSpace(contentPath) ? "public" : "private-190";
        string? logPath = reportDirectory is null ? null : Path.Combine(reportDirectory, $"{label}-commands.jsonl");
        List<string> log = [];
        WorldSnapshot? last = null;
        try
        {
            var definition = string.IsNullOrWhiteSpace(contentPath) ? Fixture() :
                JsonSerializer.Deserialize<ScenarioInitialState>(File.ReadAllText(contentPath, Encoding.UTF8), ScenarioContentFiles.DataJsonOptions)!;
            definition.Validate();
            fingerprint = GameSession.Fingerprint(definition);
            var session = GameSession.Start(definition, 190);
            Assert.Single(session.InitialEvents.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.XunUpdate);
            var continuous = new SimulationEngine(WorldState.Restore(session.CaptureStableState()));
            var resumed = new SimulationEngine(WorldState.Restore(session.CaptureStableState()));
            Assert.Equal(definition.Forces.Select(f => f.Id).Order(), continuous.Snapshot.Forces.Select(f => f.Id));
            Assert.Equal(definition.Settlements.Select(s => s.Id).Order(), continuous.Snapshot.Settlements.Select(s => s.Id));
            Assert.Equal(definition.Officers.Select(o => o.State.Id).Order(), continuous.Snapshot.Officers.Select(o => o.Id));
            Assert.Equal(definition.Units.Select(u => u.Id).Order(), continuous.Snapshot.Units.Select(u => u.Id));
            Assert.Equal(definition.Information.Corps.Select(c => c.Id).Order(), continuous.Snapshot.Corps.Select(c => c.Id));
            Assert.Equal(definition.Information.TreasureStates.Select(t => t.Id).Order(), continuous.Snapshot.Treasures.Select(t => t.Id));
            Assert.Equal(definition.Information.Relations.Length, continuous.Snapshot.Relations.Count);
            for (int xun = 0; xun < 36; xun++)
            {
                foreach (var seat in continuous.Snapshot.Simulation.Controls)
                {
                    var snapshot = continuous.Snapshot;
                    var officer = snapshot.Officers.FirstOrDefault(o => o.OwnerId == seat.ForceId && o.IsServing && !o.Acted);
                    long sequence = snapshot.Simulation.NextSequence;
                    var commandId = new Guid(checked((int)sequence + 1), 0, 0, new byte[8]);
                    GameCommand command = officer is null ? new WaitCommand(commandId, seat.ForceId, sequence) :
                        new DevelopCommand(commandId, seat.ForceId, sequence,
                            new(seat.ForceId, officer.SettlementId!.Value, [officer.Id], xun % 2 == 0 ? DomesticActivity.Commerce : DomesticActivity.Cultivation));
                    var a = continuous.Execute(new(seat.ControllerId), command);
                    var b = resumed.Execute(new(seat.ControllerId), command);
                    Equal(a.Snapshot, b.Snapshot, a.Events, b.Events);
                    Assert.Equal(a.Submission, b.Submission);
                    if (a.Submission.Accepted) accepted++;
                    else { rejected++; Assert.Same(snapshot, a.Snapshot); Assert.Empty(a.Events); }
                    log.Add(JsonSerializer.Serialize(new { xun, controller = seat.ControllerId, command = (object)command,
                        accepted = a.Submission.Accepted, rejection = a.Submission.Rejection.ToString(), stateHash = a.Snapshot.StateHash }));
                    if (seat.Kind == ControllerKind.Player && xun is 0 or 17)
                    {
                        resumed = Restore(resumed); restoreCount++;
                    }
                    var endA = continuous.EndControl(new(seat.ControllerId));
                    var endB = resumed.EndControl(new(seat.ControllerId));
                    Equal(endA.Snapshot, endB.Snapshot, endA.Events, endB.Events);
                }
                var advanced = continuous.AdvanceXun();
                var restoredAdvance = resumed.AdvanceXun();
                Equal(advanced.Snapshot, restoredAdvance.Snapshot, advanced.Events, restoredAdvance.Events);
                Assert.Equal(10, advanced.Events.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.BeforeDailyCommands));
                if (xun % 5 == 0) { resumed = Restore(resumed); restoreCount++; }
                var projected = new StrategySnapshot(definition, resumed.Snapshot);
                Assert.Equal(resumed.Snapshot.Clock.Day, projected.Day);
                Assert.Equal(resumed.Snapshot.Clock.Year, projected.Year);
                Assert.Equal(resumed.Snapshot.Forces[0].Money, projected.Forces[0].Money);
                last = resumed.Snapshot;
                finalHash = last.StateHash;
                completedXun++;
            }
            Assert.True(accepted > 0);
            Assert.Equal(new GameClock(definition.Year + 1, definition.Month, definition.Day), last!.Clock);
            Assert.Contains(last.Settlements.Zip(definition.Settlements.OrderBy(s => s.Id)), pair =>
                pair.First.Commerce > pair.Second.Commerce || pair.First.Cultivation > pair.Second.Cultivation);
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            if (reportDirectory is not null)
            {
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllLines(logPath!, log, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(reportDirectory, $"{label}-report.json"), JsonSerializer.Serialize(new
                {
                    goal = "G01", scenario = label, contentFingerprint = fingerprint,
                    fingerprintScope = "normalized gameplay content; shared Application fingerprint",
                    seed = 190, commandLog = logPath, completedXun, terminationReason = failure is null ? "requested-36-xun" : "failure",
                    finalHash, firstFailure = failure, elapsedMilliseconds = timer.ElapsedMilliseconds,
                    acceptedCommands = accepted, rejectedCommands = rejected, restoreCount,
                    forces = last?.Forces.Count, settlements = last?.Settlements.Count, officers = last?.Officers.Count,
                    units = last?.Units.Count, corps = last?.Corps.Count, treasures = last?.Treasures.Count,
                    relations = last?.Relations.Count,
                    limitations = "Scripted commands and G01 timing only; no economic cycle, AI planner, warfare, disk save service or game process."
                }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            }
        }
    }

    private static SimulationEngine Restore(SimulationEngine engine)
    {
        var json = JsonSerializer.Serialize(engine.CaptureStableState());
        var restored = new SimulationEngine(WorldState.Restore(JsonSerializer.Deserialize<WorldSnapshot>(json)!));
        Assert.Empty(restored.InitialEvents);
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        return restored;
    }

    private static void Equal(WorldSnapshot a, WorldSnapshot b, IReadOnlyList<DomainEvent> aEvents, IReadOnlyList<DomainEvent> bEvents)
    {
        Assert.Equal(a.StateHash, b.StateHash);
        Assert.Equal(a.Revision, b.Revision);
        Assert.Equal(aEvents.Select(e => JsonSerializer.Serialize(e, e.GetType())), bEvents.Select(e => JsonSerializer.Serialize(e, e.GetType())));
    }

    [Fact]
    public void SessionRejectsForgedForceWithoutChangingRecordsOrProjection()
    {
        var session = GameSession.Start(Fixture(), 190);
        var before = session.View;
        Assert.Equal(DevelopmentRejection.NotAuthorized, session.Develop(new(2, 2, [5], DomesticActivity.Commerce)).Rejection);
        Assert.Same(before, session.View);
        Assert.Empty(session.Records);
        Assert.True(session.Develop(new(1, 1, [2], DomesticActivity.Commerce)).Accepted);
        Assert.Single(session.Records);
        Assert.Equal(1, session.Snapshot.Simulation.NextSequence);
    }
    [Fact]
    public void AvailabilityProjectionUsesPersistentOccupationAfterActionRecovery()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var original = session.Snapshot;
        var tasks = original.Officers.Where(o => o.OwnerId == 1).Select((o, i) =>
            new PersistentTask(new Guid(i + 1, 0, 0, new byte[8]), "query-fixture", "player", 1,
                o.Id, 1, original.Clock, original.Clock.Advance(), 0, i)).ToImmutableArray();
        // A projection fixture deliberately supplies occupation separately from recovered Acted flags.
        var view = new StrategySnapshot(definition, original with { Simulation = original.Simulation with { Tasks = tasks } });
        Assert.All(view.Officers.Where(o => o.OwnerId == 1), o => Assert.False(o.IsAvailable));
        Assert.False(view.Settlements.Single(s => s.Id == 1).HasAvailableOfficer);
        Assert.All(view.Officers.Where(o => o.OwnerId == 2), o => Assert.True(o.IsAvailable));
    }

}
