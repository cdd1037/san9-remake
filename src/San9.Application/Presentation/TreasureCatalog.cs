using San9.Content.Text;

namespace San9.Application.Presentation;

public static class TreasureCatalog
{
    public static IReadOnlyList<CatalogPage> Pages { get; } = Array.AsReadOnly(new[]
    {
        new CatalogPage("特征", [new("name", "宝物", 12, CatalogAlignment.Left),
            new("category", "种类", 4, CatalogAlignment.Center), new("ability", "能力", 4, CatalogAlignment.Center),
            new("bonus", "上升", 4, CatalogAlignment.Right), new("tactic", "兵法", 4, CatalogAlignment.Center),
            new("effect", "效果", 8, CatalogAlignment.Center), new("holder", "所有", 6, CatalogAlignment.Center)])
    });

    public static InformationTable Read(StrategySnapshot view, TextCatalog texts, int? holderId = null, int? force = null, int? settlement = null)
    {
        var rows = from treasure in view.World.Treasures
                   where treasure.HolderId is not null && (holderId is null || treasure.HolderId == holderId)
                   join holder in view.Officers on treasure.HolderId equals holder.Id
                   where (force is null || holder.OwnerId == force) && (settlement is null || holder.SettlementId == settlement)
                   join definition in view.Information.Treasures on treasure.Id equals definition.Id
                   let bonuses = Bonuses(definition.AbilityBonuses)
                   orderby treasure.Id
                   select new InformationRow(new(InformationCategory.Treasures, treasure.Id),
                   [definition.Name, definition.Category switch { 0 => "名马", 1 => "武器", 2 => "兵书", 3 => "政书", 4 => "史书", 5 => "经书", 6 => "论文",
                           7 => "地图", 8 => "医书", 9 => "奇书", 10 => "玉玺", 11 => "宝物", 12 => "装饰", _ => throw new InvalidOperationException("Unknown treasure category.") },
                       bonuses.Ability, new CatalogCell(bonuses.Amount, definition.MaximumAbilityBonus), Tactic(view, texts, definition.TacticId),
                       Effects(view, definition),
                       texts.Resolve(holder.NameKey)], holder.SettlementId);
        return new("宝物一览", ["特征"], Pages[0].Columns.Select(c => c.Title).ToArray(), rows.ToArray());
    }

    private static (string Ability, string Amount) Bonuses(San9.Core.Domain.OfficerAbilities values)
    {
        var bonuses = new[] { (Name: "统率", Value: values.Leadership), (Name: "武力", Value: values.War), (Name: "智力", Value: values.Intelligence), (Name: "政治", Value: values.Politics) }
            .Where(value => value.Value > 0).ToArray();
        return (string.Join("／", bonuses.Select(value => value.Name)), string.Join("／", bonuses.Select(value => value.Value)));
    }

    private static CatalogCell Effects(StrategySnapshot view, San9.Content.Scenarios.TreasureDefinition definition)
    {
        var effects = new[] { (Enabled: definition.EscapeMount, Text: "安然撤退"),
            (Enabled: view.Rules.Economy.ImperialSealId == definition.Id, Text: "信望+100") }
            .Where(effect => effect.Enabled).Select(effect => effect.Text).ToArray();
        return effects.Length == 0 ? CatalogCell.Unavailable : new(string.Join("、", effects));
    }

    private static CatalogCell Tactic(StrategySnapshot view, TextCatalog texts, int? id) => id is null ? new("") :
        new(view.Information.Tactics.SingleOrDefault(t => t.Id == id)?.Name ?? $"兵法 [{id}]");
}
