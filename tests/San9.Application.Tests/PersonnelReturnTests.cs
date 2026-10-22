using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PersonnelReturnTests
{
    [Fact]
    public void ReleaseImmediatelyCancelsRecruitmentAndReturnsTheRecruiter()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with { Information = baseline.Information with { Relations = [new(1, 2, 80)] }, Officers = [.. baseline.Officers,
            new OfficerInitialState("captive", new OfficerState(7, 2, 1, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Captive }.WithLoyalty(1, 80))] };
        var session = GameSession.Start(definition, 190);
        Assert.True(session.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 2, 7)).Submission.Accepted);
        var result = session.Execute(new("player"), new ReleasePrisonerCommand(Guid.NewGuid(), 1, 1, 7));
        Assert.True(result.Submission.Accepted);
        Assert.Single(result.Events.OfType<TaskCancelledEvent>());
        Assert.Equal(2, session.Snapshot.Simulation.Tasks.Length);
        Assert.All(session.Snapshot.Simulation.Tasks, task => Assert.Equal("personnel-return", task.Kind));
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
    }

    [Fact]
    public void UnaffiliatedCaptiveBecomesFreeAtAdjacentRegionOrRejectsWithoutDestination()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with { RegionNameKeys = new() { [1] = "city.1", [2] = "city.2" },
            RegionCityIds = new() { [1] = 1, [2] = 2 },
            CityConnections = [new(1, 2)], Officers = [.. baseline.Officers,
                new OfficerInitialState("captive", new OfficerState(7, null, 1, new(50, 50, 50, 50), false)
                    { Status = OfficerStatus.Captive, Righteousness = 15 }.WithLoyalty(1, 60))] };
        foreach (bool connected in new[] { false, true })
        {
            var content = connected ? definition : definition with { CityConnections = [] };
            var session = GameSession.Start(content, 190);
            var before = session.Snapshot;
            var result = session.Execute(new("player"), new ReleasePrisonerCommand(Guid.NewGuid(), 1, 0, 7));
            Assert.Equal(connected, result.Submission.Accepted);
            if (!connected) { StateAssert.Equal(before, session.Snapshot); continue; }
            var person = session.Snapshot.Officers.Single(p => p.Id == 7);
            Assert.Equal(OfficerStatus.Free, person.Status);
            Assert.Equal(2, person.SettlementId);
            Assert.Equal(63, person.LoyaltyTo(1));
            Assert.Null(person.OwnerId);
            Assert.Empty(session.Snapshot.Simulation.Tasks);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasedCaptiveReturnsToOriginalForceWithSavedOccupation(bool exile)
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Settlements = [baseline.Settlements[0], baseline.Settlements[1] with { X = 120, Y = 1 }],
            Officers = [.. baseline.Officers, new OfficerInitialState("captive", new OfficerState(7, 2, 1,
                new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive, Righteousness = 15 }
                .WithLoyalty(1, 60).WithLoyalty(2, 80))]
        };
        var session = GameSession.Start(definition, 190);
        int originalLoyalty = session.Snapshot.Officers.Single(p => p.Id == 7).LoyaltyTo(2);
        GameCommand command = exile ? new ExileOfficerCommand(Guid.NewGuid(), 1, 0, 7)
            : new ReleasePrisonerCommand(Guid.NewGuid(), 1, 0, 7);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var prisoner = session.Snapshot.Officers.Single(p => p.Id == 7);
        Assert.Equal(OfficerStatus.Serving, prisoner.Status);
        Assert.Equal(2, prisoner.OwnerId);
        Assert.Equal(63, prisoner.LoyaltyTo(1));
        Assert.Equal(originalLoyalty, prisoner.LoyaltyTo(2));
        Assert.Equal("personnel-return", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        bool returned = false;
        for (int xun = 0; xun < 3; xun++)
        {
            var request = session.Request(new AdvanceOperation());
            var result = session.Apply(request);
            EventAssert.Equal(result.Events, restored.Apply(request).Events);
            returned |= result.Events.OfType<PersonnelReturnedEvent>().Any(e => e.OfficerId == 7);
            restored = SaveService.Decode(SaveService.Encode(restored), definition);
        }
        Assert.True(returned);
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        StateAssert.Equal(session.Capture(), restored.Capture());
    }
}
