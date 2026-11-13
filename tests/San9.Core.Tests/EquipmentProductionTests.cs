using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EquipmentProductionTests
{
    private static readonly EquipmentRulebook Catalog = new([new EquipmentDefinition("spear", EquipmentSlot.MainHand,
        EquipmentRarity.Ordinary, new(2, 0, 0, 0), new(1, 0, 0, 0))
        { Name = "枪", ResearchProjectId = "forge", ProductionCost = 200, ProductionDays = 30 }]);

    private static SimulationEngine Engine(bool researched = true, uint funds = 1000, NewGameSettings? settings = null)
    {
        var force = new ForceState(1, 0) { LeaderId = 1, CompletedResearch = researched ? ["forge"] : [] };
        var world = WorldState.Create(new(190, 1, 1), 5, [force],
            [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("player", 1, ControllerKind.Player)],
            corps: [new CorpsState(1, 1, 1, new(funds, 1000)) { IsRoot = true }], equipment: [], newGame: settings);
        return new SimulationEngine(world, new SimulationRules { Equipment = Catalog });
    }

    [Fact]
    public void DisabledEquipmentModuleRejectsManufacturingAndItsResearchWithoutSpendingMoney()
    {
        var settings = new NewGameSettings { EquipmentDevelopmentEnabled = false };
        var world = WorldState.Restore(Engine(researched: false, settings: settings).CaptureStableState()).BeginMutation(equipment: Catalog);
        var military = new MilitaryRulebook(new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], [])
        { ResearchProjects = [new("forge", 100, 30, [])] });
        Assert.False(ResearchModuleRules.IsAvailable(military, Catalog, settings with { MixedTroopsEnabled = true }, "forge"));
        Assert.Equal(CommandRejection.InvalidTask, MilitaryResearch.Start(world,
            new StartResearchCommand(Guid.NewGuid(), 1, 0, 1, "forge"), new SimulationRules { Equipment = Catalog, Military = military }, []));
        Assert.Equal(CommandRejection.InvalidTask, EquipmentProduction.Start(world,
            new StartEquipmentProductionCommand(Guid.NewGuid(), 1, 0, 1, "spear"), Catalog, []));
        Assert.Equal(1000u, world.Corps[1].Treasury.Money);
    }

    [Fact]
    public void OrdinaryOfficerEquipmentCannotBeManufactured()
    {
        var command = new StartEquipmentProductionCommand(Guid.NewGuid(), 1, 0, 1, "spear");
        Assert.Equal(CommandRejection.InvalidTask, Engine(researched: false).Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidTask, Engine(funds: 100).Execute(new("player"), command).Submission.Rejection);
        var engine = Engine();
        Assert.Equal(CommandRejection.InvalidTask, engine.Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(1000u, engine.Snapshot.Corps.Single().Treasury.Money);
    }

    [Fact]
    public void ObsoleteOrdinaryEquipmentResearchIsUnavailable()
    {
        var state = WorldState.Restore(Engine(researched: false).CaptureStableState());
        var world = state.BeginMutation(equipment: Catalog);
        var military = new MilitaryRulebook(new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], [])
        { ResearchProjects = [new("forge", 100, 30, [])] });
        var rules = new SimulationRules { Equipment = Catalog, Military = military };
        Assert.False(world.Simulation.NewGame.MixedTroopsEnabled);
        Assert.Equal(CommandRejection.InvalidTask, MilitaryResearch.Start(world,
            new StartResearchCommand(Guid.NewGuid(), 1, 0, 1, "forge"), rules, []));
        Assert.DoesNotContain("forge", world.Forces[1].CompletedResearch);
        Assert.Equal(CommandRejection.InvalidTask, EquipmentProduction.Start(world,
            new StartEquipmentProductionCommand(Guid.NewGuid(), 1, 1, 1, "spear"), Catalog, []));
    }

    [Fact]
    public void LegacyOrdinaryProductionCannotCompleteIntoOfficerInventory()
    {
        var world = WorldState.Restore(Engine().CaptureStableState()).BeginMutation(equipment: Catalog);
        world.Replace(world.Forces[1] with { EquipmentProduction = new("spear", 1, 1) });
        List<DomainEvent> events = [];
        EquipmentProduction.AdvanceDay(world, Catalog, events);
        Assert.Null(world.Forces[1].EquipmentProduction);
        Assert.Empty(world.AllEquipment);
        Assert.Single(events.OfType<EquipmentProductionCancelledEvent>());
    }

    [Fact]
    public void OwnershipChangeCancelsProduction()
    {
        var world = WorldState.Restore(Engine().CaptureStableState()).BeginMutation(equipment: Catalog);
        world.Replace(world.Forces[1] with { EquipmentProduction = new("spear", 1, 30) });
        List<DomainEvent> events = [];
        MilitaryResearch.CancelAt(world, 1, events);
        Assert.Null(world.Forces[1].EquipmentProduction);
        Assert.Single(events.OfType<EquipmentProductionCancelledEvent>());
        Assert.Empty(world.AllEquipment);
    }
}
