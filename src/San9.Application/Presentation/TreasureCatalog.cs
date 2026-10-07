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
                   where treasure.Discovered && (holderId is null || treasure.HolderId == holderId)
                   join holder in view.Officers on treasure.HolderId equals holder.Id
                   where (force is null || holder.OwnerId == force) && (settlement is null || holder.SettlementId == settlement)
                   join definition in view.Information.Treasures on treasure.Id equals definition.Id
                   orderby treasure.Id
                   select new InformationRow(new(InformationCategory.Treasures, treasure.Id),
                   [texts.Resolve(definition.NameKey), definition.Category switch { 0 => "名马", 1 => "武器", 2 => "兵书", 3 => "政书", 4 => "史书", 5 => "经书", 6 => "论文",
                           7 => "地图", 8 => "医书", 9 => "奇书", 10 => "玉玺", 11 => "宝物", 12 => "装饰", _ => throw new InvalidOperationException("Unknown treasure category.") },
                       definition.Category switch { 2 => "统率", 1 => "武力", 5 or 9 => "智力", 3 or 4 or 6 => "政治", _ => "" },
                       definition.AbilityBonus, Tactic(definition.TacticId), definition.Category switch { 0 => new CatalogCell("安然撤退"), 10 => new CatalogCell("信望+100"), _ => CatalogCell.Unavailable }, texts.Resolve(holder.NameKey)], holder.SettlementId);
        return new("宝物一览", ["特征"], Pages[0].Columns.Select(c => c.Title).ToArray(), rows.ToArray());
    }

    private static CatalogCell Tactic(int? id) => id is null ? new("") :
        OfficerCatalog.Pages.Skip(4).SelectMany(p => p.Columns.Skip(1)).FirstOrDefault(c => c.Id == $"tactic.{id}") is { } column
            ? new(column.Title) : CatalogCell.Unavailable;
}
