using San9.Tests;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AdministrativeInformationTests
{
    [Fact]
    public void MapModeSurvivesDetailsAndRelatedListsWithoutReplacingCatalogState()
    {
        var browser = new InformationBrowser();
        browser.Open(InformationCategory.Forces);
        browser.SelectPage(3, 5);
        browser.Catalog.SetRows([new(5, [(CatalogCell)10]), new(9, [(CatalogCell)20])]);
        browser.Catalog.Sort(0);
        var catalog = browser.Catalog;
        browser.ShowMap(true);
        browser.DetailSelection.Open([9,5], 5);
        browser.ShowDetail(5);
        browser.Navigate(new("都市", InformationCategory.Cities, ForceId: 5));
        browser.ShowMap(true);
        browser.ShowDetail(15);
        browser.Back();
        Assert.True(browser.IsMap);
        Assert.False(browser.IsDetail);
        browser.Back();
        Assert.Equal(InformationCategory.Forces, browser.Category);
        Assert.True(browser.IsMap);
        Assert.True(browser.IsDetail);
        Assert.Same(catalog, browser.Catalog);
        browser.Back();
        browser.ShowMap(false);
        Assert.Equal(3, browser.Page);
        Assert.Equal(new[] {9,5}, browser.Catalog.Rows.Select(r => r.Id));
        Assert.True(browser.Catalog.Descending);
        Assert.Equal(5, browser.SelectedId);
        browser.Close();
        Assert.False(browser.IsMap);
    }

    [Fact]
    public void MapMarkersKeepUnselectableCitiesAndResolveForceOwnership()
    {
        var (session, _, definition) = Fixture();
        var view = new StrategySnapshot(definition, session.View.World with {
            Settlements = session.View.World.Settlements.Select(s => s.Id == 17 ? s with { OwnerId = null } : s).ToArray()
        });
        var cities = AdministrativeMap.Markers(view, InformationCategory.Cities, [16]);
        Assert.Equal(3, cities.Length);
        Assert.Equal(16, cities.Single(c => c.CityId == 16).TargetId);
        Assert.Null(cities.Single(c => c.CityId == 15).TargetId);
        var forces = AdministrativeMap.Markers(view, InformationCategory.Forces, [5]);
        Assert.Equal(5, forces.Single(c => c.CityId == 16).TargetId);
        Assert.Null(forces.Single(c => c.CityId == 17).TargetId);
    }

    [Fact]
    public void EmptyRegionInheritsAdministrativeOwnershipWithoutBorrowingLocalPeople()
    {
        var (session,text,_) = Fixture();
        var catalog = new AdministrativeCatalog(session.View,text);
        Assert.Contains(catalog.Read(InformationCategory.Regions,0,force:5,corps:10).Rows,r => r.Key.Id == 50);
        Assert.Equal("person1",catalog.Cell(InformationCategory.Regions,50,"force").Text);
        Assert.Equal("person1",catalog.Cell(InformationCategory.Regions,50,"corps-name").Text);
        Assert.Equal("0",catalog.Cell(InformationCategory.Regions,50,"serving").Text);
        Assert.Equal("1",catalog.Cell(InformationCategory.Regions,50,"free").Text);
    }

    [Fact]
    public void DetailCapacityUsesRuntimeRulesAndMapSummaryKeepsPlainAmounts()
    {
        var (session, text, definition) = Fixture();
        var world = session.View.World with { Settlements = session.View.World.Settlements.Select(s => s.Id == 15
            ? s with { SoldierCapacity = 123456, MaxCommerce = 4321 } : s).ToArray() };
        var view = new StrategySnapshot(definition, world);
        var detail = AdministrativeDetails.Read(view, text, new(InformationCategory.Cities, 15));
        var fields = detail.Sections.SelectMany(s => s.Fields).ToArray();
        Assert.Equal("100/123456", fields.Single(f => f.Label == "士兵").Value);
        Assert.Equal("100/4321", fields.Single(f => f.Label == "收益").Value);
        Assert.Equal($"10/{MilitaryRulebook.WoundedCapacity}", fields.Single(f => f.Label == "伤兵").Value);
        var summary = AdministrativeMap.Summary(view, text, new(InformationCategory.Cities,15));
        Assert.Equal("100",summary.Single(f => f.Label == "士兵").Value);
        Assert.Equal(100U,world.Settlements.Single(s => s.Id == 15).Soldiers);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(21)]
    public void AdministrativeSidebarViewportPreservesFocusAtBothEnds(int rows)
    {
        var state = new DetailSelectionState();
        state.SetVisibleRows(rows);
        state.Open(Enumerable.Range(100,40),139);
        Assert.Equal(40-rows,state.Offset);
        state.PointAt(139);
        state.Scroll(0);
        Assert.Equal(100+rows-1,state.FocusedId);
        state.MoveFocus(1);
        Assert.Equal(1,state.Offset);
        Assert.Equal(100+rows,state.FocusedId);
        state.Activate(state.FocusedId!.Value);
        Assert.Equal(100+rows,state.CurrentId);
    }

    private static (GameSession Session, TextCatalog Text, ScenarioInitialState Definition) Fixture()
    {
        SettlementInitialState City(int id, int corps, uint troops) => new(id,$"city{id}",5,id,0,troops,500,1000,50,[])
            { CorpsId = corps, Kind = SettlementKind.City, Population = 10000, DraftPopulation = 1000, Wounded = 10, PublicOrder = 100, Commerce = 100, Cultivation = 100, MaxCommerce = 1000, MaxCultivation = 1000 };
        OfficerInitialState Person(int id, int city, OfficerStatus status) => new($"person{id}",new(id,5,city,new(70,70,70,70),false) { Status = status });
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion,"admin",190,1,1,5,
            [new(5,"force",0,100) { LeaderId = 1, RankId = 20 }], [City(15,10,100),City(16,11,200),City(17,12,300)],
            new() { [15]="city15",[16]="city16",[17]="city17",[50]="empty" }, new() { [15]=15,[16]=16,[17]=17,[50]=15 })
        {
            UnbuiltPlaces = [new(50,new(1000,1000))],
            ProvinceNameKeys = new() { [99]="province" }, CityProvinceIds = new() { [15]=99,[16]=99,[17]=99 },
            Officers = [Person(1,15,OfficerStatus.Ruler),Person(2,16,OfficerStatus.Governor),Person(3,17,OfficerStatus.Governor),
                new("free",new(4,null,null,new(20,20,20,20),false) { Status=OfficerStatus.Free,RegionId=50 })],
            Information = new([new(20,"rank",10000,100,0,3) { DescriptionKey="rank-description" },new(80,"none",1000,0,0,0) { IsUnranked=true }], [], [],
                [new(10,5,1,new(100, 200)) { IsRoot = true },new(11,5,2,new(300,400)) { Delegation=new DomesticDelegation() },new(12,5,3,new(500,600))], [])
        };
        var keys = new[] { "force","city15","city16","city17","empty","province","person1","person2","person3","free","rank","none","rank-description" };
        var text = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion,keys.ToDictionary(k=>k,k=>k));
        return (GameSession.Start(definition,1),text,definition);
    }
    private static CatalogCell Cell(InformationTable table, int id, string column) => table.Rows.Single(r=>r.Key.Id==id).Cells[Array.IndexOf(table.Columns,column)];
    [Theory]
    [InlineData(false, false, "")]
    [InlineData(true, false, "")]
    [InlineData(true, true, "○")]
    public void EmptyRegionsAndWorksInheritCityOwnershipButGarrisonsKeepTheirOwn(bool exists, bool canHost, string expected)
    {
        var (session, text, definition) = Fixture();
        definition = definition with {
            Settlements = definition.Settlements.Select((s, i) => s with { X = i, Y = 0 }).ToArray(),
            RegionCityIds = new() { [15] = 15, [50] = 15, [17] = 17 },
            Military = new(3, 1, [0, 0, 0], [new(0, 1, null)], [],
                [new(15, MapCoordinates.Center(new(0, 0)), new(1, 1)),
                 new(16, MapCoordinates.Center(new(1, 0)), new(1, 1)),
                 new(17, MapCoordinates.Center(new(2, 0)), new(1, 1))]) {
                RegionIds = [15, 50, 17], TerritoryExits = [TerritoryExits.East, TerritoryExits.East, TerritoryExits.None]
            }
        };
        var world = session.View.World;
        var view = new StrategySnapshot(definition, world with {
            Settlements = world.Settlements.Select(s => s.Id == 16 ? s with {
                Kind = SettlementKind.Facility, OwnerId = null, CanHostPersons = canHost, IsDestroyed = !exists } : s).ToArray()
        });
        var catalog = new AdministrativeCatalog(view, text);
        Assert.Equal(expected, catalog.Cell(InformationCategory.Regions, 15, "frontline").Text);
        Assert.Equal("", catalog.Cell(InformationCategory.Regions, 50, "frontline").Text);
    }
    [Fact]
    public void AdjacencyUsesLeftAlignedRegionNamesRatherThanFacilityNames()
    {
        var (session, _, definition) = Fixture();
        var entries = new[] { "force", "city15", "city16", "city17", "empty", "province", "person1", "person2", "person3", "free" }
            .ToDictionary(k => k, k => k);
        entries["city16"] = "安平港";
        entries["region16"] = "安平";
        entries["region17"] = "港口原名";
        var text = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, entries);
        definition = definition with {
            Settlements = definition.Settlements.Select((s, i) => s with { X = i, Y = 0 }).ToArray(),
            RegionNameKeys = new(definition.RegionNameKeys) { [16] = "region16", [17] = "region17" },
            Military = new(3, 1, [0, 0, 0], [new(0, 1, null)], [],
                [new(15, MapCoordinates.Center(new(0, 0)), new(1, 1)),
                 new(16, MapCoordinates.Center(new(1, 0)), new(1, 1)),
                 new(17, MapCoordinates.Center(new(2, 0)), new(1, 1))]) {
                RegionIds = [16, 15, 17], TerritoryExits = [TerritoryExits.None, TerritoryExits.All, TerritoryExits.None]
            }
        };
        var catalog = new AdministrativeCatalog(new StrategySnapshot(definition, session.View.World), text);
        Assert.Equal("安平‧港口原名", catalog.Cell(InformationCategory.Regions, 15, "adjacent").Text);
        Assert.Equal(CatalogAlignment.Left, AdministrativeCatalog.Pages(InformationCategory.Cities)
            .SelectMany(p => p.Columns).Single(c => c.Id == "regions").Alignment);
        foreach (var category in new[] { InformationCategory.Cities, InformationCategory.Regions })
            Assert.Equal(CatalogAlignment.Left, AdministrativeCatalog.Pages(category).SelectMany(p => p.Columns)
                .Single(c => c.Id == "adjacent").Alignment);
    }
    [Fact]
    public void RankColumnsMatchOriginalWidthsAndDescriptionAlignment()
    {
        var columns = AdministrativeCatalog.Pages(InformationCategory.Ranks)[0].Columns;
        Assert.Equal(new[] { 56, 56, 56, 232, 232 }, columns.Select(c => c.Width));
        Assert.Equal(CatalogAlignment.Left, columns.Single(c => c.Id == "description").Alignment);
    }
    [Theory]
    [InlineData(5, 5, 6, true, "○")]
    [InlineData(5, 5, null, true, "○")]
    [InlineData(5, 5, 5, true, "")]
    [InlineData(null, 5, 6, true, "")]
    [InlineData(5, 5, 6, false, "")]
    [InlineData(5, 6, null, true, "")]
    public void CityFrontlineUsesOwnedSubregionsAndIncludesUnownedNeighbors(int? cityOwner, int subregionOwner,
        int? neighborOwner, bool borderOpen, string expected)
    {
        var (session, text, definition) = Fixture();
        var places = definition.Settlements.Select((s, i) => s with { X = i, Y = 0,
            Kind = s.Id == 16 ? SettlementKind.Facility : SettlementKind.City }).ToArray();
        definition = definition with {
            Forces = [.. definition.Forces, definition.Forces[0] with { Id = 6 }],
            Settlements = places,
            RegionCityIds = new() { [15] = 15, [50] = 15, [17] = 17 },
            Military = new(3, 1, [0, 0, 0], [new(0, 1, null)], [],
                [new(15, MapCoordinates.Center(new(0, 0)), new(1, 1)),
                 new(16, MapCoordinates.Center(new(1, 0)), new(1, 1)),
                 new(17, MapCoordinates.Center(new(2, 0)), new(1, 1))]) {
                RegionIds = [15, 50, 17],
                TerritoryExits = [TerritoryExits.None, borderOpen ? TerritoryExits.East : TerritoryExits.None, TerritoryExits.None]
            }
        };
        var world = session.View.World;
        var view = new StrategySnapshot(definition, world with {
            Forces = [.. world.Forces, world.Forces[0] with { Id = 6 }],
            Settlements = world.Settlements.Select(s => s with {
                OwnerId = s.Id == 15 ? cityOwner : s.Id == 16 ? subregionOwner : neighborOwner,
                Kind = s.Id == 16 ? SettlementKind.Facility : SettlementKind.City }).ToArray()
        });
        var catalog = new AdministrativeCatalog(view, text);
        Assert.Equal(expected, catalog.Cell(InformationCategory.Cities, 15, "frontline").Text);
        Assert.Equal(borderOpen && neighborOwner != subregionOwner ? "○" : "",
            catalog.Cell(InformationCategory.Regions, 50, "frontline").Text);
    }
    [Theory]
    [InlineData(100, true, "常驻", 2)]
    [InlineData(50, true, "驻在", 1)]
    [InlineData(50, false, "", 0)]
    [InlineData(0, false, "", 0)]
    public void MerchantDisplayDistinguishesPermanentVisitingAndAbsent(int chance, bool present, string expected, long order)
    {
        var (session, text, definition) = Fixture();
        var world = session.View.World;
        var view = new StrategySnapshot(definition, world with {
            Settlements = world.Settlements.Select(s => s.Id == 15
                ? s with { MerchantPresenceChance = chance, MerchantPresent = present } : s).ToArray()
        });
        var cell = new AdministrativeCatalog(view, text).Cell(InformationCategory.Cities, 15, "merchant");
        Assert.Equal(expected, cell.Text);
        Assert.Equal(order, cell.Number);
        Assert.False(cell.IsUnavailable);
        var detail = AdministrativeDetails.Read(view, text, new(InformationCategory.Cities, 15));
        Assert.Equal(expected, detail.Sections.SelectMany(s => s.Fields).Single(f => f.Label == "商人").Value);
    }
    [Theory]
    [InlineData(5, 6, "×")]
    [InlineData(6, 5, "◎")]
    [InlineData(6, 7, "○")]
    public void AllianceRolesUseSymbols(int target, int leader, string expected)
    {
        var (session, text, definition) = Fixture();
        var world = session.View.World;
        var view = new StrategySnapshot(definition, world with {
            Simulation = world.Simulation with { Alliance = new(target, leader, target == 5 ? [6, 7] : [5, 7], 10) }
        });
        Assert.Equal(expected, new AdministrativeCatalog(view, text).Cell(InformationCategory.Forces, 5, "alliance").Text);
        Assert.True(new AdministrativeCatalog(session.View, text).Cell(InformationCategory.Forces, 5, "alliance").IsUnavailable);
    }
    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "person1")]
    [InlineData(2, "person1‧person1")]
    [InlineData(3, "person1‧person1‧person1")]
    [InlineData(4, "person1‧person1及其他2势力")]
    [InlineData(5, "person1‧person1及其他3势力")]
    public void RankHoldersListThreeButSummarizeFourOrMore(int count, string expected)
    {
        var (session, text, definition) = Fixture();
        var source = definition.Forces[0];
        var state = session.View.World.Forces[0];
        var view = new StrategySnapshot(definition with {
            Forces = [source with { RankId = 80 }, .. Enumerable.Range(100, count).Select(id => source with { Id = id })]
        }, session.View.World with {
            Forces = [state with { RankId = 80 }, .. Enumerable.Range(100, count).Select(id => state with { Id = id })]
        });
        Assert.Equal(expected, new AdministrativeCatalog(view, text).Cell(InformationCategory.Ranks, 20, "holders").Text);
    }
    [Fact]
    public void RegionNamesUseMiddleDots()
    {
        var (session, text, _) = Fixture();
        Assert.Equal("city15‧empty", new AdministrativeCatalog(session.View, text).Cell(InformationCategory.Cities, 15, "regions").Text);
    }
    [Fact]
    public void ForceTotalsIncludeSubaccountsAndFieldTroopsWithoutDuplicatingDirectTreasury()
    {
        var (session,text,definition)=Fixture();
        var world=session.View.World with { Forces=[new(5,100) { LeaderId=1,RankId=20 }], Corps=definition.Information.Corps,
            Units=[new(40,5,new(100,100),70) { CorpsId=11,Wounded=7 }] };
        var view=new StrategySnapshot(definition,world);
        var catalog=new AdministrativeCatalog(view,text);
        Assert.Equal(900,Cell(catalog.Read(InformationCategory.Forces,2),5,"资金").Number);
        Assert.Equal(1200,Cell(catalog.Read(InformationCategory.Forces,3),5,"兵粮").Number);
        Assert.Equal(100,Cell(catalog.Read(InformationCategory.Corps,2),10,"资金").Number);
        Assert.Equal(300,Cell(catalog.Read(InformationCategory.Corps,2),11,"资金").Number);
        Assert.Equal(670,Cell(catalog.Read(InformationCategory.Forces,1),5,"士兵").Number);
        Assert.Equal(37,Cell(catalog.Read(InformationCategory.Forces,1),5,"伤兵").Number);
        Assert.Equal(270,Cell(catalog.Read(InformationCategory.Corps,1),11,"士兵").Number);
    }
    [Fact]
    public void CorpsAndEmptyRegionLinksDoNotBroadenTheirCandidates()
    {
        var (session,text,_)=Fixture(); var query=new InformationQuery(session,text);
        var corps=query.Detail(new(InformationCategory.Corps,11));
        var cities=corps.Links.Single(l=>l.Category==InformationCategory.Cities);
        Assert.Equal(11,cities.CorpsId);
        Assert.Equal(new[]{16},query.Read(cities.Category,corps:cities.CorpsId).Rows.Select(r=>r.Key.Id));
        var officers=corps.Links.Single(l=>l.Label=="武将");
        Assert.Equal(new[]{2},query.Read(officers.Category,force:officers.ForceId,corps:officers.CorpsId).Rows.Select(r=>r.Key.Id));
        var region=query.Detail(new(InformationCategory.Regions,50));
        Assert.Equal(15,region.SettlementId); // Map fallback only.
        var local=region.Links.Single(l=>l.Category==InformationCategory.Officers);
        Assert.Null(local.SettlementId); Assert.Equal(50,local.RegionId);
        Assert.Equal(new[]{4},query.Read(local.Category,region:local.RegionId).Rows.Select(r=>r.Key.Id));
        Assert.Empty(query.Read(InformationCategory.Units,region:50).Rows);
    }
    [Fact]
    public void AuthoredUnrankedIdentityIsNotListedAndDescriptionsAndProvinceNamesAreResolved()
    {
        var (session,text,_)=Fixture(); var query=new InformationQuery(session,text);
        Assert.Equal(new[]{20},query.Read(InformationCategory.Ranks).Rows.Select(r=>r.Key.Id));
        Assert.Equal("rank-description",Cell(query.Read(InformationCategory.Ranks),20,"说明").Text);
        Assert.Equal("province",Cell(query.Read(InformationCategory.Cities),15,"州").Text);
        Assert.Equal("内政",Cell(query.Read(InformationCategory.Corps),11,"方针").Text);
        Assert.True(Cell(query.Read(InformationCategory.Corps),11,"目标").IsUnavailable);
        Assert.True(Cell(query.Read(InformationCategory.Corps),10,"方针").IsUnavailable);
    }
    [Fact]
    public void EnemyDelegationAndRequestFieldsHaveNoNumericSortKey()
    {
        var (session,text,definition)=Fixture();
        var view=new StrategySnapshot(definition with { PlayerForceId=99 },session.View.World);
        var catalog=new AdministrativeCatalog(view,text);
        var hidden=Cell(catalog.Read(InformationCategory.Corps,0),11,"方针");
        Assert.True(hidden.IsUnavailable); Assert.Null(hidden.Number); Assert.Contains("＊",hidden.Text);
        Assert.True(Cell(catalog.Read(InformationCategory.Forces,4),5,"请求者").IsUnavailable);
    }
    [Fact]
    public void AllPagesFitTheSharedCatalogAndAllDetailQueriesAreReadOnly()
    {
        var (session,text,_)=Fixture(); var query=new InformationQuery(session,text); var before=session.View.World;
        foreach(var (category,count) in new[]{(InformationCategory.Ranks,1),(InformationCategory.Forces,5),(InformationCategory.Corps,4),(InformationCategory.Cities,5),(InformationCategory.Regions,3)})
        {
            Assert.Equal(count,AdministrativeCatalog.Pages(category).Count);
            foreach(var page in AdministrativeCatalog.Pages(category)) Assert.True(page.Columns.Sum(c=>c.Width)+96<=1024);
            for(int page=0;page<count;page++)
                foreach(var row in query.Read(category,page).Rows) Assert.Equal(AdministrativeCatalog.Pages(category)[page].Columns.Length,row.Cells.Length);
            if(category!=InformationCategory.Ranks)
                foreach(var row in query.Read(category).Rows) Assert.NotEmpty(AdministrativeDetails.Read(session.View,text,row.Key).Sections);
        }
        StateAssert.Equal(before,session.View.World);
    }
    [Fact]
    public void ReturningFromRelatedListsRestoresCatalogAndDetailFocusIndependently()
    {
        var browser=new InformationBrowser(); browser.Open(InformationCategory.Corps,force:5,corps:11);
        browser.Catalog.SetRows([new(11,["a"]),new(12,["b"])]); browser.Catalog.Sort(0);
        browser.DetailSelection.Open([12,11],11); browser.DetailSelection.PointAt(12); browser.ShowDetail(11);
        var original=browser.Catalog;
        browser.Navigate(new("武将",InformationCategory.Officers,5,CorpsId:11));
        Assert.Equal(11,browser.CorpsFilter); Assert.NotSame(original,browser.Catalog);
        browser.Back(); Assert.Same(original,browser.Catalog); Assert.Equal(11,browser.DetailSelection.CurrentId); Assert.Equal(12,browser.DetailSelection.FocusedId);
        Assert.Equal(new[]{12,11},browser.Catalog.Rows.Select(r=>r.Id));
    }
}
