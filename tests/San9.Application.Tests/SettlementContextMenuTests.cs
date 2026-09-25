using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class SettlementContextMenuTests
{
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
        var before = session.Snapshot.StateHash;
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
        Assert.Equal(before, session.Snapshot.StateHash);
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
            Corps = [new(77, 1, 1, null), new(88, 1, 2, new(100, 100))]
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
                [new(77, "book", 1, 0, 0, 902)], [], [new(77, 1, TreasureDiscovery.Discovered, null)]) {
                Tactics = [new(901, "land-tech"), new(902, "ship-tech"), new(903, "attack") { DescriptionKey = "attack-description" }, new(904, "trap")],
                Formations = [new(731, "base"), new(732, "advanced"), new(733, "ship")]
            },
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, 1)],
                [Formation(731, TravelMode.Land), Formation(732, TravelMode.Land, 901), Formation(733, TravelMode.Water, 902)],
                basis.Settlements.Select(s => new BattlefieldPlace(s.Id, MapCoordinates.Center(new(s.X, s.Y)), new(1, 1))).ToImmutableArray()) {
                Elevations = Enumerable.Repeat(0, 16).ToImmutableArray(),
                TrapTerrains = [new(0, 905, true)], HigherGroundTrap = new(906, 1),
                Tactics = [new(901, 5, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [732]),
                    new(902, 4, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [733]),
                    new(903, 2, TacticEffect.Martial, 13, 9, 150, true, true, [731, 733]),
                    new(904, 6, TacticEffect.TrapSelection, 0, 0, 0, true, false, [731]),
                    new(905, 6, TacticEffect.Trap, 0, firstInterval, 0, false, false, [731]),
                    new(906, 6, TacticEffect.Trap, 0, secondInterval, 0, false, false, [731])]
            }
        };
        var keys = basis.Officers.Select(o => o.NameKey).Concat(["trap", "attack", "attack-description", "rank", "higher", "office", "office-description", "higher-office", "book", "land-tech", "ship-tech", "base", "advanced", "ship"]);
        var texts = new TextCatalog(3, keys.ToDictionary(k => k, k => new TextEntry(k, k)));
        var session = GameSession.Start(definition, 1);
        var query = new InformationQuery(session, texts);
        string hash = session.Snapshot.StateHash;
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
        Assert.Equal([733], query.Read(InformationCategory.Ships, force: 1).Rows.Select(r => r.Key.Id));
        Assert.Empty(query.Read(InformationCategory.Tactics, force: 2).Rows);
        Assert.Equal([731], query.Read(InformationCategory.Formations, force: 2).Rows.Select(r => r.Key.Id));
        Assert.Equal(hash, session.Snapshot.StateHash);
    }
}
