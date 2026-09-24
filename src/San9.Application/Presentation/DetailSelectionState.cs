namespace San9.Application.Presentation;

public sealed class DetailSelectionState
{
    public const int VisibleRows = 25;
    public IReadOnlyList<int> Candidates { get; private set; } = Array.Empty<int>();
    public int CurrentId { get; private set; }
    public int? FocusedId { get; private set; }
    public int Offset { get; private set; }

    public void Open(IEnumerable<int> candidates, int current)
    {
        var ids = candidates.ToArray();
        if (ids.Distinct().Count() != ids.Length || !ids.Contains(current)) throw new ArgumentException("Invalid detail candidates.");
        Candidates = Array.AsReadOnly(ids); CurrentId = current; FocusedId = null; Offset = 0;
        Scroll(Array.IndexOf(ids, current));
    }
    public void Refresh(IEnumerable<int> candidates)
    {
        var available = candidates.ToHashSet();
        var ordered = Candidates.Where(available.Contains).Concat(available.Except(Candidates)).ToArray();
        Candidates = Array.AsReadOnly(ordered);
        if (!available.Contains(CurrentId) && ordered.Length > 0) CurrentId = ordered[0];
        if (FocusedId is int focused && !available.Contains(focused)) FocusedId = null;
        Offset = Math.Clamp(Offset, 0, Math.Max(0, ordered.Length - VisibleRows));
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
        int? row = FocusedId is int id ? Candidates.ToList().IndexOf(id) - Offset : null;
        Offset = Math.Clamp(offset, 0, Math.Max(0, Candidates.Count - VisibleRows));
        if (row is int slot) FocusedId = Candidates[Offset + slot];
    }
    public void MoveFocus(int delta)
    {
        if (FocusedId is not int focused) return;
        int index = Math.Clamp(Candidates.ToList().IndexOf(focused) + delta, 0, Candidates.Count - 1);
        if (index < Offset) Scroll(index);
        else if (index >= Offset + VisibleRows) Scroll(index - VisibleRows + 1);
        PointAt(Candidates[index]);
    }
}
