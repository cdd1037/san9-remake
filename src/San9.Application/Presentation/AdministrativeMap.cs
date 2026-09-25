using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record InformationMapMarker(int CityId, int X, int Y, int? OwnerId, int? TargetId);

public static class AdministrativeMap
{
    public static InformationMapMarker[] Markers(StrategySnapshot view, InformationCategory category, IEnumerable<int> candidates)
    {
        if (category is not (InformationCategory.Forces or InformationCategory.Cities)) throw new ArgumentOutOfRangeException(nameof(category));
        var available = candidates.ToHashSet();
        return view.Settlements.Where(s => s.Kind == SettlementKind.City).Select(s => {
            int? target = category == InformationCategory.Forces ? s.OwnerId : s.Id;
            return new InformationMapMarker(s.Id,s.X,s.Y,s.OwnerId,target is int id && available.Contains(id) ? id : null);
        }).ToArray();
    }
    public static InformationField[] Summary(StrategySnapshot view, TextCatalog texts, InformationKey key)
    {
        var catalog = new AdministrativeCatalog(view,texts);
        (string Id,string Label)[] columns = key.Category switch {
            InformationCategory.Forces => [("name","势力"),("rank","官爵"),("trust","信望"),("tactics","兵法"),
                ("relation","友好"),("adviser","军师"),("cities","都市"),("officers","在任"),
                ("population","人口"),("draft","兵役人口"),("soldiers","士兵"),("wounded","伤兵"),
                ("money","资金"),("food","兵粮"),("money-balance","资金收支"),("food-balance","兵粮收支")],
            InformationCategory.Cities => [("name","都市"),("durability","耐久"),("trait","特性"),("province","州"),
                ("public-order","民心"),("commerce","收益"),("cultivation","收获"),("merchant","商人"),
                ("commander","大将"),("serving","在任"),("captives","俘虏"),("free","在野"),
                ("population","人口"),("draft","兵役人口"),("soldiers","士兵"),("wounded","伤兵")],
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        string Value(string id)
        {
            if (!id.EndsWith("-balance",StringComparison.Ordinal)) return catalog.Cell(key.Category,key.Id,id).Text;
            string resource=id[..id.IndexOf('-')];
            var income=catalog.Cell(key.Category,key.Id,resource+"-income");
            var cost=catalog.Cell(key.Category,key.Id,resource+"-cost");
            return income.Number is long i && cost.Number is long c ? (i-c).ToString(System.Globalization.CultureInfo.InvariantCulture) : income.Text;
        }
        return columns.Select(c => new InformationField(c.Label,Value(c.Id))).ToArray();
    }
}
