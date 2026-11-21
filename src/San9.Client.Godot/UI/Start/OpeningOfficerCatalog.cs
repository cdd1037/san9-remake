using San9.Application.Presentation;
using San9.Content.Authoring;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>The opening roster has its own summary followed by the nine shared tactics pages.</summary>
public static class OpeningOfficerCatalog
{
    public static IReadOnlyList<CatalogPage> Pages(ScenarioInitialState source, TextCatalog texts) => new[]
    {
        new CatalogPage("君主", OfficerCatalog.CorePages[0].Columns.Take(6).ToArray())
    }.Concat(OfficerCatalog.Pages(source.Information.Tactics, source.Military?.Tactics ?? [], texts).Skip(4)).ToArray();

    public static CatalogCell Cell(OfficerDefaults person, string field)
    {
        if (field.StartsWith("mastery.", StringComparison.Ordinal))
        {
            bool mastered = person.MasteredCategories.Contains(int.Parse(field.AsSpan(8)));
            return new(mastered ? "☆" : "", mastered ? 1 : 0);
        }
        if (field.StartsWith("proficiency.", StringComparison.Ordinal)) return person.TacticProficiencies[int.Parse(field.AsSpan(12))];
        if (field.StartsWith("tactic.", StringComparison.Ordinal))
        {
            bool learned = person.LearnedTactics.Contains(int.Parse(field.AsSpan(7)));
            return new(learned ? "○" : "", learned ? 1 : 0);
        }
        return field switch
        {
            "name" => person.Name, "leadership" => person.Leadership, "war" => person.War,
            "intelligence" => person.Intelligence, "politics" => person.Politics,
            "learned" => person.LearnedTactics.Count, _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }
}
