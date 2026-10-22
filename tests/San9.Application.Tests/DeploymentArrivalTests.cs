using San9.Tests;
using San9.Application.Persistence;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DeploymentArrivalTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "deployment-arrival", 190, 1, 1, 1,
        [new(1, "force", 1, 100)],
        [new(1, "origin", 1, 0, 1, 1000, 100, 100, 70, []), new(2, "destination", 1, 19, 1, 100, 100, 100, 30, [])], [], [])
    {
        Officers = [new("officer", new(1, 1, 1, new(70, 70, 70, 70), false))],
        Military = new(20, 3, Enumerable.Range(0, 60).Select(i => i % 20 is >= 4 and <= 6 ? 1 : 0).ToImmutableArray(),
            [new(0, 10, null), new(1, null, 12) { IsWater = true }],
            [new(100, 95, 0, 10000, 5, new(1, 1), TravelMode.Land),
             new(101, 50, 0, 10000, 5, new(1, 1), TravelMode.Land),
             new(200, 70, 0, 10000, 5, new(1, 1), TravelMode.Water),
             new(201, 140, 0, 10000, 5, new(1, 1), TravelMode.Water)],
            [new(1, new(2, 7), new(1, 1)), new(2, new(97, 7), new(1, 1))])
            { BasicNavalFormationId = 200, DefaultDeploymentFormationId = 100 },
        Information = new([], [], [], [new CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }], [])
    };

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(9, true)]
    public void DraftForecastMatchesActualFirstArrivalIncludingShorelinesRelayAndDelay(int delay, bool relay)
    {
        var session = GameSession.Start(Fixture(), 42);
        var draft = new DeploymentDraft(session.View, 1); Assert.True(draft.SetMembers([1]));
        var targets = new DeploymentTargets(draft); Assert.True(targets.Select(new(2, false)));
        if (relay) Assert.True(targets.SetWaypoint(new(1, 2)));
        Assert.True(targets.BeginPolicies()); Assert.True(targets.SetDepartureDelay(delay));
        var hash = session.Capture();
        var before = session.Snapshot;
        var estimate = targets.EstimateArrival();
        Assert.Equal(ArrivalEstimateStatus.Known, estimate.Status);
        StateAssert.Equal(hash, session.Capture());
        Assert.Empty(session.Snapshot.Units); Assert.Equal(before.Simulation.NextUnitId, session.Snapshot.Simulation.NextUnitId);
        using (var searches = new PathSearchDiagnostics())
        { Assert.Same(estimate, targets.EstimateArrival()); Assert.Equal(0, searches.Queries); }
        var command = targets.CreateCommand(Guid.NewGuid(), before.Simulation.NextSequence);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var unit = Assert.Single(session.Snapshot.Units);
        Assert.Equal(estimate, session.View.UnitArrival(unit.Id));
        UnitAbsorbedEvent? arrival = null;
        for (int i = 0; i < 10 && arrival is null; i++)
            arrival = session.AdvanceXun().Events.OfType<UnitAbsorbedEvent>().SingleOrDefault(e => e.UnitId == unit.Id);
        Assert.NotNull(arrival);
        Assert.Equal((long)estimate.Days!.Value, arrival.Clock.DayNumber - before.Clock.DayNumber);
    }

    [Fact]
    public void CrossCorpsRulerMarchTransfersTheDestinationAndReplaysThroughOldCorpsDissolution()
    {
        var definition = Fixture();
        definition = definition with
        {
            Forces = definition.Forces.Select(f => f with { LeaderId = 1 }).ToArray(),
            Settlements = definition.Settlements.Select(s => s with { CorpsId = s.Id }).ToArray(),
            Officers = [new("ruler", new(1, 1, 1, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }),
                new("governor", new(2, 1, 2, new(60, 60, 60, 60), false) { Status = OfficerStatus.Governor })],
            Information = definition.Information with { Corps = [
                new(1, 1, 1, new(10000, 10000)) { IsRoot = true },
                new(2, 1, 2, new(2000, 3000)) { ControlMode = CorpsControlMode.Automatic }] }
        };
        var session = GameSession.Start(definition, 42);
        var draft = new DeploymentDraft(session.View, 1); Assert.True(draft.SetMembers([1]));
        var targets = new DeploymentTargets(draft);
        Assert.Equal(MilitaryMarker.Enter, targets.Candidates[new(2, false)]);
        Assert.True(targets.Select(new(2, false))); Assert.True(targets.BeginPolicies());
        Assert.Equal(ArrivalEstimateStatus.Known, targets.EstimateArrival().Status);
        Assert.True(session.Execute(new("player"), targets.CreateCommand(Guid.NewGuid(),
            session.Snapshot.Simulation.NextSequence)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        for (int i = 0; i < 10 && session.Snapshot.Units.Count != 0; i++)
            EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        Assert.Empty(session.Snapshot.Units);
        Assert.Equal(1, session.Snapshot.Settlements.Single(s => s.Id == 2).CorpsId);
        Assert.DoesNotContain(session.Snapshot.Corps, c => c.Id == 2);
        Assert.Equal(2, session.Snapshot.Officers.Single(p => p.Id == 1).SettlementId);
        Assert.Equal(OfficerStatus.Serving, session.Snapshot.Officers.Single(p => p.Id == 2).Status);
        StateAssert.Equal(session.Capture(), restored.Capture());
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
    }

    [Fact]
    public void ArrivalCacheTracksDelayRelayAndBothFormations()
    {
        var session = GameSession.Start(Fixture(), 42);
        var draft = new DeploymentDraft(session.View, 1); draft.SetMembers([1]);
        var targets = new DeploymentTargets(draft);
        Assert.Equal(ArrivalEstimateStatus.NotApplicable, targets.EstimateArrival().Status);
        targets.Select(new(2, false)); var direct = targets.EstimateArrival();
        Assert.True(targets.SetWaypoint(new(0, 2))); var relay = targets.EstimateArrival();
        Assert.True(relay.Days > direct.Days);
        targets.BeginPolicies(); targets.SetDepartureDelay(9);
        Assert.Equal(relay.Days + 9, targets.EstimateArrival().Days);
        targets.SetDepartureDelay(0);
        Assert.True(draft.SetFormation(101, false)); var slow = targets.EstimateArrival();
        Assert.True(slow.Days > relay.Days);
        Assert.True(draft.SetFormation(201, true));
        Assert.True(targets.EstimateArrival().Days < slow.Days);
    }
}
