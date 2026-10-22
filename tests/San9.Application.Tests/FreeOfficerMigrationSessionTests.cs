using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class FreeOfficerMigrationSessionTests
{
    private static ScenarioInitialState Scenario()
    {
        var fixture = G01ScenarioTests.Fixture();
        return fixture with
        {
            Settlements = [fixture.Settlements[0] with { X = 0, Y = 0 }, fixture.Settlements[1] with { X = 180, Y = 0 }],
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2" },
            RegionCityIds = new() { [1] = 1, [2] = 2 }, CityConnections = [new(2, 1)],
            Officers = [.. fixture.Officers, new("free", new(9000, null, 2, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Free, BirthYear = 170 })]
        };
    }

    [Fact]
    public void MigrationCancelsRecruitmentAndRoundTripsSave()
    {
        var scenario = Scenario();
        const ulong seed = 2UL;
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "personnel-free-migration")).NextUInt64() % 100 < 50);
        var session = GameSession.Start(scenario, seed);
        session.AdvanceXun(); session.AdvanceXun();
        var command = new RecruitOfficerCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 2, 9000);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new CommandOperation(command), "player", command.CommandId)).Disposition);
        Assert.Contains(session.Snapshot.Simulation.Tasks, t => t.Id == command.CommandId);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        var result = session.AdvanceXun();
        Assert.Equal(new FreeOfficerMigratedEvent(new(190, 2, 1), 9000, 2, 1), Assert.Single(result.Events.OfType<FreeOfficerMigratedEvent>()));
        Assert.Contains(result.Events.OfType<TaskCancelledEvent>(), e => e.TaskId == command.CommandId);
        Assert.DoesNotContain(session.Snapshot.Simulation.Tasks, t => t.Kind == "personnel-recruit");
        Assert.Equal(1, session.Snapshot.Officers.Single(p => p.Id == 9000).SettlementId);
        Assert.Contains(session.View.Officers, p => p.Id == 9000);
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var after = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.Empty(after.InitialEvents);
        StateAssert.Equal(session.Capture(), after.Capture());
    }

    [Fact]
    public void DisabledMigrationIsIndependentOfAppearance()
    {
        var scenario = Scenario() with { FreeOfficerMigrationEnabled = false };
        var session = GameSession.Start(scenario, 190);
        for (int i = 0; i < 6; i++) Assert.Empty(session.AdvanceXun().Events.OfType<FreeOfficerMigratedEvent>());
        Assert.True(session.Snapshot.Simulation.AnnualAppearancesEnabled);
        Assert.False(session.Snapshot.Simulation.FreeOfficerMigrationEnabled);
        Assert.Equal(2, session.Snapshot.Officers.Single(p => p.Id == 9000).SettlementId);
        Assert.DoesNotContain(session.Snapshot.Simulation.RandomStreams, s => s.Name.StartsWith("personnel-free-migration"));

        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.False(restored.Snapshot.Simulation.FreeOfficerMigrationEnabled);
    }
}
