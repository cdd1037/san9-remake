using San9.Core.Domain;
using San9.Core.Simulation;
using System.Collections.Immutable;

namespace San9.Core.Tests;

public sealed class ArmamentPurchaseTests
{
    private static SimulationEngine Engine(bool enabled = true, bool researched = true, uint money = 1000, long? credit = null)
    {
        var force = new ForceState(1, 0) { LeaderId = 1, CompletedResearch = researched ? ["smith"] : [], ArmamentMigrationCredit = credit };
        var world = WorldState.Create(new(190, 1, 1), 5, [force],
            [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
            controls: [new("player", 1, ControllerKind.Player)],
            corps: [new CorpsState(1, 1, 1, new(money, 1000)) { IsRoot = true }],
            newGame: new NewGameSettings { MixedTroopsEnabled = enabled });
        var military = new MilitaryRulebook(new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], [])
        {
            ResearchProjects = [new("smith", 100, 30, [])],
            TroopTypes = [new(0, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            Armaments = [new("armor", "皮甲", ArmamentSlot.Armor, [], 0, 8, 100, "smith")
                { ProductionCostPerThousand = 50, ProductionDays = 30 }]
        });
        return new(world, new SimulationRules { Military = military });
    }

    [Fact]
    public void PurchaseUsesMigrationCreditBeforeTreasuryAndLeavesAnyBalance()
    {
        var engine = Engine(money: 30, credit: 250);
        var first = new PurchaseArmamentCommand(Guid.NewGuid(), 1, 0, 1, "armor", 2);
        var result = engine.Execute(new("player"), first);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(30u, engine.Snapshot.Corps.Single().Treasury.Money);
        Assert.Equal(50, engine.Snapshot.Forces.Single().ArmamentMigrationCredit);
        Assert.Equal(200, Assert.Single(result.Events.OfType<ArmamentPurchasedEvent>()).MigrationCreditUsed);
        var second = first with { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence, Batches = 1 };
        Assert.Equal(CommandRejection.InsufficientFunds, engine.Execute(new("player"), second).Submission.Rejection);
        Assert.Equal(50, engine.Snapshot.Forces.Single().ArmamentMigrationCredit);
    }

    [Fact]
    public void PurchaseRequiresModuleResearchFundsAndValidBatchCount()
    {
        var command = new PurchaseArmamentCommand(Guid.NewGuid(), 1, 0, 1, "armor", 2);
        Assert.Equal(CommandRejection.InvalidTask, Engine(enabled: false).Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidTask, Engine(researched: false).Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(CommandRejection.InsufficientFunds, Engine(money: 100).Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidTask, Engine().Execute(new("player"), command with { Batches = uint.MaxValue }).Submission.Rejection);
        var engine = Engine();
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(800u, engine.Snapshot.Corps.Single().Treasury.Money);
        Assert.Equal(2000u, engine.Snapshot.Forces.Single().ArmamentInventory!["armor"]);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()),
            new SimulationRules { Military = new MilitaryRulebook(new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], [])
            { ResearchProjects = [new("smith", 100, 30, [])], TroopTypes = [new(0, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
                Armaments = [new("armor", "皮甲", ArmamentSlot.Armor, [], 0, 8, 100, "smith")
                    { ProductionCostPerThousand = 50, ProductionDays = 30 }] }) });
        Assert.Equal(2000u, restored.Snapshot.Forces.Single().ArmamentInventory!["armor"]);
    }

    [Fact]
    public void ManufactureRequiresResearchAndCompletesAfterThirtyDaysAcrossRestore()
    {
        var command = new StartArmamentProductionCommand(Guid.NewGuid(), 1, 0, 1, "armor", 2);
        Assert.Equal(CommandRejection.InvalidTask, Engine(researched: false).Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(CommandRejection.InsufficientFunds, Engine(money: 50).Execute(new("player"), command).Submission.Rejection);
        var engine = Engine();
        Assert.True(engine.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(900u, engine.Snapshot.Corps.Single().Treasury.Money);
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], [])
        {
            ResearchProjects = [new("smith", 100, 30, [])], TroopTypes = [new(0, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            Armaments = [new("armor", "皮甲", ArmamentSlot.Armor, [], 0, 8, 100, "smith")
                { ProductionCostPerThousand = 50, ProductionDays = 30 }]
        };
        var rules = new MilitaryRulebook(definition);
        var world = WorldState.Restore(engine.CaptureStableState());
        var first = world.BeginMutation();
        for (int i = 0; i < 15; i++) ArmamentProduction.AdvanceDay(first, rules, []);
        world.Commit(first);
        world = WorldState.Restore(world.CreateSnapshot());
        var second = world.BeginMutation();
        for (int i = 0; i < 15; i++) ArmamentProduction.AdvanceDay(second, rules, []);
        Assert.Null(second.Forces[1].ArmamentProduction);
        Assert.Equal(2000u, second.Forces[1].ArmamentInventory!["armor"]);
    }
}
