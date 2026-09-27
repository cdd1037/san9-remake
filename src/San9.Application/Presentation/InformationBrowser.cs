namespace San9.Application.Presentation;

public enum InformationCategory { Ranks, Forces, Corps, Cities, Regions, Units, Officers, Treasures, Records, Chronicle, Offices, Tactics, Formations, Ships }
public readonly record struct InformationKey(InformationCategory Category, int Id);
public sealed record InformationRow(InformationKey Key, CatalogCell[] Cells, int? SettlementId = null);
public sealed record InformationTable(string Title, string[] Pages, string[] Columns, InformationRow[] Rows);
public sealed record InformationField(string Label, string Value);
public sealed record InformationLink(string Label, InformationCategory Category, int? ForceId = null, int? SettlementId = null, int? HolderId = null, int? CorpsId = null, int? RegionId = null, int? OfficerId = null, int? UnitId = null);
public sealed record InformationDetail(string Title, InformationField[] Fields, InformationLink[] Links, int? SettlementId)
{
    public string? ImageRole { get; init; }
}

/// <summary>One navigation entry owns its route and all transient page state.</summary>
public sealed class InformationPageContext(InformationLink route)
{
    public InformationLink Route { get; } = route;
    public CatalogTableState Catalog { get; } = new();
    public DetailSelectionState DetailSelection { get; } = new();
    public OfficerDetailState OfficerDetail { get; } = new();
    public bool IsDetail { get; internal set; }
    public bool IsMap { get; internal set; }
}

/// <summary>Modal navigation state; no map selection or world mutations.</summary>
public sealed class InformationBrowser
{
    private readonly Stack<InformationPageContext> _history = [];
    private bool _directDetailRoot;
    public InformationPageContext Current { get; private set; } = new(new("", InformationCategory.Ranks));
    public bool IsOpen { get; private set; }
    public bool IsMenu { get; private set; }
    public bool IsDetail => Current.IsDetail;
    public bool IsMap => Current.IsMap;
    public InformationCategory Category => Current.Route.Category;
    public int Page => Catalog.Page;
    public int Offset => Catalog.Offset;
    public int SortColumn => Catalog.SortColumn;
    public bool Descending => Catalog.Descending;
    public int? SelectedId => Catalog.SelectedId;
    public int? ForceFilter => Current.Route.ForceId;
    public int? SettlementFilter => Current.Route.SettlementId;
    public int? HolderFilter => Current.Route.HolderId;
    public int? CorpsFilter => Current.Route.CorpsId;
    public int? RegionFilter => Current.Route.RegionId;
    public int? OfficerFilter => Current.Route.OfficerId;
    public int? UnitFilter => Current.Route.UnitId;
    public CatalogTableState Catalog => Current.Catalog;
    public DetailSelectionState DetailSelection => Current.DetailSelection;
    public OfficerDetailState OfficerDetail => Current.OfficerDetail;
    public const int PageSize = CatalogTableState.VisibleRows;
    public void ShowMap(bool value)
    {
        if (!IsOpen || IsMenu || IsDetail || Category is not (InformationCategory.Forces or InformationCategory.Cities))
            throw new InvalidOperationException("Map mode requires a force or city catalog.");
        Current.IsMap = value;
    }
    public void OpenMenu() { Close(); IsOpen = IsMenu = true; }
    public void Open(InformationCategory category, int? force = null, int? settlement = null, int? holder = null, int? corps = null, int? region = null, int? officer = null, int? unit = null)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        IsOpen = true; IsMenu = false;
        Current = new(new("", category, force, settlement, holder, corps, region, officer, unit));
    }
    public void OpenContext(MapContextEntry entry)
    {
        Close();
        var link = entry.Target;
        Open(link.Category, link.ForceId, link.SettlementId, link.HolderId, link.CorpsId, link.RegionId, link.OfficerId, link.UnitId);
        if (entry.DetailId is int id) { ShowDetail(id); _directDetailRoot = true; }
    }
    public void Navigate(InformationLink link)
    {
        _history.Push(Current);
        Open(link.Category, link.ForceId, link.SettlementId, link.HolderId, link.CorpsId, link.RegionId, link.OfficerId, link.UnitId);
    }
    public void Close()
    {
        IsOpen = IsMenu = false; Current.IsDetail = Current.IsMap = false;
        _history.Clear(); _directDetailRoot = false;
    }
    public void Back()
    {
        if (IsDetail) { if (_directDetailRoot && _history.Count == 0) Close(); else Current.IsDetail = false; return; }
        if (_history.TryPop(out var previous)) { Current = previous; return; }
        Close();
    }
    public void SelectPage(int page, int count) => Catalog.SelectPage(page, count);
    public void Sort(int column, int count) { Catalog.SelectSort(column, count); Catalog.Scroll(0); }
    public void Scroll(int offset, int count) => Catalog.Scroll(offset, count);
    public void Select(int id) => Catalog.SelectId(id);
    public void ShowDetail(int id) { Catalog.SelectId(id); Current.IsDetail = true; }
    public InformationRow[] Order(InformationTable table)
    {
        var positions = Catalog.Rows.Select((row, index) => (row.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        IEnumerable<InformationRow> rows = table.Rows.OrderBy(row => row.Key.Id)
            .OrderBy(row => positions.GetValueOrDefault(row.Key.Id, int.MaxValue));
        if (SortColumn >= 0 && SortColumn < table.Columns.Length)
        {
            var comparer = Comparer<CatalogCell>.Create((a, b) => a.Number is long x && b.Number is long y
                ? x.CompareTo(y) : StringComparer.Ordinal.Compare(a.Text, b.Text));
            rows = Descending ? rows.OrderByDescending(r => r.Cells[SortColumn], comparer) : rows.OrderBy(r => r.Cells[SortColumn], comparer);
        }
        var ordered = rows.ToArray();
        Catalog.BindOrderedRows(ordered.Select(row => new CatalogRow(row.Key.Id, row.Cells)).ToArray());
        return ordered;
    }
}
