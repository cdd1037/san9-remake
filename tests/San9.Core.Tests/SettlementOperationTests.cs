using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class SettlementOperationTests
{
    private static readonly CommandContext Player = new("player");
    private static readonly MilitaryRulebook Military = new(new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
        [new(1, 5, 0, 10000, 1, new(1, 1), TravelMode.Land)
            { Combat = new(10, 10, 10, 10, 50), Attack = new(10, true), CaptureRanks = [BattleRank.Middle] }],
        [new(1, new(2, 2), new(1, 1)) { Combat = new(10, 10, 10, 10, 50) },
         new(2, new(12, 2), new(1, 1)), new(3, new(27, 2), new(1, 1))]));
    private static readonly PersonnelRulebook Geography = new(
        [new(1, new(2, 2)), new(2, new(12, 2)), new(3, new(27, 2))],
        new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 3 });

    private static SimulationEngine Engine(bool confused = false, bool engaged = false, bool destination = false)
    {
        var world = WorldState.Create(new(190, 2, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
            [new(1, 1, 5000, 100, 200, 50, 0, 0, 100, 0, 100)
                { DraftPopulation = 10000, MerchantPresenceChance = 100, MerchantPresent = true, ConfusionDaysRemaining = confused && !destination ? 1 : 0 },
             new(2, 1, 100, 100, 200, 50, 0, 0, 100, 0, 100)
                { ConfusionDaysRemaining = confused && destination ? 1 : 0 },
             new(3, 2, 100, 100, 200, 50, 0, 0, 100, 0, 100)],
            [new(11, 1, 1, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler },
             new(12, 1, 1, new(80, 80, 80, 80), false),
             new(21, 2, 3, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler },
             new(22, 2, 3, new(80, 80, 80, 80), false) { ForceLoyalties = System.Collections.Immutable.ImmutableDictionary<int, int>.Empty.Add(2, 80) },
             new(30, null, 3, new(80, 80, 80, 80), false) { Status = OfficerStatus.Free }],
            units: engaged ? [new(1, 2, destination ? new(14, 2) : new(4, 2), 100)
                { FormationId = 1, Morale = 100, Order = new(MilitaryOrderKind.AttackSettlement, null, destination ? 2 : 1) }] : [],
            controls: [new("player", 1, ControllerKind.Player)],
            corps: [new CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true },
                new CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }]);
        return new(world, EconomyRulebook.Empty, Geography, TreasureRulebook.Empty, military: Military);
    }

    [Theory]
    [InlineData(DomesticActivity.Patrol)]
    [InlineData(DomesticActivity.Commerce)]
    [InlineData(DomesticActivity.Cultivation)]
    [InlineData(DomesticActivity.Repair)]
    [InlineData(DomesticActivity.Recruit)]
    [InlineData(DomesticActivity.Train)]
    public void DomesticWorkRechecksCurrentOriginAndRejectsWithoutEffects(DomesticActivity activity)
    {
        var command = new DevelopCommand(Guid.NewGuid(), 1, 0, new(1, 1, [12], activity));
        Assert.True(Engine().Execute(Player, command).Submission.Accepted);
        foreach (var engine in new[] { Engine(confused: true), Engine(engaged: true) })
        {
            var before = engine.Snapshot;
            Assert.Equal(CommandRejection.DevelopmentFailed, engine.GetPreparationEligibility(Player, command));
            var result = engine.Execute(Player, command);
            Assert.Equal(DevelopmentRejection.SettlementUnavailable, result.Development!.Value.Rejection);
            Assert.False(result.Submission.Accepted);
            Assert.Empty(result.Events);
            StateAssert.Equal(before, engine.Snapshot);
        }
    }

    [Theory]
    [InlineData(FoodTradeDirection.Buy)]
    [InlineData(FoodTradeDirection.Sell)]
    public void FoodTradeQuoteAndSubmissionRejectBlockedOrigin(FoodTradeDirection direction)
    {
        var command = new FoodTradeCommand(Guid.NewGuid(), 1, 0, 1, [12], direction, 100);
        Assert.True(Engine().Execute(Player, command).Submission.Accepted);
        foreach (var engine in new[] { Engine(confused: true), Engine(engaged: true) })
        {
            var before = engine.Snapshot;
            Assert.Equal(FoodTradeRejection.SettlementUnavailable, engine.GetFoodTradeQuote(1, 1, [12], direction).Rejection);
            Assert.Equal(CommandRejection.FoodTradeFailed, engine.Execute(Player, command).Submission.Rejection);
            StateAssert.Equal(before, engine.Snapshot);
        }
    }

    [Theory]
    [InlineData("move")]
    [InlineData("search")]
    [InlineData("recruit")]
    [InlineData("gift")]
    [InlineData("alienate")]
    public void CivilianMissionPreviewsAndSubmissionRejectBlockedOrigin(string kind)
    {
        Guid id = Guid.NewGuid();
        GameCommand command = kind switch
        {
            "move" => new MoveOfficersCommand(id, 1, 0, [12], 2),
            "search" => new SearchCommand(id, 1, 0, [12], 1),
            "recruit" => new RecruitOfficerCommand(id, 1, 0, 12, 30),
            "gift" => new GiftCommand(id, 1, 0, 12, 2, new(GiftKind.Money, 1000, null)),
            _ => new AlienateCommand(id, 1, 0, 12, 22)
        };
        Assert.True(Engine().Execute(Player, command).Submission.Accepted);
        foreach (var engine in new[] { Engine(confused: true), Engine(engaged: true) })
        {
            var before = engine.Snapshot;
            Assert.Equal(CommandRejection.PersonUnavailable, engine.GetPreparationEligibility(Player, command));
            var result = engine.Execute(Player, command);
            Assert.Equal(CommandRejection.PersonUnavailable, result.Submission.Rejection);
            Assert.Empty(result.Events);
            StateAssert.Equal(before, engine.Snapshot);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecallCanMoveFromNormalOriginIntoBlockedDestination(bool engaged)
    {
        var engine = Engine(confused: !engaged, engaged: engaged, destination: true);
        var command = new MoveOfficersCommand(Guid.NewGuid(), 1, 0, [12], 2);
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(Player, command));
        Assert.True(engine.Execute(Player, command).Submission.Accepted);
    }

    [Fact]
    public void SiegeAllowsSortieButConfusionStillRejectsIt()
    {
        var command = new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [12], 1000, 1,
            new(MilitaryOrderKind.Move, new(17, 2), null));
        var besieged = Engine(engaged: true);
        Assert.Contains(1, MilitaryEngagementQuery.Capture(besieged.Snapshot, Military).Settlements);
        Assert.Equal(CommandRejection.None, besieged.GetPreparationEligibility(Player, command));
        Assert.True(besieged.Execute(Player, command).Submission.Accepted);
        var confused = Engine(confused: true, engaged: true);
        var before = confused.Snapshot;
        Assert.Equal(CommandRejection.SettlementConfused, confused.Execute(Player, command).Submission.Rejection);
        StateAssert.Equal(before, confused.Snapshot);
    }
}
