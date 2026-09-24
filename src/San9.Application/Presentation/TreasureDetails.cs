using San9.Content.Text;

namespace San9.Application.Presentation;

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
