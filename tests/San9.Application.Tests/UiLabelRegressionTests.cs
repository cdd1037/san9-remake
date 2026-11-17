using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed partial class OfficerCommandTests
{
    [Fact]
    public void PersonalTacticPagesIncludeTechnologiesAndExcludeAutomaticShipAndTrapEffects()
    {
        var content = definition with
        {
            Military = definition.Military! with { Tactics = [.. definition.Military.Tactics,
                new(9900, 4, TacticEffect.Martial, 10, 1, 0, false, false, [900]),
                new(9901, 7, TacticEffect.Trap, 10, 1, 0, false, false, [900]),
                new(9902, 5, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [900])] },
            Information = definition.Information with { Tactics = [.. definition.Information.Tactics,
                new(9900, "ship-effect"), new(9901, "trap-effect"), new(9902, "自制技术")] }
        };
        // Excluded engine effects need no officer-page name resolution.
        var pages = OfficerCatalog.Pages(new(content, Deployed()), texts);
        var tactics = pages.SelectMany(p => p.Columns).Where(c => c.Id.StartsWith("tactic.")).ToArray();
        Assert.Equal(["tactic.0", "tactic.2", "tactic.9902"], tactics.Select(c => c.Id));
        Assert.Equal("自制技术", tactics[^1].Title);
    }

    [Theory]
    [InlineData(false, UnitCondition.Normal, "进军")]
    [InlineData(true, UnitCondition.Normal, "撤退")]
    [InlineData(false, UnitCondition.Disguised, "撤退")]
    [InlineData(false, UnitCondition.Routed, "撤退")]
    public void FriendlyEntryAndForcedReturnHaveDistinctLabelsInEveryUnitView(bool automatic, UnitCondition condition, string expected)
    {
        var world = Deployed();
        world = world with { Units = world.Units.Select(u => u.Id == 10 ? u with
            { Order = new(MilitaryOrderKind.Return, null, 2), AutonomousReturn = automatic, Condition = condition } : u).ToArray() };
        var view = new StrategySnapshot(definition, world);
        var unit = world.Units.Single(u => u.Id == 10);
        Assert.Equal(expected, new OfficerCommands(view, texts).ReadUnit(unit, 1).Order.Text);
        Assert.Equal(expected, UnitCatalog.Read(view, view.MilitaryRules, texts, 4).Rows.Single(r => r.Key.Id == 10).Cells[1].Text);
        Assert.Equal(expected, UnitDetails.Read(view, view.MilitaryRules, texts, 10).Orders[0].Value);
        Assert.Equal(expected, MapCommandSummary.Unit(view, texts, 10).Rows.Single(r => r.Label == "命令").Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9009)]
    public void AuthoredTacticNamesAreSharedByOfficerUnitTreasureAndForceViews(int tacticId)
    {
        var content = definition with
        {
            Military = definition.Military! with { Tactics = definition.Military.Tactics.Select(t => t.Id == 0 ? t with { Id = tacticId } : t).ToImmutableArray() },
            Information = definition.Information with
            {
                Tactics = definition.Information.Tactics.Select(t => t.Id == 0 ? t with { Id = tacticId, Name = "自定义兵法" } : t).ToArray(),
                Treasures = [definition.Information.Treasures[0] with { TacticId = tacticId, Description = "说明" }]
            }
        };
        var world = Deployed();
        world = world with
        {
            Officers = world.Officers.Select(p => p.Id == 1 ? p with { EquippedTacticId = tacticId, LearnedTactics = [tacticId] } : p).ToArray(),
            Forces = world.Forces.Select(f => f.Id == 1 ? f with { ProvidedTactics = [tacticId] } : f).ToArray(),
            Treasures = [new(9000, new OfficerTreasurePlacement(1))]
        };
        var view = new StrategySnapshot(content, world);
        Assert.Equal("自定义兵法", OfficerCatalog.Pages(view, texts)[4].Columns.Single(c => c.Id == $"tactic.{tacticId}").Title);
        Assert.Contains(OfficerDetails.Read(view, texts, 1).Tactics.SelectMany(g => g.Tactics), t => t.Name == "自定义兵法");
        Assert.Equal("自定义兵法", UnitDetails.Read(view, view.MilitaryRules, texts, 10).Board.Members.Single(m => m.OfficerId == 1).Tactic);
        Assert.Equal("自定义兵法", TreasureCatalog.Read(view, texts).Rows.Single().Cells[4].Text);
        Assert.Equal("自定义兵法", TreasureDetails.Read(view, texts, 9000).Tactic);
        Assert.Equal("自定义兵法", new AdministrativeCatalog(view, texts).Cell(InformationCategory.Forces, 1, "tactics").Text);
    }
}

public sealed partial class MilitarySessionTests
{
    [Theory]
    [InlineData(UnitCondition.Normal, 2, "输送")]
    [InlineData(UnitCondition.Disguised, 1, "撤退")]
    [InlineData(UnitCondition.Routed, 1, "撤退")]
    public void ForcedTransportReturnUsesItsHomeAsTheDisplayedTarget(UnitCondition condition, int target, string order)
    {
        var content = Fixture();
        var session = GameSession.Start(content, 42);
        Assert.True(session.Execute(new("player"), new San9.Core.Simulation.TransportUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 400, 2)).Submission.Accepted);
        var saved = session.Capture();
        saved = saved with { World = saved.World with { Units = saved.World.Units.Select(u => u with
            { Condition = condition, ConditionDaysRemaining = condition == UnitCondition.Normal ? 0 : 5 }).ToArray() } };
        session = SaveService.Decode(SaveService.EncodeSnapshot(saved), content);
        var texts = MissionScenario.Texts(session.View);
        var result = new OfficerCommands(session.View, texts).ReadUnit(session.Snapshot.Units.Single(), 1);
        Assert.Equal(order, result.Order.Text);
        Assert.Equal(session.View.Settlements.Single(s => s.Id == target).ResolveName(texts), result.Target.Text);
    }
}

public sealed class SettlementLabelRegressionTests
{
    [Theory]
    [InlineData(false, false, "一般")]
    [InlineData(false, true, "交战")]
    [InlineData(true, true, "混乱")]
    public void SettlementCatalogAndMapSummaryShareCurrentCondition(bool confused, bool engaged, string expected)
    {
        var view = DeploymentDraftTests.View(world => world with
        {
            Settlements = world.Settlements.Select(s => s.Id == 1 ? s with { ConfusionDaysRemaining = confused ? 3 : 0 } : s).ToArray(),
            Units = engaged ? [new(20, 2, new(8, 7), 1000) { FormationId = 100, CommanderId = 4, ServicePlaceId = 2,
                Morale = 70, Order = new(MilitaryOrderKind.AttackSettlement, null, 1),
                MemberPositions = ImmutableDictionary<int, FormationPosition>.Empty.Add(4, new(1, BattleRank.Middle)) }] : [],
            Officers = world.Officers.Select(p => engaged && p.Id == 4 ? p with { SettlementId = null, UnitId = 20 } : p).ToArray()
        }, configure: source => source with { Military = source.Military! with
        {
            Formations = source.Military.Formations.Select(f => f.Id == 100 ? f with { Attack = new(1, true) } : f).ToImmutableArray(),
            Places = source.Military.Places.Select(p => p with { Combat = new(10, 10, 10, 10, 50) }).ToImmutableArray()
        } });
        Assert.Equal(engaged, view.IsSettlementEngaged(1));
        var texts = MissionScenario.Texts(view);
        Assert.Equal(expected, new AdministrativeCatalog(view, texts).Cell(InformationCategory.Cities, 1, "state").Text);
        Assert.Equal(expected, MapCommandSummary.Settlement(view, texts, 1).Status);
    }
}
