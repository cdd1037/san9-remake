namespace San9.Application.Presentation;

/// <summary>Transactional multi-selection, independent of hover, sorting and paging.</summary>
public sealed class CatalogSelection(IEnumerable<int> candidates, IEnumerable<int> selected, int limit)
{
    private readonly HashSet<int> _candidates = candidates.ToHashSet();
    private readonly HashSet<int> _selected = selected.ToHashSet();
    public int Limit { get; } = limit;
    public IReadOnlySet<int> Selected => _selected;
    public bool CanToggle(int id) => _candidates.Contains(id) && (_selected.Contains(id) || _selected.Count < Limit);
    public bool Toggle(int id)
    {
        if (!CanToggle(id)) return false;
        if (_selected.Remove(id)) return true;
        return _selected.Add(id);
    }
    public void SelectAll(IEnumerable<int> order)
    {
        foreach (int id in order)
        { if (_selected.Count >= Limit) break; if (CanToggle(id)) _selected.Add(id); }
    }
    public void Clear() => _selected.Clear();
}
