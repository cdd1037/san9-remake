using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed class InformationQuery(GameSession session, TextCatalog texts)
{
    public static IReadOnlyList<InformationCategory> MenuCategories { get; } = Array.AsReadOnly(Enum.GetValues<InformationCategory>().Where(c => !ForceCapabilityCatalog.Supports(c)).ToArray());
    private StrategySnapshot View => session.View;
    private string Name(string key) => texts.Resolve(key);
    private string Force(int? id) => View.Forces.FirstOrDefault(f => f.Id == id) is { } f ? Name(f.NameKey) : "无";
    private string Person(int? id) => View.Officers.FirstOrDefault(o => o.Id == id) is { } o ? Name(o.NameKey) : "无";
    private string Place(int? id) => View.Settlements.FirstOrDefault(s => s.Id == id) is { } s ? s.ResolveName(texts)
        : id is int region && View.RegionNameKeys.TryGetValue(region, out string? key) ? Name(key) : "无";
    private string Rank(int? id) => View.Information.Ranks.FirstOrDefault(r => r.Id == id) is { } r ? Name(r.NameKey) : "无";
    private string Office(int? id) => View.Information.Offices.FirstOrDefault(o => o.Id == id) is { } o ? Name(o.NameKey) : "无";
    public static string Status(OfficerStatus status) => status switch
    { OfficerStatus.Ruler => "君主", OfficerStatus.Governor => "都督", OfficerStatus.Strategist => "军师", OfficerStatus.Serving => "一般", OfficerStatus.Captive => "俘虏", OfficerStatus.Free => "在野", _ => throw new ArgumentOutOfRangeException(nameof(status)) };
    public static string MenuTitle(InformationCategory category) => category switch
    {
        InformationCategory.Ranks => "全官爵", InformationCategory.Forces => "全势力", InformationCategory.Corps => "全军团",
        InformationCategory.Cities => "全都市", InformationCategory.Regions => "全地域", InformationCategory.Units => "全部队",
        InformationCategory.Officers => "全武将", InformationCategory.Treasures => "全宝物", InformationCategory.Records => "进行记录",
        InformationCategory.Chronicle => "年表", InformationCategory.Offices => "全爵位",
        InformationCategory.Tactics => "全兵法", InformationCategory.Formations => "全阵形", InformationCategory.Ships => "全船", _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
    public bool Available(InformationCategory category) => category switch
    {
        InformationCategory.Units => View.World.Units.Count > 0,
        InformationCategory.Treasures => TreasureCatalog.Read(View, texts).Rows.Length > 0,
        _ => true
    };
    public string MenuHintKey(InformationCategory category) => StrategyHintText.Key("information-" + category.ToString().ToLowerInvariant() +
        (!Available(category) ? "-empty" : ""));
    public InformationTable Read(InformationBrowser browser) => browser.Category == InformationCategory.Treasures
        ? TreasureCatalog.Read(View, texts, browser.HolderFilter, browser.ForceFilter, browser.SettlementFilter)
        : Read(browser.Category, browser.Page, browser.ForceFilter, browser.SettlementFilter, browser.CorpsFilter, browser.RegionFilter, browser.OfficerFilter);
    public InformationTable Read(InformationCategory category, int page = 0, int? force = null, int? settlement = null, int? corps = null, int? region = null, int? officer = null)
    {
        var v = View;
        if (ForceCapabilityCatalog.Supports(category)) return ForceCapabilityCatalog.Read(session, texts, category, force ?? throw new ArgumentException("A force catalog requires an owner."), page);
        if (AdministrativeCatalog.Supports(category)) return new AdministrativeCatalog(v, texts).Read(category, page, force, settlement, corps, region);
        InformationRow Row(int id, CatalogCell[] cells, int? location = null) => new(new(category, id), cells, location);
        bool Owner(int? id) => force is null || force == id;
        switch (category)
        {
            case InformationCategory.Units:
                return page == 1
                    ? new("部队一览", ["状态", "方针"], ["部队", "敌接近时", "追击", "事后命令", "自主撤退"],
                        v.World.Units.Where(u => Owner(u.OwnerId) && (corps is null || u.CorpsId == corps) && (region is null || v.RegionAt(u.Position) == region)).Select(u => Row(u.Id, [$"{Force(u.OwnerId)}部队",
                            u.Policies.AttackApproachingEnemies ? "迎击" : "继续任务", u.Policies.Pursue ? "许可" : "禁止",
                            u.Policies.AttackAfterAction ? "攻击" : "返城", u.Policies.AutonomousRetreat ? "许可" : "禁止"])).ToArray())
                    : new("部队一览", ["状态", "方针"], ["部队", "势力", "士兵", "位置"], v.World.Units.Where(u => Owner(u.OwnerId) && (corps is null || u.CorpsId == corps) && (region is null || v.RegionAt(u.Position) == region)).Select(u => Row(u.Id, [$"{Force(u.OwnerId)}部队", Force(u.OwnerId), (long)u.Soldiers, $"{u.Cell.X}, {u.Cell.Y}"])).ToArray());
            case InformationCategory.Officers:
                return OfficerCatalog.Read(v, texts, page, force, settlement, officer, corps, region);
            case InformationCategory.Treasures:
                return TreasureCatalog.Read(v, texts, force: force, settlement: settlement);
            case InformationCategory.Records:
                return new("进行记录", ["记录"], ["日期", "内容"], session.Records.Select((r, i) => Row(i,
                    [$"{r.Clock.Year}年{r.Clock.Month}月{(int)r.Clock.Xun}旬", $"{Person(r.OfficerId)}在{Place(r.SettlementId)}执行{(r.Activity == DomesticActivity.Commerce ? "商业" : "开垦")}，增加{r.Increment}，支出{r.Cost}。"], r.SettlementId)).ToArray());
            case InformationCategory.Chronicle:
            {
                var initial = session.InitialView;
                var ruler = initial.Forces.Single(f => f.Id == initial.PlayerForceId);
                var rows = new List<InformationRow>
                {
                    Row(0, [$"{session.InitialDate}", $"{Rank(ruler.RankId)} {Name(ruler.NameKey)}统治{Place(ruler.LeaderSettlementId)}周边。"], ruler.LeaderSettlementId)
                };
                var strategist = initial.World.Officers.FirstOrDefault(o => o.OwnerId == ruler.Id && o.Status == OfficerStatus.Strategist);
                if (strategist is not null) rows.Add(Row(1, [session.InitialDate, $"{Person(strategist.Id)}辅佐{Name(ruler.NameKey)}担任军师。"], strategist.SettlementId));
                rows.AddRange(session.Chronicle.Select((e, i) => Row(i + 2, [$"{e.Year}年{e.Month}月{e.Day}日", e.Text])));
                return new($"{Name(ruler.NameKey)}军年表", ["年表"], ["日期", "内容"], rows.ToArray());
            }
            default: throw new ArgumentOutOfRangeException(nameof(category));
        }
    }

    public InformationDetail Detail(InformationKey key)
    {
        if (AdministrativeCatalog.Supports(key.Category))
        {
            var detail = AdministrativeDetails.Read(View, texts, key);
            return new(detail.Name + "情报", detail.Sections.SelectMany(s => s.Fields).Select(f => new InformationField(f.Label,f.Value)).ToArray(), detail.Links, detail.MapSettlementId) { ImageRole = detail.PortraitRole };
        }
        var table = Read(key.Category);
        var row = table.Rows.Single(r => r.Key == key);
        List<InformationField> fields = [];
        for (int page = 0; page < (key.Category == InformationCategory.Officers ? 1 : table.Pages.Length); page++)
        {
            var pageTable = Read(key.Category, page); var pageRow = pageTable.Rows.Single(r => r.Key == key);
            for (int c = 1; c < pageTable.Columns.Length; c++)
            {
                string label = pageTable.Columns[c];
                if (label == "最大") label = pageTable.Columns[c - 1] + "上限";
                if (!fields.Any(f => f.Label == label)) fields.Add(new(label, pageRow.Cells[c].Text));
            }
        }
        List<InformationLink> links = [];
        int? force = key.Category switch
        {
            InformationCategory.Forces => key.Id,
            InformationCategory.Corps => View.World.Corps.Single(c => c.Id == key.Id).OwnerId,
            InformationCategory.Cities => View.Settlements.Single(s => s.Id == key.Id).OwnerId,
            InformationCategory.Officers => View.Officers.Single(o => o.Id == key.Id).OwnerId,
            _ => null
        };
        if (force is not null)
        {
            if (key.Category != InformationCategory.Forces) links.Add(new("势力", InformationCategory.Forces, force));
            if (key.Category != InformationCategory.Corps) links.Add(new("军团", InformationCategory.Corps, force));
            if (key.Category != InformationCategory.Cities) links.Add(new("都市", InformationCategory.Cities, force));
        }
        if (key.Category != InformationCategory.Officers && key.Category != InformationCategory.Ranks)
            links.Add(new("武将", InformationCategory.Officers, force, key.Category is InformationCategory.Cities or InformationCategory.Regions ? row.SettlementId : null));
        if (key.Category == InformationCategory.Cities) links.Add(new("地域", InformationCategory.Regions, null, key.Id));
        return new(row.Cells[0].Text + "情报", fields.ToArray(), links.ToArray(), row.SettlementId)
        {
            ImageRole = key.Category switch
            {
                InformationCategory.Officers => $"information-person-{key.Id}",
                InformationCategory.Treasures => $"information-treasure-{key.Id}",
                InformationCategory.Forces when View.Forces.Single(f => f.Id == key.Id).LeaderId is int leader => $"information-person-{leader}",
                InformationCategory.Corps => $"information-person-{View.World.Corps.Single(c => c.Id == key.Id).LeaderId}",
                _ => null
            }
        };
    }
}
