using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OfficerCommandTests
{
    private readonly ScenarioInitialState definition;
    private readonly WorldSnapshot initial;
    private readonly TextCatalog texts;

    public OfficerCommandTests()
    {
        var basis = G01ScenarioTests.Fixture();
        initial = GameSession.Start(basis, 1).Snapshot;
        definition = basis with
        {
            RegionNameKeys = new() { [1] = "city.1", [2] = "city.2" },
            FacilityNameSuffixKeys = new() { [9000] = "fort" },
            Information = basis.Information with { Treasures = [new(9000, "treasure", 1, 5, 10, null)] }
        };
        texts = new(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new Dictionary<string, string>
        {
            ["city.1"] = "甲城", ["city.2"] = "乙城",
            ["officer.1"] = "甲", ["officer.2"] = "乙", ["officer.3"] = "丙",
            ["officer.4"] = "丁", ["officer.5"] = "戊", ["officer.6"] = "己",
            ["treasure"] = "宝刀", ["fort"] = "城塞"
        });
    }

    private PersistentTask Task(string kind) => new(Guid.NewGuid(), kind, "player", 1, 1, 1,
        initial.Clock, initial.Clock.Advance(), 0, 0)
    {
        Journey = new(2, 4, null) { Route = new(new(0, 0), new(300, 0), false) }
    };

    private CatalogCell[] Read(WorldSnapshot world, int id = 1, ScenarioInitialState? content = null)
    {
        var view = new StrategySnapshot(content ?? definition, world);
        var row = Assert.Single(OfficerCatalog.Read(view, texts, 2, null, null, id).Rows);
        var fields = OfficerDetails.Read(view, texts, id).Fields[3];
        Assert.Equal(row.Cells.Skip(1).Select(c => c.Text), fields.Select(f => f.Value));
        return row.Cells.Skip(1).ToArray();
    }

    private CatalogCell[] ReadTask(PersistentTask task) => Read(initial with
        { Simulation = initial.Simulation with { Tasks = [task] } });

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "完")]
    public void StationedOfficerShowsCompletionRatherThanAFabricatedStandingOrder(bool acted, string expected)
    {
        var cells = Read(initial with { Officers = initial.Officers.Select(o => o with { Acted = acted }).ToArray() });
        Assert.Equal(expected, cells[0].Text);
        Assert.All(cells.Skip(1).Take(4), c => Assert.Equal(CatalogCell.Unavailable, c));
        Assert.Equal("", cells[5].Text);
    }

    [Theory]
    [InlineData("personnel-search", "探索", "乙城", "—")]
    [InlineData("personnel-recruit", "登庸", "丁", "—")]
    [InlineData("strategy-alienate", "离间", "丁", "—")]
    [InlineData("personnel-move", "移动", "—", "乙城")]
    [InlineData("personnel-return", "撤回", "—", "乙城")]
    [InlineData("tactic-study", "修行", "—", "乙城")]
    public void PersonnelObjectsAreNotAlwaysPersonsAndTargetsAreNotAlwaysDestinations(string kind, string order, string subject, string target)
    {
        var cells = ReadTask(Task(kind));
        Assert.Equal(new[] { order, subject, target }, cells.Take(3).Select(c => c.Text));
    }

    [Theory]
    [InlineData(GiftKind.Money, "资金500")]
    [InlineData(GiftKind.Food, "兵粮500")]
    [InlineData(GiftKind.Treasure, "宝刀")]
    public void GiftsShowRecipientForceAndActualOffer(GiftKind kind, string expected)
    {
        var task = Task("diplomacy-gift") with { Gift = new(2, new(kind, kind == GiftKind.Treasure ? 0u : 500u, kind == GiftKind.Treasure ? 9000 : null)) };
        Assert.Equal(new[] { "赠与", "丁", expected }, ReadTask(task).Take(3).Select(c => c.Text));
    }

    [Fact]
    public void DiplomaticTargetsUseTheMissionPayloadNotTheEnvoysDestination()
    {
        Assert.Equal(new[] { "请求", "丁", "甲城" }, ReadTask(Task("diplomacy-attack-request") with
            { AttackRequest = new(2, 1) }).Take(3).Select(c => c.Text));
        Assert.Equal(new[] { "交还", "丁", "戊" }, ReadTask(Task("diplomacy-prisoner-return") with
            { PrisonerReturn = new(2, 5) }).Take(3).Select(c => c.Text));
        Assert.Equal(new[] { "劝降", "丁", "—" }, ReadTask(Task("diplomacy-surrender") with
            { Surrender = new(2) }).Take(3).Select(c => c.Text));
        var gossip = Task("strategy-gossip");
        gossip = gossip with { Journey = gossip.Journey! with { Gossip = new(2, 1) } };
        Assert.Equal(new[] { "流言", "丁", "甲" }, ReadTask(gossip).Take(3).Select(c => c.Text));
    }

    private WorldSnapshot Deployed() => initial with
    {
        Officers = initial.Officers.Select(o => o with
        {
            SettlementId = null, UnitId = o.OwnerId == 1 ? 10 : 20, IsCommander = false, EquippedTacticId = 2
        }).ToArray(),
        Units =
        [
            new(10, 1, new(2, 2), 1000)
            {
                FormationId = 900, CommanderId = 1, ServicePlaceId = 1,
                Order = new(MilitaryOrderKind.AttackSettlement, null, 2),
                MemberRanks = new Dictionary<int, BattleRank> { [1] = BattleRank.Rear, [2] = BattleRank.Front, [3] = BattleRank.Middle }.ToImmutableDictionary()
            },
            new(20, 2, new(7, 7), 1000)
            {
                FormationId = 900, CommanderId = 4,
                MemberRanks = new Dictionary<int, BattleRank> { [4] = BattleRank.Middle }.ToImmutableDictionary()
            }
        ]
    };

    [Fact]
    public void FieldMembersShareEffectiveOrderButHaveIndividualRanksAndTactics()
    {
        var world = Deployed();
        Assert.Equal(new[] { "攻击", "—", "乙城", "—", "后卫", "奋迅" }, Read(world).Select(c => c.Text));
        Assert.Equal("前锋", Read(world, 2)[4].Text);
        Assert.Equal("中锋", Read(world, 3)[4].Text);
        var engaged = world with { Units = [world.Units[0] with { EngagementTargetId = 20 }, world.Units[1]] };
        Assert.Equal("丁队", Read(engaged)[2].Text);
        var changedLeader = engaged with { Units = [engaged.Units[0], engaged.Units[1] with { CommanderId = 5 }] };
        Assert.Equal("戊队", Read(changedLeader)[2].Text);
        var forcedReturn = world with { Units = [world.Units[0] with { Condition = UnitCondition.Disguised }, world.Units[1]] };
        Assert.Equal(new[] { "撤退", "—", "甲城" }, Read(forcedReturn).Take(3).Select(c => c.Text));
        Assert.Equal("乙城", Read(world)[2].Text);
    }

    [Fact]
    public void ForeignOrdersAreHiddenButRanksAndDefaultEquippedTacticsRemainPublic()
    {
        var cells = Read(Deployed(), 4);
        Assert.Equal(new[] { "＊＊", "＊＊＊＊", "＊＊＊＊＊", "**", "中锋", "奋迅" }, cells.Select(c => c.Text));
        Assert.All(cells.Take(4), c => { Assert.True(c.IsUnavailable); Assert.Null(c.Number); });
        var foreignTask = Task("diplomacy-gift") with { OfficerId = 4, ForceId = 2, Gift = new(1, new(GiftKind.Money, 999, null)) };
        var hidden = Read(initial with { Simulation = initial.Simulation with { Tasks = [foreignTask] } }, 4);
        Assert.Equal("＊＊＊＊＊", hidden[2].Text);
        Assert.DoesNotContain(hidden, c => c.Text.Contains("999"));
    }

    [Fact]
    public void UnrankedTransportAndUnequippedMembersDoNotInventBattlePositionsOrTactics()
    {
        var world = Deployed();
        var transport = world with { Units = [world.Units[0] with
            { MemberRanks = ImmutableDictionary<int, BattleRank>.Empty, Order = new(MilitaryOrderKind.Transport, null, 2) }, world.Units[1]] };
        Assert.Equal(new[] { "输送", "—", "乙城", "—", "—", "—" }, Read(transport).Select(c => c.Text));
        var unselected = world with { Officers = world.Officers.Select(o => o with { EquippedTacticId = null }).ToArray() };
        Assert.Equal("", Read(unselected)[5].Text);
        var changed = world with { Officers = world.Officers.Select(o => o with { EquippedTacticId = 0 }).ToArray() };
        Assert.Equal("奋战", Read(changed)[5].Text);
    }

    [Theory]
    [InlineData(UnitStrategyKind.FalseReport, "伪报")]
    [InlineData(UnitStrategyKind.Disorder, "扰乱")]
    [InlineData(UnitStrategyKind.Inspire, "激励")]
    [InlineData(UnitStrategyKind.Reinforce, "救援")]
    public void UnitStrategiesNameTheTargetUnitInTheTargetColumn(UnitStrategyKind kind, string expected)
    {
        var task = Task("unit-strategy");
        task = task with { Journey = task.Journey! with { UnitStrategy = new(kind, 20) } };
        var world = initial with { Units = [Deployed().Units[1]], Simulation = initial.Simulation with { Tasks = [task] } };
        Assert.Equal(new[] { expected, "—", "丁队" }, Read(world).Take(3).Select(c => c.Text));
    }

    [Fact]
    public void ConstructionNamesTheFacilityAndGeographicTarget()
    {
        var world = Deployed();
        world = world with { Units = [world.Units[0] with { Order = new(MilitaryOrderKind.Construct, null, null)
            { Construction = new(2, 9000, new(7, 7), 1) } }, world.Units[1]] };
        Assert.Equal(new[] { "城塞", "—", "乙城周围" }, Read(world).Take(3).Select(c => c.Text));
        var move = world with { Units = [world.Units[0] with { Order = new(MilitaryOrderKind.Move, new(7, 7), null) }, world.Units[1]] };
        var mapped = definition with { Military = new(2, 2, [0, 0, 0, 0], [new(0, 1, null)], [], []) { RegionIds = [1, 1, 2, 2] } };
        Assert.Equal("乙城周围", Read(move, content: mapped)[2].Text);
    }

    [Theory]
    [InlineData(true, "烧夷")]
    [InlineData(false, "夺取")]
    public void SettlementStrategiesUseCurrentTargetFacilityNames(bool burn, string expected)
    {
        var task = Task(burn ? "strategy-burn" : "strategy-seize");
        task = task with { Journey = task.Journey! with
            { Burn = burn ? new(1) : null, Seize = burn ? null : new(1) } };
        Assert.Equal(new[] { expected, "—", "甲城" }, ReadTask(task).Take(3).Select(c => c.Text));
    }
}
