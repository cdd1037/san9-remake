namespace San9.Application.Presentation;

public enum InformationCategory { Ranks, Forces, Corps, Cities, Regions, Units, Officers, Treasures, Records, Chronicle }
public readonly record struct InformationKey(InformationCategory Category, int Id);
public sealed record InformationRow(InformationKey Key, CatalogCell[] Cells, int? SettlementId = null);
public sealed record InformationTable(string Title, string[] Pages, string[] Columns, InformationRow[] Rows);
public sealed record InformationField(string Label, string Value);
public sealed record InformationLink(string Label, InformationCategory Category, int? ForceId = null, int? SettlementId = null);
public sealed record InformationDetail(string Title, InformationField[] Fields, InformationLink[] Links, int? SettlementId)
{
    public string? ImageRole { get; init; }
}

/// <summary>Modal navigation state; no map selection or world mutations.</summary>
public sealed class InformationBrowser
{
    private readonly Stack<(InformationCategory Category, int Page, int Offset, int Sort, bool Descending, int? Selected, bool Detail, int? Force, int? Settlement)> _history = [];
    public bool IsOpen { get; private set; }
    public bool IsMenu { get; private set; }
    public bool IsDetail { get; private set; }
    public InformationCategory Category { get; private set; }
    public int Page { get; private set; }
    public int Offset { get; private set; }
    public int SortColumn { get; private set; } = -1;
    public bool Descending { get; private set; }
    public int? SelectedId { get; private set; }
    public int? ForceFilter { get; private set; }
    public int? SettlementFilter { get; private set; }
    public const int PageSize = 15;
    public void OpenMenu() { Close(); IsOpen = IsMenu = true; }
    public void Open(InformationCategory category, int? force = null, int? settlement = null)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        IsOpen = true; IsMenu = IsDetail = false; Category = category; Page = Offset = 0;
        SortColumn = -1; Descending = false; SelectedId = null; ForceFilter = force; SettlementFilter = settlement;
    }
    public void Navigate(InformationLink link)
    {
        _history.Push((Category, Page, Offset, SortColumn, Descending, SelectedId, IsDetail, ForceFilter, SettlementFilter));
        Open(link.Category, link.ForceId, link.SettlementId);
    }
    public void Close() { IsOpen = IsMenu = IsDetail = false; _history.Clear(); }
    public void Back()
    {
        if (IsDetail) { IsDetail = false; return; }
        if (_history.TryPop(out var previous))
        {
            (Category, Page, Offset, SortColumn, Descending, SelectedId, IsDetail, ForceFilter, SettlementFilter) = previous;
            return;
        }
        Close();
    }
    public void SelectPage(int page, int count)
    { if (page < 0 || page >= count) throw new ArgumentOutOfRangeException(nameof(page)); Page = page; Offset = 0; SortColumn = -1; }
    public void Sort(int column, int count)
    {
        if (column < 0 || column >= count) throw new ArgumentOutOfRangeException(nameof(column));
        Descending = SortColumn != column || !Descending; SortColumn = column; Offset = 0;
    }
    public void Scroll(int offset, int count) => Offset = Math.Clamp(offset, 0, Math.Max(0, count - PageSize));
    public void Select(int id) => SelectedId = id;
    public void ShowDetail(int id) { SelectedId = id; IsDetail = true; }
    public InformationRow[] Order(InformationTable table)
    {
        IEnumerable<InformationRow> rows = table.Rows;
        if (SortColumn >= 0 && SortColumn < table.Columns.Length)
        {
            var comparer = Comparer<CatalogCell>.Create((a, b) => a.Number is long x && b.Number is long y
                ? x.CompareTo(y) : StringComparer.Ordinal.Compare(a.Text, b.Text));
            rows = Descending ? rows.OrderByDescending(r => r.Cells[SortColumn], comparer) : rows.OrderBy(r => r.Cells[SortColumn], comparer);
        }
        return rows.ThenByIdentity().ToArray();
    }
}

internal static class InformationOrdering
{
    public static IEnumerable<InformationRow> ThenByIdentity(this IEnumerable<InformationRow> rows) =>
        rows is IOrderedEnumerable<InformationRow> ordered ? ordered.ThenBy(r => r.Key.Id) : rows.OrderBy(r => r.Key.Id);
}
