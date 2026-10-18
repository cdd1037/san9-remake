using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public sealed class InformationQuery(GameSession session, TextCatalog texts, San9.Content.Interface.ChroniclePresentationDefinition? chronicle = null)
{
    public static IReadOnlyList<InformationCategory> MenuCategories { get; } = Array.AsReadOnly(Enum.GetValues<InformationCategory>().Where(c => !ForceCapabilityCatalog.Supports(c)).ToArray());
    private StrategySnapshot View => session.View;
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
        InformationCategory.Units => View.World.Units.Any(UnitCatalog.IsListed),
        InformationCategory.Treasures => TreasureCatalog.Read(View, texts).Rows.Length > 0,
        _ => true
    };
    public string MenuHintKey(InformationCategory category) => StrategyHintText.Key("information-" + category.ToString().ToLowerInvariant() +
        (!Available(category) ? "-empty" : ""));
    public InformationTable Read(InformationBrowser browser) => browser.Category == InformationCategory.Treasures
        ? TreasureCatalog.Read(View, texts, browser.HolderFilter, browser.ForceFilter, browser.SettlementFilter)
        : Read(browser.Category, browser.Page, browser.ForceFilter, browser.SettlementFilter, browser.CorpsFilter, browser.RegionFilter, browser.OfficerFilter, browser.UnitFilter);
    public InformationTable Read(InformationCategory category, int page = 0, int? force = null, int? settlement = null, int? corps = null, int? region = null, int? officer = null, int? unit = null)
    {
        var v = View;
        if (ForceCapabilityCatalog.Supports(category)) return ForceCapabilityCatalog.Read(session, texts, category, force ?? throw new ArgumentException("A force catalog requires an owner."), page);
        if (AdministrativeCatalog.Supports(category)) return new AdministrativeCatalog(v, texts).Read(category, page, force, settlement, corps, region);
        switch (category)
        {
            case InformationCategory.Units:
                return UnitCatalog.Read(v, session.MilitaryRules, texts, page, force, corps, region);
            case InformationCategory.Officers:
                return OfficerCatalog.Read(v, texts, page, force, settlement, officer, corps, region, unit);
            case InformationCategory.Treasures:
                return TreasureCatalog.Read(v, texts, force: force, settlement: settlement);
            default: throw new ArgumentOutOfRangeException(nameof(category));
        }
    }

    public ChroniclePresentation ReadChronicle() => ChroniclePresentation.Read(session, texts, chronicle ?? throw new InvalidOperationException("Missing chronicle presentation."));
    public string JournalHint(string role) => ChroniclePresentation.Hint(session, texts, chronicle ?? throw new InvalidOperationException("Missing chronicle presentation."), role);

    public UnitDetailData UnitDetail(int id) => UnitDetails.Read(View, session.MilitaryRules, texts, id);

    public InformationDetail Detail(InformationKey key, int? selectedPage = null)
    {
        if (AdministrativeCatalog.Supports(key.Category))
        {
            var detail = AdministrativeDetails.Read(View, texts, key);
            return new(detail.Name + "情报", detail.Sections.SelectMany(s => s.Fields).Select(f => new InformationField(f.Label,f.Value)).ToArray(), detail.Links, detail.MapSettlementId) { ImageRole = detail.PortraitRole };
        }
        var table = Read(key.Category);
        var row = table.Rows.Single(r => r.Key == key);
        List<InformationField> fields = [];
        for (int page = selectedPage ?? 0; page < (selectedPage is int selected ? selected + 1 : key.Category == InformationCategory.Officers ? 1 : table.Pages.Length); page++)
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
