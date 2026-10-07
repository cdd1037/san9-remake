using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Application.Presentation;

public static class ConstructionFacilityCatalog
{
    public static IReadOnlyList<CatalogPage> Pages { get; } = Array.AsReadOnly<CatalogPage>([
        new("特征", [
            new("name", "工作物名", 10, CatalogAlignment.Left),
            new("direction", "方向", 4, CatalogAlignment.Center),
            new("durability", "最大耐久", 10, CatalogAlignment.Right),
            new("recovery", "恢复", 4, CatalogAlignment.Right),
            new("cost", "费用", 4, CatalogAlignment.Right),
            new("tactic", "兵法", 4, CatalogAlignment.Center),
            new("feature", "特性", 30, CatalogAlignment.Left)])]);

    public static CatalogRow[] Read(SpecialDeploymentDraft draft, TextCatalog texts) =>
        draft.AvailableFacilities().Select(f =>
        {
            string feature = f.InnateAttack is { UsesRockfallPower: true } ? "facility-rockfall" :
                f.Reprisal is not null ? "facility-stone" : f.InnateAttack is { RangeBonus: > 0 } ? "facility-range" : "";
            string tactics = string.Join("‧", (f.Tactics?.TacticPriority ?? []).Select(id =>
                texts.Resolve(draft.View.Information.Tactics.Single(t => t.Id == id).NameKey)));
            return new CatalogRow(f.Id, [draft.FacilityName(f.Id, texts),
                f.Footprint.Width == f.Footprint.Height ? "——" : f.Footprint.Width < f.Footprint.Height ? "南北" : "东西",
                f.MaxDurability, f.WoundedRecovery, f.ConstructionCost, tactics,
                feature.Length == 0 ? "" : texts.ResolveRich(SpecialDeploymentText.Key(feature))]);
        }).ToArray();
}
