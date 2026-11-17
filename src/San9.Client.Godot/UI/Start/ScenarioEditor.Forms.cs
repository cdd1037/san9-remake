using Godot;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class ScenarioEditor
{
    private void BuildOverview()
    {
        var scroll = new ScrollContainer { Name = "剧本", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _pages.AddChild(scroll); var body = ContentEditorControls.PageBody(scroll);
        var workspace = _workspace!;
        var heading = Grid(body, "剧本信息");
        ContentEditorControls.TextField(heading, "标题", workspace.Preview.Title,
            value => workspace.SetDescription(value, workspace.Preview.Description), "ScenarioTitle");
        ContentEditorControls.TextArea(body, "简介", workspace.Preview.Description,
            value => workspace.SetDescription(workspace.Preview.Title, value), "ScenarioDescription");
        var date = Grid(body, "开局日期");
        Number(date, "年份", workspace.Document.Year, 1, int.MaxValue, value => workspace.Update(workspace.Document with { Year = (int)value }), "ScenarioYear");
        Number(date, "月份", workspace.Document.Month, 1, 12, value => workspace.Update(workspace.Document with { Month = (int)value }), "ScenarioMonth");
        ContentEditorControls.Choices(date, "旬", ["上旬（1日）", "中旬（11日）", "下旬（21日）"], (workspace.Document.Day - 1) / 10,
            index => workspace.Update(workspace.Document with { Day = index * 10 + 1 }), "ScenarioXun");
        Reference(date, "默认玩家势力", workspace.Document.PlayerForceId, ForceChoices(),
            id => workspace.Update(workspace.Document with { PlayerForceId = id }));
        var settings = ContentEditorControls.ButtonGroup(body, "初始规则开关");
        void Toggle(string title, bool value, Func<ScenarioDocument, bool, ScenarioDocument> update) =>
            ContentEditorControls.Toggle(settings, title, value, enabled => workspace.Update(update(workspace.Document, enabled)));
        var document = workspace.Document;
        Toggle("每年登场", document.AnnualAppearancesEnabled, (d, value) => d with { AnnualAppearancesEnabled = value });
        Toggle("自然死亡", document.NaturalDeathsEnabled, (d, value) => d with { NaturalDeathsEnabled = value });
        Toggle("在野迁移", document.FreeOfficerMigrationEnabled, (d, value) => d with { FreeOfficerMigrationEnabled = value });
        Toggle("官职请求", document.OfficeRequestsEnabled, (d, value) => d with { OfficeRequestsEnabled = value });
        Toggle("部队叛变", document.UnitDefectionsEnabled, (d, value) => d with { UnitDefectionsEnabled = value });
        Toggle("据点叛变", document.SettlementDefectionsEnabled, (d, value) => d with { SettlementDefectionsEnabled = value });
        Toggle("都市独立", document.CityIndependenceEnabled, (d, value) => d with { CityIndependenceEnabled = value });
        Toggle("势力兴起", document.ForceEmergenceEnabled, (d, value) => d with { ForceEmergenceEnabled = value });
        Toggle("势力合并", document.ForceConsolidationEnabled, (d, value) => d with { ForceConsolidationEnabled = value });
    }

    private void BuildListPage(string name, Func<IEnumerable<(int Id, string Name)>> entries, Action<VBoxContainer, int> build)
    {
        var split = new HSplitContainer { Name = name, SplitOffsets = [245] }; _pages.AddChild(split);
        var side = ContentEditorControls.Sidebar(split);
        var search = new LineEdit { Name = name + "搜索", PlaceholderText = "搜索名称或 ID" }; side.AddChild(search);
        var list = new ItemList { Name = name + "列表", SizeFlagsVertical = SizeFlags.ExpandFill }; side.AddChild(list);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        split.AddChild(scroll); var form = ContentEditorControls.PageBody(scroll);
        var visible = new List<int>(); int? selected = null;
        void Show(int? id)
        {
            selected = id;
            foreach (Node child in form.GetChildren()) { form.RemoveChild(child); child.QueueFree(); }
            if (id is int value) build(form, value);
        }
        void Filter()
        {
            list.Clear(); visible.Clear();
            foreach (var entry in entries())
            {
                if (!entry.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) && !entry.Id.ToString().Contains(search.Text, StringComparison.Ordinal)) continue;
                visible.Add(entry.Id); list.AddItem($"{entry.Name}　[{entry.Id}]");
            }
            int index = selected is int id ? visible.IndexOf(id) : -1;
            if (index < 0 && visible.Count > 0) index = 0;
            if (index >= 0) list.Select(index);
            Show(index >= 0 ? visible[index] : null);
        }
        list.ItemSelected += index => Show(visible[(int)index]); search.TextChanged += _ => Filter(); Filter();
    }

    private void BuildForce(VBoxContainer body, int id)
    {
        var workspace = _workspace!;
        ForceInitialState Current() => workspace.Document.Forces.Single(f => f.Id == id);
        void Change(Func<ForceInitialState, ForceInitialState> update) => workspace.Update(workspace.Document with
        { Forces = workspace.Document.Forces.Select(f => f.Id == id ? update(f) : f).ToArray() });
        var force = Current(); var grid = Grid(body, workspace.Name(force.NameKey));
        Number(grid, "信望", force.Trust, 0, 1000, value => Change(f => f with { Trust = (int)value }));
        Number(grid, "基础信望", force.BaseTrust, 0, 1000, value => Change(f => f with { BaseTrust = (int)value }));
        Reference(grid, "君主", force.LeaderId, OfficerChoices(), value => Change(f => f with { LeaderId = value }));
        Reference(grid, "军师", force.AdviserId, OfficerChoices(), value => Change(f => f with { AdviserId = value }));
        Reference(grid, "爵位", force.RankId, workspace.Definitions.Information.Ranks.Select(r => (r.Id, r.Name)), value => Change(f => f with { RankId = value }));
        Reference(grid, "国号", force.CountryTitleId, workspace.Definitions.Information.CountryTitles.Select(c => (c.Id, workspace.Name(c.NameKey))),
            value => Change(f => f with { CountryTitleId = value }));
        ContentEditorControls.Toggle(body, "休眠势力", force.IsDormant, value => Change(f => f with { IsDormant = value }));
        foreach (var corps in workspace.Document.Information.Corps.Where(c => c.OwnerId == id))
        {
            var treasury = Grid(body, corps.IsRoot ? $"直属军团 [{corps.Id}]" : $"军团 [{corps.Id}]");
            void ChangeTreasury(Func<CorpsTreasury, CorpsTreasury> update) => workspace.Update(workspace.Document with
            { Information = workspace.Document.Information with { Corps = workspace.Document.Information.Corps.Select(c => c.Id == corps.Id ? c with { Treasury = update(c.Treasury) } : c).ToArray() } });
            Number(treasury, "资金", corps.Treasury.Money, 0, uint.MaxValue, value => ChangeTreasury(t => t with { Money = (uint)value }));
            Number(treasury, "兵粮", corps.Treasury.Food, 0, uint.MaxValue, value => ChangeTreasury(t => t with { Food = (uint)value }));
        }
    }

    private void BuildSettlement(VBoxContainer body, int id)
    {
        var workspace = _workspace!; var settlement = workspace.Document.Settlements.Single(s => s.Id == id);
        void Change(Func<SettlementInitialState, SettlementInitialState> update) => workspace.Update(workspace.Document with
        { Settlements = workspace.Document.Settlements.Select(s => s.Id == id ? update(s) : s).ToArray() });
        var grid = Grid(body, workspace.Name(settlement.NameKey));
        Reference(grid, "所属势力", settlement.OwnerId, ForceChoices(), value => Change(s => s with { OwnerId = value }));
        Reference(grid, "所属军团", settlement.CorpsId, workspace.Document.Information.Corps.Select(c => (c.Id, $"{c.Id} · {ForceName(c.OwnerId)}")), value => Change(s => s with { CorpsId = value }));
        Number(grid, "兵力", settlement.Soldiers, 0, settlement.SoldierCapacity, value => Change(s => s with { Soldiers = (uint)value }));
        Number(grid, "伤兵", settlement.Wounded, 0, MilitaryRulebook.WoundedCapacity, value => Change(s => s with { Wounded = (uint)value }));
        Number(grid, "士气", settlement.Morale, 0, 100, value => Change(s => s with { Morale = (int)value }));
        Number(grid, "耐久", settlement.Durability, 0, settlement.MaxDurability, value => Change(s => s with { Durability = (int)value }));
        Number(grid, "民心", settlement.PublicOrder, 0, 1000, value => Change(s => s with { PublicOrder = (int)value }));
        Number(grid, "商业", settlement.Commerce, 0, settlement.MaxCommerce, value => Change(s => s with { Commerce = (int)value }));
        Number(grid, "农业", settlement.Cultivation, 0, settlement.MaxCultivation, value => Change(s => s with { Cultivation = (int)value }));
        Number(grid, "人口", settlement.Population, 0, 1000000, value => Change(s => s with { Population = (uint)value }));
        Number(grid, "预备兵役", settlement.DraftPopulation, 0, uint.MaxValue, value => Change(s => s with { DraftPopulation = (uint)value }));
        ContentEditorControls.Toggle(body, "休眠据点", settlement.IsDormant, value => Change(s => s with { IsDormant = value }));
    }

    private void BuildOfficer(VBoxContainer body, int id)
    {
        var workspace = _workspace!; var officer = workspace.Document.Officers.Single(o => o.Id == id);
        void Change(Func<ScenarioOfficerState, ScenarioOfficerState> update) => workspace.Update(workspace.Document with
        { Officers = workspace.Document.Officers.Select(o => o.Id == id ? update(o) : o).ToArray() });
        var grid = Grid(body, workspace.OfficerName(id));
        Reference(grid, "所属势力", officer.OwnerId, ForceChoices(), value =>
        {
            Change(o => o with { OwnerId = value });
            foreach (Node child in body.GetChildren()) { body.RemoveChild(child); child.QueueFree(); }
            BuildOfficer(body, id);
        });
        Reference(grid, "所在据点", officer.SettlementId, workspace.Document.Settlements.Select(s => (s.Id, workspace.Name(s.NameKey))), value => Change(o => o with { SettlementId = value }));
        Reference(grid, "所在地域", officer.RegionId, RegionChoices(), value => Change(o => o with { RegionId = value }));
        ContentEditorControls.Choices(grid, "身份", ["君主", "太守", "军师", "一般", "俘虏", "在野", "未发现", "未登场", "死亡", "特殊"], (int)officer.Status,
            index => Change(o => o with { Status = (OfficerStatus)index }));
        Reference(grid, "官职", officer.OfficeId, workspace.Definitions.Information.Offices.Select(o => (o.Id, o.Name)), value => Change(o => o with { OfficeId = value }));
        Number(grid, "伤病", officer.Injury, 0, 14, value => Change(o => o with { Injury = (int)value }));
        ContentEditorControls.Toggle(body, "休眠武将", officer.IsDormant, value => Change(o => o with { IsDormant = value }));
        ContentEditorControls.Toggle(body, "本旬已行动", officer.Acted, value => Change(o => o with { Acted = value }));
        ContentEditorControls.Toggle(body, "军团长", officer.IsCommander, value => Change(o => o with { IsCommander = value }));
        if (officer.OwnerId is int owner)
        {
            Number(grid, "对所属势力忠诚", officer.ForceLoyalties.GetValueOrDefault(owner, workspace.Definitions.DefaultLoyalty), 0, 255, value => Change(o => o with
            { ForceLoyalties = (int)value == workspace.Definitions.DefaultLoyalty ? o.ForceLoyalties.Remove(owner) : o.ForceLoyalties.SetItem(owner, (int)value) }));
        }
        if (officer.UnitId is int unit) ContentEditorControls.Caption(body, $"当前属于部队 [{unit}]，部队引用可在完整初始状态页编辑。");
    }

    private void BuildTreasure(VBoxContainer body, int id)
    {
        var workspace = _workspace!;
        var definition = workspace.Definitions.Information.Treasures.Single(t => t.Id == id);
        var treasure = workspace.GetTreasureState(id);
        void Change(Func<TreasureState, TreasureState> update) => workspace.SetTreasureState(update(workspace.GetTreasureState(id)));
        var grid = Grid(body, definition.Name);
        ContentEditorControls.Choices(grid, "发现状态", ["未启用", "未出土", "已出土"], (int)treasure.Discovery,
            index => Change(t => t with { Discovery = (TreasureDiscovery)index }), "TreasureDiscovery");
        Reference(grid, "持有武将", treasure.HolderId, OfficerChoices(), value => Change(t => t with { HolderId = value }));
        Reference(grid, "出土地域", treasure.DiscoveryRegionId, workspace.RegionIds.Select(region => (region, workspace.RegionName(region))),
            value => Change(t => t with { DiscoveryRegionId = value }));
        body.AddChild(new Label { Text = "未出土且没有持有者的宝物，可在出土地域通过探索获得。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private IEnumerable<(int Id, string Name)> ForceChoices() => _workspace!.Document.Forces.Select(f => (f.Id, _workspace.Name(f.NameKey)));
    private IEnumerable<(int Id, string Name)> OfficerChoices() => _workspace!.Document.Officers.Select(o => (o.Id, _workspace.OfficerName(o.Id)));
    private IEnumerable<(int Id, string Name)> RegionChoices() => _workspace!.Document.UnbuiltPlaces.Select(p => (p.Id, _workspace.RegionName(p.Id)));
    private string ForceName(int id) => _workspace!.Name(_workspace.Document.Forces.Single(f => f.Id == id).NameKey);

    private static GridContainer Grid(Node parent, string title)
    {
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 16); grid.AddThemeConstantOverride("v_separation", 8);
        ContentEditorControls.Section(parent, title).AddChild(grid); return grid;
    }

    private static void Number(Node parent, string title, double value, double min, double max, Action<double> changed, string name = "")
    {
        ContentEditorControls.Caption(parent, title); ContentEditorControls.NumericInput(parent, value, min, max, changed, name);
    }

    private static void Reference(Node parent, string title, int? selected, IEnumerable<(int Id, string Name)> choices, Action<int?> changed)
    {
        var values = choices.ToList();
        if (selected is int id && values.All(v => v.Id != id)) values.Add((id, $"未知引用 {id}"));
        var candidates = values.Select(v => ($"{v.Name} [{v.Id}]", (int?)v.Id))
            .Prepend(("无", (int?)null)).ToArray();
        SearchableReferenceField.Add(parent, title, candidates, selected, changed,
            "ScenarioReference" + title, "按名称或 ID 搜索。", $"未知引用 {selected}");
    }
}
