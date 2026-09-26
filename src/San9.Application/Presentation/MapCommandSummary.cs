using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record MapCommandSummaryRow(string Label, string Value, int Y, bool Numeric = true);
public sealed record MapCommandSummary(string Kind, string Name, string Status, IReadOnlyList<MapCommandSummaryRow> Rows)
{
    public string? Icon { get; init; }

    public static MapCommandSummary Settlement(StrategySnapshot view, TextCatalog texts, int id)
    {
        var place = view.World.Settlements.Single(s => s.Id == id);
        var visible = view.Settlements.Single(s => s.Id == id);
        bool city = place.Kind == SettlementKind.City;
        var catalog = new AdministrativeCatalog(view, texts);
        string Cell(string field) => catalog.Cell(InformationCategory.Cities, id, field).Text;
        var staffing = SettlementStaffing.Read(view, id);
        var people = staffing.Present;
        List<MapCommandSummaryRow> rows = [];
        int y = 186;
        void Row(string title, string value, bool numeric = true) { rows.Add(new(title, value, y, numeric)); y += 28; }
        if (city)
        {
            Row("民心", $"{place.PublicOrder,4}/{1000,4}"); Row("收益", $"{place.Commerce,4}/{place.MaxCommerce,4}");
            Row("收获", $"{place.Cultivation,4}/{place.MaxCultivation,4}"); y += 10;
        }
        Row("耐久", $"{place.Durability,4}/{place.MaxDurability,4}"); Row("士气", place.Soldiers > 0 ? $"{place.Morale,3}/{100,3}" : "----"); y += 10;
        if (city) { Row("人口", place.Population.ToString()); Row("兵役人口", place.DraftPopulation.ToString()); }
        Row("士兵", place.Soldiers.ToString()); Row("伤兵", place.Wounded.ToString()); y += 10;
        if (city)
        {
            Row("增加兵役", Cell("draft-growth")); Row("资金收入", Cell("money-income")); Row("兵粮收入", Cell("food-income")); y += 10;
            Row("商人", Cell("merchant"), false); Row("市价", $"兵粮{view.World.Simulation.FoodExchangeRate}/资金1", false); y += 10;
        }
        Row("在任", $"{staffing.Available,3}/{staffing.Assigned,3}");
        Row("俘虏", people.Count(o => o.Status == OfficerStatus.Captive).ToString());
        Row("在野", people.Count(o => o.Status == OfficerStatus.Free).ToString());
        return new(city ? "city" : "facility", visible.ResolveName(texts), place.ConfusionDaysRemaining > 0 ? "混乱" : "一般", rows) {
            Icon = city ? place.MerchantPresent ? "merchant" : visible.Icons.Contains("training") ? "training" : null : null
        };
    }

    public static MapCommandSummary Unit(StrategySnapshot view, TextCatalog texts, int id)
    {
        var unit = view.World.Units.Single(u => u.Id == id);
        var detail = UnitDetails.Read(view, view.MilitaryRules, texts, id);
        return new("unit", detail.Name, detail.Condition, [
            new("士兵", unit.Soldiers.ToString(), 186), new("伤兵", detail.Wounded, 214), new("士气", $"{unit.Morale,3}/{100,3}", 242),
            new("阵形", detail.Formation, 280, false), new("船", detail.Ship, 308, false),
            .. detail.Orders.Select((field, index) => new MapCommandSummaryRow(field.Label, field.Value, 346 + index * 28, false))]);
    }
}
