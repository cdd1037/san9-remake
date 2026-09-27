namespace San9.Application.Presentation;

public enum CatalogAlignment { Left, Right, Center }
public sealed record CatalogCell(string Text, long? Number = null, bool IsUnavailable = false)
{
    public static CatalogCell Unavailable => new("—", IsUnavailable: true);
    public static implicit operator CatalogCell(string text) => new(text);
    public static implicit operator CatalogCell(int value) => new(value.ToString(System.Globalization.CultureInfo.InvariantCulture), value);
    public static implicit operator CatalogCell(long value) => new(value.ToString(System.Globalization.CultureInfo.InvariantCulture), value);
}
public sealed record CatalogColumn(string Id, string Title, int PreferredHalfCells, CatalogAlignment Alignment)
{
    public int Width
    {
        get
        {
            int titleCells = Title.Sum(c => c < 128 ? 1 : 2);
            int cells = Math.Max(PreferredHalfCells, titleCells + 2);
            if (Alignment == CatalogAlignment.Center && (cells - PreferredHalfCells) % 2 != 0) cells++;
            return cells * 8 + 8;
        }
    }
}
public sealed record CatalogPage(string Title, CatalogColumn[] Columns);
public sealed record CatalogRow(int Id, CatalogCell[] Cells);

/// <summary>Owns only view state; caller supplies candidates and handles activation.</summary>
public sealed class CatalogTableState
{
    public const int VisibleRows = 15;
    public CatalogRow[] Rows { get; private set; } = [];
    public int Page { get; private set; }
    private int? _requestedId;
    public int? SelectedId => Current?.Id ?? _requestedId;
    public void SelectId(int id)
    {
        _requestedId = id;
        int index = Array.FindIndex(Rows, row => row.Id == id);
        CurrentIndex = index;
    }
    public void SelectPage(int page, int count)
    {
        if (page < 0 || page >= count) throw new ArgumentOutOfRangeException(nameof(page));
        if (Page == page) return;
        Page = page; SortColumn = -1; Descending = false;
    }
    internal void SelectSort(int column, int count)
    {
        if (column < 0 || column >= count) throw new ArgumentOutOfRangeException(nameof(column));
        Descending = SortColumn != column || !Descending; SortColumn = column;
    }
    internal void BindOrderedRows(CatalogRow[] rows)
    {
        int? id = SelectedId;
        Rows = rows; _requestedId = null;
        CurrentIndex = rows.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(rows, row => row.Id == id));
    }
    public int Offset { get; private set; }
    public int CurrentIndex { get; private set; } = -1;
    public int SortColumn { get; private set; } = -1;
    public bool Descending { get; private set; }
    public CatalogRow? Current => CurrentIndex >= 0 && CurrentIndex < Rows.Length ? Rows[CurrentIndex] : null;
    public void SetRows(CatalogRow[] rows, int? currentId = null, int offset = 0)
    {
        if (rows.Select(r => r.Id).Distinct().Count() != rows.Length) throw new ArgumentException("Duplicate catalog row identity.", nameof(rows));
        Rows = rows.ToArray(); _requestedId = null; SortColumn = -1; Descending = false;
        CurrentIndex = rows.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(rows, r => r.Id == currentId));
        Scroll(offset);
    }
    /// <summary>Reprojects cells without replacing the shared row order or viewport.</summary>
    public void RefreshRows(CatalogRow[] rows, bool columnsChanged = false)
    {
        var byId = rows.ToDictionary(row => row.Id);
        int? currentId = SelectedId;
        _requestedId = null;
        var retained = Rows.Where(row => byId.ContainsKey(row.Id)).Select(row => byId[row.Id]).ToArray();
        var retainedIds = retained.Select(row => row.Id).ToHashSet();
        Rows = retained.Concat(rows.Where(row => !retainedIds.Contains(row.Id))).ToArray();
        CurrentIndex = Rows.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(Rows, row => row.Id == currentId));
        Scroll(Offset);
        if (columnsChanged) { SortColumn = -1; Descending = false; }
    }
    public void Scroll(int offset) => Scroll(offset, Rows.Length);
    internal void Scroll(int offset, int count) => Offset = Math.Clamp(offset, 0, Math.Max(0, count - VisibleRows));
    public void PointAt(int index)
    { if ((uint)index < Rows.Length) CurrentIndex = index; }
    public void MoveTo(int index)
    {
        if (Rows.Length == 0) return;
        CurrentIndex = Math.Clamp(index, 0, Rows.Length - 1);
        if (CurrentIndex < Offset) Scroll(CurrentIndex);
        else if (CurrentIndex >= Offset + VisibleRows) Scroll(CurrentIndex - VisibleRows + 1);
    }
    public void Sort(int column)
    {
        if (column < 0 || Rows.Any(r => column >= r.Cells.Length)) throw new ArgumentOutOfRangeException(nameof(column));
        Descending = SortColumn != column || !Descending; SortColumn = column;
        var comparer = Comparer<CatalogCell>.Create((a, b) =>
        {
            if (a.IsUnavailable != b.IsUnavailable) return a.IsUnavailable ? 1 : -1;
            int result = a.Number is long x && b.Number is long y ? x.CompareTo(y)
                : System.Globalization.CultureInfo.GetCultureInfo("zh-CN").CompareInfo.Compare(a.Text, b.Text);
            return Descending ? -result : result;
        });
        Rows = Rows.OrderBy(r => r.Cells[column], comparer).ToArray();
        // Original catalog retains numeric view positions across sorting.
    }
}
