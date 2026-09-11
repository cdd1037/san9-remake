using San9.Content.Text;

namespace San9.Application.Presentation;

public sealed class TreasureDetailState
{
    public const int VisibleRows = 25;
    public IReadOnlyList<int> Candidates { get; private set; } = Array.Empty<int>();
    public int CurrentId { get; private set; }
    public int? FocusedId { get; private set; }
    public int Offset { get; private set; }

    public void Open(IEnumerable<int> candidates, int current)
    {
        var ids = candidates.ToArray();
        if (ids.Distinct().Count() != ids.Length || !ids.Contains(current)) throw new ArgumentException("Invalid treasure candidates.");
        Candidates = Array.AsReadOnly(ids); CurrentId = current; FocusedId = null; Offset = 0;
        Scroll(Array.IndexOf(ids, current));
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

public sealed record TreasureDetailData(string Name, string PortraitRole, string Holder, string Description,
    string Category, string Effect, string Ability, string Tactic, int? TacticGroup);

public static class TreasureDetails
{
    public static TreasureDetailData Read(StrategySnapshot view, TextCatalog texts, int id)
    {
        var row = TreasureCatalog.Read(view, texts).Rows.Single(r => r.Key.Id == id);
        var cells = row.Cells;
        string ability = cells[2].Text.Length == 0 ? "" : $"{cells[2].Text}+{cells[3].Text}";
        int? tacticId = view.Information.Treasures.Single(t => t.Id == id).TacticId;
        int? group = tacticId is null ? null : OfficerCatalog.Pages.Skip(4)
            .Select((page, index) => (page, index))
            .Single(p => p.page.Columns.Any(c => c.Id == $"tactic.{tacticId}")).index;
        return new(cells[0].Text, $"information-treasure-{id}", cells[6].Text,
            texts.ResolveRich(TreasureText.DescriptionKey(id)), cells[1].Text,
            cells[5].IsUnavailable ? "" : cells[5].Text, ability, cells[4].Text, group);
    }
}
