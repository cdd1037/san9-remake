using San9.Content.Text;

namespace San9.Application.Presentation;

public sealed record TreasureDetailData(string Name, string? PortraitRole, string Holder, string Description,
    string Category, string Effect, string Ability, string Tactic, int? TacticGroup);

public static class TreasureDetails
{
    public static TreasureDetailData Read(StrategySnapshot view, TextCatalog texts, int id)
    {
        var row = TreasureCatalog.Read(view, texts).Rows.Single(r => r.Key.Id == id);
        var cells = row.Cells;
        var definition = view.Information.Treasures.Single(t => t.Id == id);
        var bonuses = definition.AbilityBonuses;
        string ability = string.Join("、", new[] { (Name: "统率", Value: bonuses.Leadership), (Name: "武力", Value: bonuses.War),
            (Name: "智力", Value: bonuses.Intelligence), (Name: "政治", Value: bonuses.Politics) }
            .Where(value => value.Value > 0).Select(value => $"{value.Name}+{value.Value}"));
        int? tacticId = definition.TacticId;
        int? group = tacticId is int tactic ? view.TacticCategory(tactic) : null;
        return new(cells[0].Text, definition.ImageRole, cells[6].Text,
            MessageMarkup.Escape(definition.Description), cells[1].Text,
            cells[5].IsUnavailable ? "" : cells[5].Text, ability, cells[4].Text, group);
    }
}
