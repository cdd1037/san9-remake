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
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, null)],
                [new(900, 5, 0, 100000, 5, new(1, 1), TravelMode.Land) { Tactics = new(15, 2, [], []) }], [])
            {
                Tactics = [new(0, 0, TacticEffect.Martial, 10, 9, 0, true, false, [900]),
                    new(2, 0, TacticEffect.Martial, 10, 9, 0, true, false, [900])]
            },
            RegionNameKeys = new() { [1] = "city.1", [2] = "city.2" },
            FacilityNameSuffixKeys = new() { [9000] = "fort" },
            Information = basis.Information with { Treasures = [new(9000, "treasure", 1, 5, 10, null)], Tactics = [new(0, "tactic.0"), new(2, "tactic.2")] }
        };
        texts = new(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new Dictionary<string, string>
        {
            ["force.1"] = "甲势力", ["force.2"] = "乙势力",
            ["city.1"] = "甲城", ["city.2"] = "乙城",
            ["officer.1"] = "甲", ["officer.2"] = "乙", ["officer.3"] = "丙",
            ["officer.4"] = "丁", ["officer.5"] = "戊", ["officer.6"] = "己",
            ["naval-tactic"] = "水战", ["tactic.0"] = "奋战", ["tactic.2"] = "奋迅",
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

    private ExecutionCommandRow ReadProgress(PersistentTask task, WorldSnapshot? world = null)
    {
        world ??= initial;
        var view = new StrategySnapshot(definition, world with
            { Simulation = world.Simulation with { Tasks = [task] } });
        var row = Assert.Single(ExecutionCommands.Read(view, texts));
        Assert.Equal(texts.Resolve(view.MessageSpeaker(task.OfficerId).NameKey), row.Officer);
        return row;
    }

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
    [InlineData("personnel-search", "探索", "乙城", "—", "乙城")]
    [InlineData("personnel-recruit", "登庸", "丁", "—", "丁")]
    [InlineData("strategy-alienate", "离间", "丁", "—", "丁")]
    [InlineData("personnel-move", "移动", "—", "乙城", "乙城")]
    [InlineData("personnel-return", "撤回", "—", "乙城", "乙城")]
    [InlineData("tactic-study", "修行", "—", "乙城", "乙城")]
    public void PersonnelObjectsAreNotAlwaysPersonsAndTargetsAreNotAlwaysDestinations(string kind, string order,
        string subject, string target, string progressTarget)
    {
        var cells = ReadTask(Task(kind));
        Assert.Equal(new[] { order, subject, target }, cells.Take(3).Select(c => c.Text));
        var progress = ReadProgress(Task(kind));
        Assert.Equal((order, progressTarget), (progress.Command, progress.Target));
    }

    [Theory]
    [InlineData(GiftKind.Money, "资金1000")]
    [InlineData(GiftKind.Food, "兵粮10000")]
    [InlineData(GiftKind.Treasure, "宝刀")]
    public void GiftsShowRecipientForceAndActualOffer(GiftKind kind, string expected)
    {
        var task = Task("diplomacy-gift") with { Gift = new(2, new(kind, kind == GiftKind.Treasure ? 0u : GiftOffer.MinimumAmount(kind), kind == GiftKind.Treasure ? 9000 : null)) };
        Assert.Equal(new[] { "赠与", "丁", expected }, ReadTask(task).Take(3).Select(c => c.Text));
        var progress = ReadProgress(task);
        Assert.Equal(("赠与", "丁"), (progress.Command, progress.Target));
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

    [Theory]
    [InlineData("diplomacy-attack-request", "请求", "丁")]
    [InlineData("diplomacy-prisoner-return", "交还", "丁")]
    [InlineData("diplomacy-surrender", "劝降", "丁")]
    [InlineData("strategy-gossip", "流言", "甲")]
    public void ProgressTargetsUseTheDiplomaticPartyExceptForGossip(string kind, string command, string target)
    {
        var task = Task(kind);
        task = task with
        {
            AttackRequest = kind == "diplomacy-attack-request" ? new(2, 1) : null,
            PrisonerReturn = kind == "diplomacy-prisoner-return" ? new(2, 5) : null,
            Surrender = kind == "diplomacy-surrender" ? new(2) : null,
            Journey = task.Journey! with { Gossip = kind == "strategy-gossip" ? new(2, 1) : null }
        };
        var row = ReadProgress(task);
        Assert.Equal((command, target), (row.Command, row.Target));
    }

    [Fact]
    public void ProgressNamesUseCurrentPeopleAndRulersWithoutDecorations()
    {
        texts.Entries["officer.1"] = "长姓名甲乙";
        texts.Entries["officer.5"] = "新君主";
        var task = Task("diplomacy-gift") with { Gift = new(2, new(GiftKind.Money, 1000, null)) };
        var world = initial with { Forces = initial.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 5 } : f).ToArray() };
        var row = ReadProgress(task, world);
        Assert.Equal(("长姓名甲乙", "新君主"), (row.Officer, row.Target));
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ForeignOrderAndTacticVisibilityAreIndependent(bool orders, bool tactics)
    {
        var world = Deployed();
        world = world with
        {
            Simulation = world.Simulation with { OtherForceOrdersVisible = orders, OtherForceTacticsVisible = tactics },
            Units = [world.Units[0], world.Units[1] with { Order = new(MilitaryOrderKind.AttackSettlement, null, 1) }]
        };
        var cells = Read(world, 4);
        Assert.Equal(orders ? "攻击" : "＊＊", cells[0].Text);
        Assert.Equal(orders ? "甲城" : "＊＊＊＊＊", cells[2].Text);
        Assert.Equal(tactics ? "奋迅" : "＊＊", cells[5].Text);
        Assert.Equal(!orders, cells[0].IsUnavailable);
        Assert.Equal(!tactics, cells[5].IsUnavailable);
        Assert.Equal("中锋", cells[4].Text);
        Assert.Equal(new[] { "攻击", "—", "乙城", "—", "后卫", "奋迅" }, Read(world).Select(c => c.Text));

        var personnel = world with { Units = [world.Units[0], world.Units[1] with { IsPersonnel = true }] };
        Assert.Equal("＊＊＊＊＊", Read(personnel, 4)[2].Text);
        var foreignTask = Task("diplomacy-gift") with { OfficerId = 4, ForceId = 2, Gift = new(1, new(GiftKind.Money, 1000, null)) };
        var hidden = Read(initial with { Simulation = world.Simulation with { Tasks = [foreignTask] } }, 4);
        Assert.Equal("＊＊＊＊＊", hidden[2].Text);
        Assert.DoesNotContain(hidden, c => c.Text.Contains("1000"));
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
        var row = ReadProgress(task, world);
        Assert.Equal((expected, "丁"), (row.Command, row.Target));
        Assert.Equal("戊", ReadProgress(task, world with { Units = [world.Units[0] with { CommanderId = 5 }] }).Target);
    }

    [Theory]
    [InlineData("combat", "奋迅")]
    [InlineData("construction", "—")]
    [InlineData("transport", "—")]
    [InlineData("unformed", "—")]
    [InlineData("naval", "奋迅")]
    public void CommandAndFormationBoardShowEquipmentWithoutSubstitutingAutomaticTactics(string mode, string expected)
    {
        var world = Deployed();
        var military = definition.Military!;
        if (mode == "construction") military = military with { ConstructionFormations = new(900, null) };
        if (mode == "transport") military = military with { TransportFormations = new(900, null) };
        if (mode == "unformed") world = world with { Units = world.Units.Select(u => u with { Condition = UnitCondition.NoFormation }).ToArray() };
        if (mode == "naval") military = military with
        {
            TerrainIds = Enumerable.Repeat(1, 16).ToImmutableArray(), Terrains = [new(1, 1, 1) { IsWater = true }],
            Formations = [.. military.Formations, new(901, 5, 0, 100000, 5, new(1, 1), TravelMode.Water)
                { Tactics = new(15, 2, [], [3]) { AutomaticOpportunityChance = 10 } }],
            Tactics = [.. military.Tactics, new(3, 4, TacticEffect.Martial, 10, 9, 0, false, true, [901])]
        };
        if (mode == "naval") world = world with { Units = world.Units.Select(u => u with { NavalFormationId = 901 }).ToArray() };
        var content = definition with { Military = military, Information = definition.Information with
            { Tactics = [.. definition.Information.Tactics, new(3, "naval-tactic")] } };
        Assert.Equal(expected, Read(world, content: content)[5].Text);
        var detail = UnitDetails.Read(new(content, world), new(military), texts, 10);
        Assert.All(detail.Board.Members, member => Assert.Equal(expected, member.Tactic));
        Assert.All(detail.Board.Members, member => Assert.Equal(mode == "combat", member.TacticAvailable));
        Assert.All(world.Officers, officer => Assert.Equal(2, officer.EquippedTacticId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavalEquipmentNamesSurviveShoreChangesAndOnlyIncompatibleTacticsAreDimmed(bool water)
    {
        var military = definition.Military!;
        military = military with
        {
            TerrainIds = Enumerable.Repeat(water ? 1 : 0, 16).ToImmutableArray(),
            Terrains = [new(0, 1, null), new(1, 1, 1) { IsWater = true }],
            Formations = [.. military.Formations,
                new(901, 5, 0, 100000, 5, new(1, 1), TravelMode.Water)
                    { Tactics = new(15, 2, [], [3]) { AutomaticOpportunityChance = 10 } }],
            Tactics = [military.Tactics[0] with { CompatibleFormations = [900, 901] }, military.Tactics[1],
                new(3, 4, TacticEffect.Martial, 10, 9, 0, false, true, [901])]
        };
        var content = definition with { Military = military, Information = definition.Information with
            { Tactics = [.. definition.Information.Tactics, new(3, "naval-tactic")] } };
        var world = Deployed();
        world = world with
        {
            Units = world.Units.Select(u => u with { NavalFormationId = 901 }).ToArray(),
            Officers = world.Officers.Select(o => o with
                { EquippedTacticId = o.Id == 1 ? 0 : o.Id == 3 ? null : 2 }).ToArray()
        };
        var view = new StrategySnapshot(content, world);
        var members = UnitDetails.Read(view, view.MilitaryRules, texts, 10).Board.Members.ToDictionary(m => m.OfficerId);
        Assert.Equal("奋战", members[1].Tactic);
        Assert.True(members[1].TacticAvailable);
        Assert.Equal("奋迅", members[2].Tactic);
        Assert.Equal(!water, members[2].TacticAvailable);
        Assert.Equal("—", members[3].Tactic);
        Assert.Equal("", Read(world, 3, content)[5].Text);
        Assert.Equal("奋战", Read(world, 1, content)[5].Text);
        var incompatible = Read(world, 2, content)[5];
        Assert.Equal("奋迅", incompatible.Text);
        Assert.Equal(water, incompatible.IsUnavailable);

        var hidden = world with { Simulation = world.Simulation with { OtherForceTacticsVisible = false } };
        var hiddenView = new StrategySnapshot(content, hidden);
        Assert.All(UnitDetails.Read(hiddenView, hiddenView.MilitaryRules, texts, 20).Board.Members,
            member => Assert.Equal("＊＊＊", member.Tactic));
        Assert.Equal("＊＊", Read(hidden, 4, content)[5].Text);
    }

    [Fact]
    public void ConstructionNamesTheFacilityAndGeographicTarget()
    {
        var world = Deployed();
        world = world with { Units = [world.Units[0] with { Order = new(MilitaryOrderKind.Construct, null, null)
            { Construction = new(2, 9000, new(7, 7), 1) } }, world.Units[1]] };
        Assert.Equal(new[] { "城塞", "—", "乙城周围" }, Read(world).Take(3).Select(c => c.Text));
        var move = world with { Units = [world.Units[0] with { Order = new(MilitaryOrderKind.Move, new(7, 7), null) }, world.Units[1]] };
        var mapped = definition with { Military = definition.Military! with { Width = 2, Height = 2, TerrainIds = [0, 0, 0, 0], RegionIds = [1, 1, 2, 2] } };
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
        var row = ReadProgress(task);
        Assert.Equal((expected, "甲城"), (row.Command, row.Target));
    }
}
