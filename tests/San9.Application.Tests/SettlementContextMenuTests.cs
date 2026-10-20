using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class SettlementContextMenuTests
{
    [Theory]
    [InlineData(9000, false, false, false)]
    [InlineData(9000, false, false, true)]
    [InlineData(9001, true, false, false)]
    [InlineData(9001, true, false, true)]
    [InlineData(9002, true, true, false)]
    [InlineData(9002, true, true, true)]
    public void WorkHoverAndContextUseIndependentAuthoredCapabilities(int type, bool work, bool excluded, bool constructing)
    {
        var basis = G01ScenarioTests.Fixture();
        var definition = basis with
        {
            RegionNameKeys = basis.Settlements.ToDictionary(s => s.Id, s => s.NameKey),
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, 1)],
                [new(1, 10, 50, 10000, 5, new(1, 1), TravelMode.Land)],
                basis.Settlements.Select(s => new BattlefieldPlace(s.Id, s.Position, new(1, 1)) { FacilityTypeId = 9000 }).ToImmutableArray())
            {
                Facilities = [new(9000, 0, 1000, new(1, 1), 0, true, true, true, 100000, null, null, null, []),
                    new(9001, 0, 1000, new(1, 1), 0, true, false, false, 100000, null, null, null, []) { IsWork = true },
                    new(9002, 0, 1000, new(1, 1), 0, true, false, false, 100000, null, null, null, []) { IsWork = true }]
            },
            Information = basis.Information with { MapContextExcludedFacilityIds = [9002] }
        };
        var session = GameSession.Start(definition, 1);
        var before = session.Snapshot;
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            basis.RegionNameKeys.Values.Concat(basis.Officers.Select(o => o.NameKey)).Concat(basis.Forces.Select(f => f.NameKey))
                .Concat(basis.Settlements.Select(s => s.NameKey)).Distinct().ToDictionary(k => k, k => k));
        foreach (int? owner in new int?[] { 1, 2, null })
        {
            var snapshot = before with { Settlements = before.Settlements.Select(s => s.Id == 1 ? s with
            {
                Kind = SettlementKind.Facility, OwnerId = owner, CanHostPersons = !work && !constructing,
                Facility = new(type, basis.Settlements.Single(p => p.Id == s.Id).Position) { BuilderUnitId = constructing ? 999 : null }
            } : s).ToArray() };
            var view = new StrategySnapshot(definition, snapshot);
            var projected = view.Settlements.Single(s => s.Id == 1);
            Assert.Equal(work, projected.IsWork);
            Assert.Equal(!excluded, projected.HasMapContextMenu);
            Assert.Equal(constructing, projected.IsUnderConstruction);
            foreach (var context in new[] { MapInformationContext.Strategy, MapInformationContext.Execution })
                Assert.Equal(work, MapHoverInfo.Resolve(view, texts, 1, context) is null);
            var entries = SettlementContextMenu.Read(view, 1);
            if (excluded) Assert.Empty(entries);
            else if (work || constructing) Assert.Equal(InformationCategory.Regions, Assert.Single(entries).Target.Category);
            else Assert.NotEmpty(entries);
            if (work) Assert.Empty(MapCommandMenu.Settlement(view, 1));
        }
        StateAssert.Equal(before, session.Snapshot);
    }

    [Theory]
    [InlineData(SettlementKind.City, null)]
    [InlineData(SettlementKind.City, 1)]
    [InlineData(SettlementKind.City, 2)]
    [InlineData(SettlementKind.Facility, null)]
    [InlineData(SettlementKind.Facility, 1)]
    [InlineData(SettlementKind.Facility, 2)]
    public void ContextUsesClickedObjectAndItsCurrentOwnerWithoutSelectingCommands(SettlementKind kind, int? owner)
    {
        var definition = G01ScenarioTests.Fixture();
        var session = GameSession.Start(definition, 1);
        var before = session.Snapshot;
        var snapshot = session.Snapshot with { Settlements = session.Snapshot.Settlements.Select(s =>
            s.Id == 1 ? s with { Kind = kind, OwnerId = owner } : s).ToArray() };
        var entries = SettlementContextMenu.Read(new(definition, snapshot), 1);
        Assert.Equal(owner is null ? 2 : 8, entries.Count);
        Assert.Equal(kind == SettlementKind.City ? InformationCategory.Cities : InformationCategory.Regions, entries[0].Target.Category);
        Assert.Equal(1, entries[0].DetailId);
        Assert.Equal(1, entries[1].Target.RegionId);
        Assert.Null(entries[1].Target.ForceId);
        Assert.True(entries[1].Enabled);
        Assert.All(entries.Skip(2), e => Assert.Equal(owner, e.Target.ForceId));
        StateAssert.Equal(before, session.Snapshot);
    }

    [Fact]
    public void EmptyGarrisonIsDisabledAndNonGarrisonWorksOnlyExposeRegionInformation()
    {
        var definition = G01ScenarioTests.Fixture();
        var snapshot = GameSession.Start(definition, 1).Snapshot;
        var empty = snapshot with { Officers = snapshot.Officers.Where(o => o.SettlementId != 1).ToArray() };
        Assert.False(SettlementContextMenu.Read(new(definition, empty), 1)[1].Enabled);
        var work = snapshot with { Settlements = snapshot.Settlements.Select(s => s.Id == 1
            ? s with { Kind = SettlementKind.Facility, CanHostPersons = false } : s).ToArray() };
        Assert.Equal(InformationCategory.Regions, Assert.Single(SettlementContextMenu.Read(new(definition, work), 1)).Target.Category);
        Assert.Empty(SettlementContextMenu.Read(new(definition, snapshot), 999));
        var destroyed = snapshot with { Settlements = snapshot.Settlements.Select(s => s with { IsDestroyed = true }).ToArray() };
        Assert.Empty(SettlementContextMenu.Read(new(definition, destroyed), 1));
    }

    [Fact]
    public void CorpsAndBarbarianBranchesUseSemanticOwnership()
    {
        var definition = G01ScenarioTests.Fixture();
        var snapshot = GameSession.Start(definition, 1).Snapshot;
        snapshot = snapshot with {
            Settlements = snapshot.Settlements.Select(s => s.Id == 1 ? s with { CorpsId = 77 } : s).ToArray(),
            Corps = [new(77, 1, 1, new(1000, 2000)) { IsRoot = true }, new(88, 1, 2, new(100, 100))]
        };
        var entries = SettlementContextMenu.Read(new(definition, snapshot), 1);
        Assert.Equal(10, entries.Count);
        Assert.Equal(77, entries.Single(e => e.Title == "军团情报").DetailId);
        Assert.Equal(77, entries.Single(e => e.Title == "军团武将").Target.CorpsId);
        snapshot = snapshot with { Forces = snapshot.Forces.Select(f => f.Id == 1 ? f with { IsBarbarian = true } : f).ToArray() };
        Assert.DoesNotContain(SettlementContextMenu.Read(new(definition, snapshot), 1), e => e.Target.Category == InformationCategory.Offices);
    }

    [Fact]
    public void DirectDetailsReturnToMapAfterNestedListsAndNextContextClearsOldFilters()
    {
        var browser = new InformationBrowser();
        browser.OpenContext(new("都市情报", new("", InformationCategory.Cities, SettlementId: 17), 17));
        browser.Navigate(new("武将", InformationCategory.Officers, RegionId: 17));
        browser.ShowDetail(900);
        browser.Back(); Assert.True(browser.IsOpen); Assert.False(browser.IsDetail);
        browser.Back(); Assert.True(browser.IsDetail); Assert.Equal(17, browser.SelectedId);
        browser.Back(); Assert.False(browser.IsOpen);
        browser.OpenContext(new("势力武将", new("", InformationCategory.Officers, ForceId: 2)));
        Assert.Equal(2, browser.ForceFilter); Assert.Null(browser.SettlementFilter); Assert.Null(browser.RegionFilter);
        browser.Back(); Assert.False(browser.IsOpen);
    }

    [Theory]
    [InlineData(9, 9, "9")]
    [InlineData(9, 12, "9～12")]
    [InlineData(0, 0, "0")]
    public void CapabilityCatalogUsesAuthoredIdsAndCurrentLearningTreasuresAndRank(int firstInterval, int secondInterval, string expectedInterval)
    {
        var basis = G01ScenarioTests.Fixture();
        FormationRule Formation(int id, TravelMode mode, int? tactic = null) => new(id, 10, 50, 10000, 5, new(1, 1), mode) { RequiredTacticId = tactic };
        var definition = basis with {
            Forces = basis.Forces.Select(f => f.Id == 1 ? f with { RankId = 700 } : f).ToArray(),
            Officers = basis.Officers.Select(o => o.State.Id == 1 ? o with { State = o.State with { LearnedTactics = [901, 903, 904] } }
                : o.State.Id == 2 ? o with { State = o.State with { OfficeId = 888 } } : o).ToArray(),
            Information = new([new(700, "rank", 10000, 0, 0, 9), new(100, "higher", 20000, 0, 0, 10)],
                [new(888, "office", 10000, 11) { RequiredRankId = 700, DescriptionKey = "office-description" }, new(999, "higher-office", 20000, 20) { RequiredRankId = 100 }],
                [new(77, "book", 1, 0, 0, 902)], basis.Information.Corps, [new(77, 1, TreasureDiscovery.Discovered, null)]) {
                Tactics = [new(901, "land-tech"), new(902, "ship-tech"), new(903, "attack") { DescriptionKey = "attack-description" }, new(904, "trap"), new(907, "wave"), new(908, "rain")],
                Formations = [new(731, "base"), new(732, "advanced"), new(733, "ship") { DescriptionKey = "ship-description" }]
            },
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, 1)],
                [Formation(731, TravelMode.Land) with { MovementBudget = 61, Tactics = new(15, 0, 3, [1, 2], []),
                    DoublesCapture = true, DoublesDuelChance = true, SnipingChanceBonus = 30 }, Formation(732, TravelMode.Land, 901), Formation(733, TravelMode.Water, 902) with {
                    MovementBudget = 61, Tactics = new(10, 0, 2, [], [908, 907]) }],
                basis.Settlements.Select(s => new BattlefieldPlace(s.Id, MapCoordinates.Center(new(s.X, s.Y)), new(1, 1))).ToImmutableArray()) {
                Elevations = Enumerable.Repeat(0, 16).ToImmutableArray(),
                TrapTerrains = [new(0, 905, true)], HigherGroundTrap = new(906, 1),
                Tactics = [new(901, 5, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [732]),
                    new(902, 4, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [733]),
                    new(903, 2, TacticEffect.Martial, 13, 9, 150, true, true, [731, 733]),
                    new(904, 6, TacticEffect.TrapSelection, 0, 0, 0, true, false, [731]),
                    new(905, 6, TacticEffect.Trap, 0, firstInterval, 0, false, false, [731]),
                    new(906, 6, TacticEffect.Trap, 0, secondInterval, 0, false, false, [731]),
                    new(907, 4, TacticEffect.Martial, 10, 0, 0, false, false, [733]),
                    new(908, 4, TacticEffect.Martial, 20, 0, 0, false, false, [733])]
            }
        };
        var keys = basis.Officers.Select(o => o.NameKey).Concat(["wave", "rain", "ship-description", "trap", "attack", "attack-description", "rank", "higher", "office", "office-description", "higher-office", "book", "land-tech", "ship-tech", "base", "advanced", "ship"]);
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, keys.ToDictionary(k => k, k => k));
        var session = GameSession.Start(definition, 1);
        var query = new InformationQuery(session, texts);
        var hash = session.Snapshot;
        Assert.Throws<InvalidDataException>(() => (definition with {
            Information = definition.Information with { Tactics = [new(901, "land-tech")] }
        }).Validate());
        foreach (var category in new[] { InformationCategory.Offices, InformationCategory.Tactics, InformationCategory.Formations, InformationCategory.Ships })
        {
            Assert.All(ForceCapabilityCatalog.Pages(category), page => Assert.True(page.Columns.Sum(c => c.Width) + 96 <= 1024));
            var table = query.Read(category, force: 1);
            Assert.All(table.Rows, row => Assert.Equal(table.Columns.Length, row.Cells.Length));
        }
        var offices = query.Read(InformationCategory.Offices, force: 1);
        Assert.Equal(["爵位", "武将", "指挥", "俸禄", "统率", "武力", "智力", "政治", "忠诚上升"], offices.Columns);
        var office = Assert.Single(offices.Rows);
        Assert.Equal(888, office.Key.Id);
        Assert.Equal("officer.2", office.Cells[1].Text);
        Assert.Equal(5, office.Cells[^1].Number);
        var descriptions = query.Read(InformationCategory.Offices, 1, force: 1);
        Assert.Equal(offices.Rows.Select(r => r.Key), descriptions.Rows.Select(r => r.Key));
        Assert.Equal("office-description", Assert.Single(descriptions.Rows).Cells[1].Text);
        var tactics = query.Read(InformationCategory.Tactics, force: 1);
        var attack = Assert.Single(tactics.Rows, r => r.Key.Id == 903);
        Assert.Equal(expectedInterval, Assert.Single(tactics.Rows, r => r.Key.Id == 904).Cells[^1].Text);
        Assert.Equal(903, attack.Key.Id);
        Assert.Equal(["兵法", "系统", "威力", "陆上", "水上", "对设施", "熟练要求", "使用间隔"], tactics.Columns);
        Assert.Equal(["attack", "弓骑", "13", "○", "○", "○", "150", "9"], attack.Cells.Select(c => c.Text));
        Assert.Equal("attack-description", Assert.Single(query.Read(InformationCategory.Tactics, 1, force: 1).Rows, r => r.Key.Id == 903).Cells[1].Text);
        Assert.Equal([731, 732], query.Read(InformationCategory.Formations, force: 1).Rows.Select(r => r.Key.Id));
        var formations = query.Read(InformationCategory.Formations, force: 1);
        Assert.Equal(["特征", "效果", "说明"], formations.Pages);
        Assert.Equal(["阵形", "对部队", "对守兵", "对城壁", "守备力", "机动力", "费用"], formations.Columns);
        Assert.Equal("12.2", formations.Rows.Single(r => r.Key.Id == 731).Cells[5].Text);
        var effects = query.Read(InformationCategory.Formations, 1, force: 1);
        Assert.Equal("易于发动骑兵、弓骑系兵法。较易俘获敌方武将。较易发动兵法连锁。较易发动单挑。较易发动狙击。",
            effects.Rows.Single(r => r.Key.Id == 731).Cells[1].Text);
        Assert.Equal("", effects.Rows.Single(r => r.Key.Id == 732).Cells[1].Text);
        Assert.Equal(formations.Rows.Select(r => r.Key), query.Read(InformationCategory.Formations, 2, force: 1).Rows.Select(r => r.Key));
        Assert.Equal([733], query.Read(InformationCategory.Ships, force: 1).Rows.Select(r => r.Key.Id));
        var ships = query.Read(InformationCategory.Ships, force: 1);
        Assert.Equal(["特征", "效果", "说明"], ships.Pages);
        Assert.Equal(["船", "对部队", "对守兵", "对城壁", "守备力", "机动力", "费用"], ships.Columns);
        Assert.Equal("12.2", Assert.Single(ships.Rows).Cells[5].Text);
        Assert.Equal("可发动兵法「rain」「wave」。", Assert.Single(query.Read(InformationCategory.Ships, 1, force: 1).Rows).Cells[1].Text);
        Assert.Equal("ship-description", Assert.Single(query.Read(InformationCategory.Ships, 2, force: 1).Rows).Cells[1].Text);
        Assert.Throws<InvalidDataException>(() => (definition with {
            Information = definition.Information with { Tactics = definition.Information.Tactics.Where(t => t.Id != 907).ToArray() }
        }).Validate());
        var withoutAutomaticTactics = definition with { Military = definition.Military with {
            Formations = definition.Military.Formations.Select(f => f.Id == 733 ? f with { Tactics = null } : f).ToImmutableArray() } };
        Assert.Equal("", Assert.Single(new InformationQuery(GameSession.Start(withoutAutomaticTactics, 1), texts)
            .Read(InformationCategory.Ships, 1, force: 1).Rows).Cells[1].Text);
        Assert.Empty(query.Read(InformationCategory.Tactics, force: 2).Rows);
        Assert.Equal([731], query.Read(InformationCategory.Formations, force: 2).Rows.Select(r => r.Key.Id));
        StateAssert.Equal(hash, session.Snapshot);
    }
}
