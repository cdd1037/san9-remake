using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class RecruitmentTerritoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransferSettlesCaptivesAndLastCityLifecycleInTheSameAdvance(bool keepCity)
    {
        var geography = new PersonnelRulebook([new(1, new(2, 2)), new(2, new(32, 2)), new(3, new(62, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 2 }, [new(1, 2), new(2, 1)]);
        static OfficerState Person(int id, int owner, int place, bool ruler = false) =>
            new OfficerState(id, owner, place, new(80, 80, 100, 80), false)
            { Status = ruler ? OfficerStatus.Ruler : OfficerStatus.Serving }
            .WithLoyalty(1, 255).WithLoyalty(2, 0);
        static SettlementState Place(int id, int owner) => new(id, owner, 1000, 100, 100, 70, 500, 100, 1000, 100, 1000)
            { CorpsId = owner, ConsumesFood = false };
        var world = WorldState.Create(new(190, 1, 1), 190,
            [new(1, 1000) { LeaderId = 1, BaseTrust = 1000 },
             new(2, 0) { LeaderId = 2 }],
            [Place(1, 1), Place(2, 2), Place(3, 2) with { Kind = keepCity ? SettlementKind.City : SettlementKind.Facility }],
            [Person(1, 1, 1, true), Person(2, 2, 3, true), Person(11, 1, 1),
             Person(20, 2, 2) with { IsCommander = true },
             Person(30, 1, 2) with { Status = OfficerStatus.Captive },
             Person(31, 2, 2) with { Status = OfficerStatus.Captive }],
            corps: [new(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new(2, 2, 2, new(10000, 10000)) { IsRoot = true }], relations: [new(1, 2, 50)],
            controls: [new("player", 1, ControllerKind.Player), new("ai:2", 2, ControllerKind.Automatic)],
            naturalDeathsEnabled: false, forceEmergenceEnabled: false, cityIndependenceEnabled: false,
            forceConsolidationEnabled: false, settlementDefectionsEnabled: false, unitDefectionsEnabled: false);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, geography);
        Assert.True(engine.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 11, 20)).Submission.Accepted);
        engine.EndControl(new("player")); engine.EndControl(new("ai:2"));
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty, geography);
        var advance = engine.AdvanceXun();
        EventAssert.Equal(advance.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.True(Assert.Single(advance.Events.OfType<RecruitmentResolvedEvent>()).Success);
        var snapshot = engine.CaptureStableState();
        Assert.Equal(1, snapshot.Settlements.Single(s => s.Id == 2).OwnerId);
        Assert.Equal(!keepCity, snapshot.Forces.Single(f => f.Id == 2).IsEliminated);
        Assert.Equal(keepCity, snapshot.Simulation.Controls.Any(c => c.ForceId == 2));
        Assert.Equal(keepCity ? 0 : 1, advance.Events.OfType<ForceEliminatedEvent>().Count(e => e.ForceId == 2));
        var captive = snapshot.Officers.Single(p => p.Id == 30);
        Assert.Equal(OfficerStatus.Serving, captive.Status);
        Assert.Equal(1, captive.OwnerId);
        Assert.Equal(2, captive.SettlementId);
        Assert.Contains(advance.Events.OfType<PrisonerDisplacedEvent>(), e => e.OfficerId == 30);
        Assert.DoesNotContain(snapshot.Officers, p => p.Id == 31 && p.Status == OfficerStatus.Captive);
        if (!keepCity)
        {
            Assert.DoesNotContain(snapshot.Corps, c => c.OwnerId == 2);
            Assert.DoesNotContain(snapshot.Simulation.Tasks, t => t.ForceId == 2);
        }
    }
}
