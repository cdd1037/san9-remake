using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public enum OfficerDetailPage { Status, Tactics }
public sealed class OfficerDetailState
{
    public const int VisibleRows = 18;
    public int[] Candidates { get; private set; } = [];
    public int CurrentId { get; private set; }
    public int? FocusedId { get; private set; }
    public int Offset { get; private set; }
    public OfficerDetailPage Page { get; private set; }
    public void Open(IEnumerable<int> candidates, int current)
    {
        var ids = candidates.ToArray();
        if (ids.Distinct().Count() != ids.Length || !ids.Contains(current)) throw new ArgumentException("Invalid detail candidates.");
        Candidates = ids; CurrentId = current; FocusedId = null; Page = OfficerDetailPage.Status;
        Scroll(Array.IndexOf(ids, current));
    }
    public void Refresh(IEnumerable<int> candidates)
    {
        var available = candidates.ToHashSet();
        Candidates = Candidates.Where(available.Contains).Concat(available.Except(Candidates)).ToArray();
        if (!available.Contains(CurrentId) && Candidates.Length > 0) CurrentId = Candidates[0];
        if (FocusedId is int focused && !available.Contains(focused)) FocusedId = null;
        Scroll(Offset);
    }
    public void PointAt(int id)
    {
        if (!Candidates.Contains(id)) throw new ArgumentOutOfRangeException(nameof(id));
        FocusedId = id;
    }
    public void ClearFocus() => FocusedId = null;
    public void Activate(int id) { PointAt(id); CurrentId = id; }
    public void SelectPage(OfficerDetailPage page)
    {
        if (!Enum.IsDefined(page)) throw new ArgumentOutOfRangeException(nameof(page));
        Page = page;
    }
    public void Scroll(int offset) => Offset = Math.Clamp(offset, 0, Math.Max(0, Candidates.Length - VisibleRows));
    public void MoveFocus(int delta)
    {
        int index = Math.Clamp(Array.IndexOf(Candidates, FocusedId ?? CurrentId) + delta, 0, Candidates.Length - 1);
        PointAt(Candidates[index]);
        if (index < Offset) Scroll(index);
        else if (index >= Offset + VisibleRows) Scroll(index - VisibleRows + 1);
    }
}

public sealed record OfficerDetailData(string Name, string Biography, string PortraitRole, OfficerAbilities RadarAbilities,
    string[] AbilityValues, InformationField[][] Fields, OfficerTacticGroup[] Tactics, int[] Treasures)
{ public bool IsDeceased { get; init; } }
public sealed record OfficerTacticGroup(string Name, int Proficiency, OfficerTactic[] Tactics);
public sealed record OfficerTactic(string Name, bool Available);

public static class OfficerDetails
{
    private static readonly string[][] FieldIds =
    [
        ["force", "corps", "home", "location", "service", "status"],
        ["office", "capacity", "salary", "loyalty"],
        ["gender", "personality", "parent", "age", "health"],
        ["order", "target-person", "target-place", "arrival", "formation", "equipped-tactic"]
    ];
    public static OfficerDetailData Read(StrategySnapshot view, TextCatalog texts, int id)
    {
        var officer = view.Officers.Single(o => o.Id == id);
        var person = view.World.Officers.Single(o => o.Id == id);
        var values = new Dictionary<string, InformationField>();
        var tactics = new List<OfficerTacticGroup>();
        for (int page = 0; page < OfficerCatalog.Pages.Count; page++)
        {
            var definition = OfficerCatalog.Pages[page];
            var row = OfficerCatalog.Read(view, texts, page, null, null, id).Rows.Single(r => r.Key.Id == id);
            for (int c = 0; c < definition.Columns.Length; c++)
                values[definition.Columns[c].Id] = new(definition.Columns[c].Title, row.Cells[c].Text);
            if (page >= 4)
            {
                var cells = definition.Columns.Select((column, index) => (Column: column, Cell: row.Cells[index])).ToArray();
                tactics.Add(new(definition.Title,
                    (int)cells.Single(c => c.Column.Id.StartsWith("proficiency.", StringComparison.Ordinal)).Cell.Number!.Value,
                    cells.Where(c => c.Column.Id.StartsWith("tactic.", StringComparison.Ordinal))
                        .Select(c => new OfficerTactic(c.Column.Title, c.Cell.Number > 0)).ToArray()));
            }
        }
        string name = texts.Resolve(officer.NameKey);
        if (officer.CourtesyNameKey is string courtesy) name += $"〈{texts.Resolve(courtesy)}〉";
        int[] Base(OfficerAbilities a) => [a.Leadership, a.War, a.Intelligence, a.Politics];
        var current = Base(officer.Abilities);
        var abilityValues = Base(person.BaseAbilities).Select((value, i) => current[i] == value ? $"{value}" : $"{value}{current[i] - value:+0;-0}").ToArray();
        return new(name, officer.BiographyKey is string bio ? texts.ResolveRich(bio) : "", $"information-person-{id}",
            officer.Abilities, abilityValues, FieldIds.Select(column => column.Select(key => values[key]).ToArray()).ToArray(),
            tactics.ToArray(), view.World.Treasures.Where(t => t.Discovered && t.HolderId == id).Select(t => t.Id).ToArray()) { IsDeceased = person.Status == OfficerStatus.Dead };
    }
}
