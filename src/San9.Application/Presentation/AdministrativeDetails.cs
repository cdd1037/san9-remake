using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed record InformationSection(string Title, InformationField[] Fields);
public sealed record AdministrativeDetailData(string Name, string? PortraitRole, InformationSection[] Sections,
    InformationLink[] Links, int? MapSettlementId);

public static class AdministrativeDetails
{
    public static AdministrativeDetailData Read(StrategySnapshot view, TextCatalog texts, InformationKey key)
    {
        if (key.Category == InformationCategory.Ranks || !AdministrativeCatalog.Supports(key.Category)) throw new ArgumentOutOfRangeException(nameof(key));
        var fields = new AdministrativeCatalog(view, texts);
        InformationSection Section(string title, params (string Id, string Label)[] columns) => new(title,
            columns.Select(c => new InformationField(c.Label, fields.Cell(key.Category, key.Id, c.Id).Text)).ToArray());
        InformationSection Economy(string title, string prefix) => Section(title, (prefix, title),
            (prefix + "-cost", "支出"), (prefix + "-income", "收入"), (prefix + "-forecast", "预测"));
        List<InformationLink> links = [];
        InformationSection[] sections;
        int? leader = null;
        if (key.Category is InformationCategory.Forces or InformationCategory.Corps)
        {
            var corps = key.Category == InformationCategory.Corps ? view.World.Corps.Single(c => c.Id == key.Id) : null;
            int owner = corps?.OwnerId ?? key.Id;
            leader = corps?.LeaderId ?? view.Forces.Single(f => f.Id == owner).LeaderId;
            sections = [
                corps is null ? Section("所属", ("country", "国号"), ("rank", "官爵"), ("trust", "信望"), ("tactics", "兵法"),
                    ("adviser", "军师"), ("corps", "军团"), ("cities", "都市"), ("officers", "武将"))
                    : Section("所属", ("force", "势力"), ("policy", "方针"), ("target", "目标"), ("cities", "都市"), ("officers", "武将")),
                Section("战力", ("population", "人口"), ("draft", "兵役人口"), ("soldiers", "士兵"), ("wounded", "伤兵")),
                Economy("资金", "money"), Economy("兵粮", "food"),
                .. corps is null ? new[] { Section("外交", ("relation", "友好"), ("alliance-leader", "联盟盟主"), ("alliance-target", "联盟标的"), ("alliance-days", "联盟期间"),
                    ("requester", "请求者"), ("request-target", "目标设施"), ("request-days", "请求期间")) } : Array.Empty<InformationSection>()];
            if (leader is int person) links.Add(new(corps is null ? "君主" : "都督", InformationCategory.Officers, OfficerId: person));
            if (corps is null) links.Add(new("军团", InformationCategory.Corps, owner));
            links.Add(new("都市", InformationCategory.Cities, owner, CorpsId: corps?.Id));
            links.Add(new("地域", InformationCategory.Regions, owner, CorpsId: corps?.Id));
            links.Add(new("部队", InformationCategory.Units, owner, CorpsId: corps?.Id));
            links.Add(new("武将", InformationCategory.Officers, owner, CorpsId: corps?.Id));
        }
        else
        {
            bool city = key.Category == InformationCategory.Cities;
            sections = [
                Section("所属", ("province", "州"), ("city", "都市"), ("force", "势力"), ("corps-name", "军团"), ("commander", "大将"), ("serving", "在任"), ("captives", "俘虏"), ("free", "在野")),
                Section("战力", ("soldiers", "士兵"), ("wounded", "伤兵"), ("morale", "士气"), ("state", "状态"), ("durability", "耐久"), ("max-durability", "最大耐久"), ("tactics", "兵法"), ("trait", "特性")),
                .. city ? new[] {
                    Section("内政", ("public-order", "民心"), ("commerce", "收益"), ("cultivation", "收获"), ("money-income", "资金收入"), ("food-income", "兵粮收入"), ("merchant", "商人"), ("emperor", "汉朝皇帝")),
                    Section("人口", ("population", "人口"), ("draft", "兵役人口"), ("draft-growth", "增加兵役")) }
                    : new[] { Section("设施", ("facility", "工作物"), ("recovery", "恢复")) },
                Section("邻接", ("frontline", "前线"), ("adjacent", "邻接"))];
            if (city) links.Add(new("地域", InformationCategory.Regions, SettlementId: key.Id));
            // Local identity is independent of any fallback used by the map locator.
            links.Add(new("部队", InformationCategory.Units, RegionId: key.Id));
            links.Add(new("武将", InformationCategory.Officers, RegionId: key.Id));
        }
        return new(fields.Cell(key.Category, key.Id, "name").Text, leader is int portrait ? $"information-person-{portrait}" : null,
            sections, links.ToArray(), fields.MapTarget(key.Category, key.Id));
    }
}
