using San9.Application.Presentation;

namespace San9.Client.Godot;

public static class OpeningForceCatalog
{
    public static IReadOnlyList<CatalogPage> Pages { get; } = new[] { new CatalogPage("选择",
        [new("name", "势力", 8, CatalogAlignment.Left), N("trust", "信望", 4), N("cities", "都市", 3),
            N("officers", "武将", 3), N("money", "资金", 7), N("food", "兵粮", 7), N("population", "人口", 8),
            N("soldiers", "士兵", 8), new("tactics", "兵法", 4, CatalogAlignment.Center)]) }
        .Concat(AdministrativeCatalog.Pages(InformationCategory.Forces)).ToArray();
    private static CatalogColumn N(string id, string label, int width) => new(id, label, width, CatalogAlignment.Right);
    public static CatalogCell Cell(OpeningForce f, string field)
    {
        if (f.IsBarbarian && (field == "trust" || field.StartsWith("money", StringComparison.Ordinal) || field.StartsWith("food", StringComparison.Ordinal)))
            return new(field == "trust" ? "****" : "*******", IsUnavailable: true);
        if (field.StartsWith("money", StringComparison.Ordinal) || field.StartsWith("food", StringComparison.Ordinal))
        {
            var resource = field.StartsWith("money", StringComparison.Ordinal) ? f.Outlook.Money : f.Outlook.Food;
            return field.Contains('-') ? field[(field.IndexOf('-') + 1)..] switch
            {
                "cost" => resource.Expenditure, "income" => resource.Income, "forecast" => resource.Forecast,
                "balance" => resource.Income - resource.Expenditure,
                "sufficient" => resource.Sufficient ? "○" : "×", _ => throw new ArgumentOutOfRangeException(nameof(field))
            } : resource.Balance;
        }
        return field switch
        {
            "name" => f.Name, "rank" => f.Rank, "country" => f.Country, "trust" => f.Trust, "adviser" => f.Adviser,
            "corps" => f.Corps, "cities" => f.CityIds.Length, "officers" => f.Officers, "population" => f.Population,
            "draft" => f.DraftPopulation, "soldiers" => f.Soldiers, "wounded" => f.Wounded, "tactics" => f.Tactics,
            "alliance" => f.AllianceDays <= 0 ? CatalogCell.Unavailable : f.IsAllianceTarget ? "×" : f.IsAllianceLeader ? "◎" : "○",
            "alliance-days" => f.AllianceDays > 0 ? f.AllianceDays : CatalogCell.Unavailable,
            // There is no player observer before this draft is confirmed.
            "relation" => CatalogCell.Unavailable,
            "requester" or "request-target" or "request-days" => new("*******", IsUnavailable: true),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown opening force field.")
        };
    }
}
