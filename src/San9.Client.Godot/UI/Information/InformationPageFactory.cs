using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

internal sealed class InformationPageFactory
{
    public required InformationBrowser State { get; init; }
    public required GameSession Session { get; init; }
    public required TextCatalog Texts { get; init; }
    public required TextureService Service { get; init; }
    public required TextureScope Textures { get; init; }
    public required MapWorld World { get; init; }
    public required San9.Content.Interface.InterfaceSkin Skin { get; init; }
    public required InformationQuery Query { get; init; }
    public required Action Rebuild { get; init; }
    public required Action Back { get; init; }
    public Control Create()
    {
        if (State.Category == InformationCategory.Units && State.IsDetail)
        {
            var unitDetail = new UnitDetailView
            {
                State = State.DetailSelection, Textures = Textures,
                Read = id => Query.UnitDetail(id),
                ReadName = id => Query.UnitDetail(id).Name,
                CurrentChanged = State.Select, Closed = Back,
                OpenOfficers = id => { State.Navigate(new("武将", InformationCategory.Officers, UnitId: id)); Rebuild(); },
                OpenOfficer = id =>
                {
                    State.Navigate(new("武将", InformationCategory.Officers, OfficerId: id));
                    State.OfficerDetail.Open([id],id); State.ShowDetail(id); Rebuild();
                }
            };
            return unitDetail;
        }
        if (State.Category == InformationCategory.Units && !State.IsDetail)
        {
            var catalog = new CatalogView
            {
                Service = Service, Textures = Textures, TableState = State.Catalog,
                Title = "部队一览", HintText = "请选择欲观看其情报的部队。", Pages = UnitCatalog.Pages, TabStripFollowsPage = true,
                ReadRows = page => Query.Read(InformationCategory.Units, page, State.ForceFilter, corps: State.CorpsFilter, region: State.RegionFilter)
                    .Rows.Select(r => new CatalogRow(r.Key.Id, r.Cells)).ToArray(),
                Closed = Back, Activated = id =>
                {
                    State.DetailSelection.SetVisibleRows(UnitDetailView.VisibleRows);
                    State.DetailSelection.Open(State.Catalog.Rows.Select(r => r.Id), id);
                    State.ShowDetail(id); Rebuild();
                },
            };
            return catalog;
        }
        if (ForceCapabilityCatalog.Supports(State.Category))
        {
            var catalog = new CatalogView
            {
                Service = Service, Textures = Textures, TableState = State.Catalog,
                Title = State.Category is InformationCategory.Offices or InformationCategory.Tactics or InformationCategory.Formations or InformationCategory.Ships ? InformationQuery.MenuTitle(State.Category)[1..] + "一览" : "势力" + InformationQuery.MenuTitle(State.Category)[1..],
                HintText = State.Category == InformationCategory.Offices ? "画面显示君主能任命的爵位。"
                    : State.Category == InformationCategory.Ships ? "画面显示着能使用的船。"
                    : State.Category == InformationCategory.Formations ? "画面显示着能使用的阵形。"
                    : "查看" + Texts.Resolve(Session.View.Forces.Single(f => f.Id == State.ForceFilter).NameKey) + "势力可用的项目。",
                Pages = ForceCapabilityCatalog.Pages(State.Category),
                ReadRows = page => Query.Read(State.Category, page, State.ForceFilter).Rows.Select(r => new CatalogRow(r.Key.Id, r.Cells)).ToArray(), Closed = Back
            };
            return catalog;
        }
        if (AdministrativeCatalog.Supports(State.Category))
        {
            var category = State.Category;
            if (State.IsDetail)
            {
                var administrativeDetail = new AdministrativeDetailView
                {
                    State = State.DetailSelection, Category = category, Title = InformationQuery.MenuTitle(category)[1..] + "情报", Service = Service, Textures = Textures,
                    Read = id => AdministrativeDetails.Read(Session.View, Texts, new(category, id)),
                    ReadName = id => new AdministrativeCatalog(Session.View, Texts).Cell(category, id, "name").Text,
                    Available = link => Query.Read(link.Category, 0, link.ForceId, link.SettlementId, link.CorpsId, link.RegionId, link.OfficerId).Rows.Length > 0,
                    Navigate = link => { State.Navigate(link); Rebuild(); }, CurrentChanged = State.Select, Closed = Back,
                    Preview = id => World.CreateInformationPreview(id, Texts)
                };
                return administrativeDetail;
            }
            if (State.IsMap)
            {
                var rows = Query.Read(State).Rows;
                State.Catalog.RefreshRows(rows.Select(r => new CatalogRow(r.Key.Id,r.Cells)).ToArray());
                var informationMap = new InformationMapView {
                    Session = Session, Texts = Texts, Skin = Skin, Service = Service, Textures = Textures, Category = category,
                    ReadCandidates = () => Query.Read(State).Rows.Select(r => r.Key.Id).ToArray(), Closed = Back,
                    ShowList = () => { State.ShowMap(false); Rebuild(); },
                    Activated = id => OpenAdministrativeDetail(category,id)
                };
                return informationMap;
            }
            var pages = AdministrativeCatalog.Pages(category);
            var catalog = new CatalogView
            {
                Service = Service, Textures = Textures, TableState = State.Catalog, Title = InformationQuery.MenuTitle(category)[1..] + "一览", Pages = pages,
                HintText = category == InformationCategory.Ranks ? "查看官爵的指挥、信望、说明与就任势力。" : "请选择欲观看其情报的项目。",
                ReadRows = page => Query.Read(category, page, State.ForceFilter, State.SettlementFilter, State.CorpsFilter, State.RegionFilter).Rows.Select(r => new CatalogRow(r.Key.Id,r.Cells)).ToArray(),
                Closed = Back, ShowMap = category is InformationCategory.Forces or InformationCategory.Cities ? () => { State.ShowMap(true); Rebuild(); } : null,
                Activated = category == InformationCategory.Ranks ? null : id => OpenAdministrativeDetail(category,id),
            };
            return catalog;
        }
        if (State.Category == InformationCategory.Officers && !State.IsDetail)
        {
            var catalog = new CatalogView
            {
                Service = Service,
                TableState = State.Catalog, Title = "武将一览", HintText = Texts.ResolveRich(StrategyHintText.Key("catalog-officers")), Pages = OfficerCatalog.Pages, Textures = Textures,
                ReadRows = page => Query.Read(State.Category, page, State.ForceFilter, State.SettlementFilter, State.CorpsFilter, State.RegionFilter, State.OfficerFilter, State.UnitFilter).Rows
                    .Select(row => new CatalogRow(row.Key.Id, row.Cells)).ToArray(),
                Portrait = id => $"catalog-person-{id}",
                PortraitDeceased = id => Session.View.World.Officers.Any(o => o.Id == id && o.Status == San9.Core.Domain.OfficerStatus.Dead),
                Closed = Back, Activated = id =>
                {
                    State.OfficerDetail.Open(State.Catalog.Rows.Select(row => row.Id), id);
                    State.ShowDetail(id); Rebuild();
                },
            };
            return catalog;
        }
        if (State.Category == InformationCategory.Officers && State.IsDetail)
        {
            var officerDetail = new OfficerDetailView
            {
                Service = Service,
                State = State.OfficerDetail, Textures = Textures, Texts = Texts,
                Read = id => OfficerDetails.Read(Session.View, Texts, id),
                ReadName = id => Texts.Resolve(Session.View.Officers.Single(o => o.Id == id).NameKey),
                OpenTreasures = id => { State.Navigate(new("宝物", InformationCategory.Treasures, HolderId: id)); Rebuild(); },
                CurrentChanged = State.Select, Closed = Back
            };
            return officerDetail;
        }
        if (State.Category == InformationCategory.Treasures && State.IsDetail)
        {
            var treasureDetail = new TreasureDetailView
            {
                Service = Service,
                State = State.DetailSelection, Textures = Textures, Texts = Texts,
                Read = id => TreasureDetails.Read(Session.View, Texts, id),
                ReadName = id => Texts.Resolve(Session.View.Information.Treasures.Single(t => t.Id == id).NameKey),
                CurrentChanged = State.Select, Closed = Back
            };
            return treasureDetail;
        }
        if (State.Category == InformationCategory.Treasures)
        {
            var catalog = new CatalogView
            {
                Service = Service,
                TableState = State.Catalog, Title = "宝物一览", HintText = "请选择欲观看其情报的宝物。",
                Pages = TreasureCatalog.Pages, Textures = Textures,
                ReadRows = _ => TreasureCatalog.Read(Session.View, Texts, State.HolderFilter, State.ForceFilter, State.SettlementFilter)
                    .Rows.Select(row => new CatalogRow(row.Key.Id, row.Cells)).ToArray(),
                Portrait = id => $"information-treasure-{id}", PortraitSize = new(120, 120),
                Closed = Back, Activated = id =>
                {
                    State.DetailSelection.Open(State.Catalog.Rows.Select(row => row.Id), id);
                    State.ShowDetail(id); Rebuild();
                },
            };
            return catalog;
        }
        return new InformationJournalView { State = State, Session = Session, Query = Query, Textures = Textures, Back = Back };
    }
    private void OpenAdministrativeDetail(InformationCategory category, int id)
    {
        State.DetailSelection.SetVisibleRows(AdministrativeDetailView.VisibleRows(category));
        State.DetailSelection.Open(State.Catalog.Rows.Select(r => r.Id),id);
        State.ShowDetail(id); Rebuild();
    }
}
