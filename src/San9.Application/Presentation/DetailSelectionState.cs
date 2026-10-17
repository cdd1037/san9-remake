namespace San9.Application.Presentation;

public sealed class DetailSelectionState
{
    private System.Collections.ObjectModel.ReadOnlyCollection<int> _candidates = Array.AsReadOnly(Array.Empty<int>());
    public const int VisibleRows = 25;
    public int VisibleRowCount { get; private set; } = VisibleRows;
    public void SetVisibleRows(int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        VisibleRowCount = count;
        Offset = Math.Clamp(Offset, 0, Math.Max(0, Candidates.Count - count));
        ClearHiddenFocus();
    }
    public IReadOnlyList<int> Candidates => _candidates;
    public int CurrentId { get; private set; }
    public int? FocusedId { get; private set; }
    public int Offset { get; private set; }

    public void Open(IEnumerable<int> candidates, int current)
    {
        var ids = candidates.ToArray();
        if (ids.Distinct().Count() != ids.Length || !ids.Contains(current)) throw new ArgumentException("Invalid detail candidates.");
        _candidates = Array.AsReadOnly(ids); CurrentId = current; FocusedId = null; Offset = 0;
        Scroll(Array.IndexOf(ids, current));
    }
    public void Refresh(IEnumerable<int> candidates)
    {
        var available = candidates.ToHashSet();
        var ordered = Candidates.Where(available.Contains).Concat(available.Except(Candidates)).ToArray();
        _candidates = Array.AsReadOnly(ordered);
        if (!available.Contains(CurrentId) && ordered.Length > 0) CurrentId = ordered[0];
        if (FocusedId is int focused && !available.Contains(focused)) FocusedId = null;
        Offset = Math.Clamp(Offset, 0, Math.Max(0, ordered.Length - VisibleRowCount));
        ClearHiddenFocus();
    }
    public void PointAt(int id)
    {
        if (!Candidates.Contains(id)) throw new ArgumentOutOfRangeException(nameof(id));
        FocusedId = id;
    }
    public void Activate(int id) { PointAt(id); CurrentId = id; }
    public void ClearFocus() => FocusedId = null;
    public void Scroll(int offset)
    {
        int? row = FocusedRow();
        Offset = Math.Clamp(offset, 0, Math.Max(0, Candidates.Count - VisibleRowCount));
        FocusedId = row is int slot && Offset + slot < Candidates.Count ? Candidates[Offset + slot] : null;
    }
    public void MoveFocus(int delta)
    {
        if (FocusedId is not int focused) return;
        int index = (int)Math.Clamp((long)_candidates.IndexOf(focused) + delta, 0, Candidates.Count - 1);
        if (index < Offset) Scroll(index);
        else if (index >= Offset + VisibleRowCount) Scroll(index - VisibleRowCount + 1);
        PointAt(Candidates[index]);
    }

    private int? FocusedRow()
    {
        if (FocusedId is not int id) return null;
        int row = _candidates.IndexOf(id) - Offset;
        return row >= 0 && row < VisibleRowCount ? row : null;
    }

    private void ClearHiddenFocus()
    {
        if (FocusedRow() is null) FocusedId = null;
    }
}
