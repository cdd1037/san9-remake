using Godot;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class ScenarioEditor
{
    private static readonly string[] OfficerStatusNames = ["君主", "太守", "军师", "一般", "俘虏", "在野", "未发现", "未登场", "死亡", "特殊"];

    private void BuildOverview()
    {
        var mapPage = new MarginContainer { Name = "开局地图" };
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) mapPage.AddThemeConstantOverride("margin_" + edge, 16);
        _pages.AddChild(mapPage);
        BuildCityOverview(mapPage);

        var scroll = new ScrollContainer { Name = "剧本设置", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _pages.AddChild(scroll);
        var body = ContentEditorControls.PageBody(scroll);
        var columns = new GridContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("h_separation", 24);
        columns.AddThemeConstantOverride("v_separation", 16);
        body.AddChild(columns);
        void FitColumns() => columns.Columns = columns.Size.X >= 740 ? 2 : 1;
        columns.Resized += FitColumns;
        FitColumns();
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var rules = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(text); columns.AddChild(rules);
        var workspace = _workspace!;
        var heading = Grid(text, "剧本信息");
        ContentEditorControls.TextField(heading, "标题", workspace.Preview.Title,
            value => workspace.SetDescription(value, workspace.Preview.Description), "ScenarioTitle");
        var description = ContentEditorControls.TextArea(text, "简介", workspace.Preview.Description,
            value => workspace.SetDescription(workspace.Preview.Title, value), "ScenarioDescription");
        description.CustomMinimumSize = new(0, 200);
        var date = Grid(rules, "开局日期");
        Number(date, "年份", workspace.Document.Year, 1, int.MaxValue, value => workspace.Update(workspace.Document with { Year = (int)value }), "ScenarioYear");
        Number(date, "月份", workspace.Document.Month, 1, 12, value => workspace.Update(workspace.Document with { Month = (int)value }), "ScenarioMonth");
        ContentEditorControls.Choices(date, "旬", ["上旬（1日）", "中旬（11日）", "下旬（21日）"], (workspace.Document.Day - 1) / 10,
            index => workspace.Update(workspace.Document with { Day = index * 10 + 1 }), "ScenarioXun");
        Reference(date, "默认玩家势力", workspace.Document.PlayerForceId, ForceChoices(),
            id => workspace.Update(workspace.Document with { PlayerForceId = id }));
        var settings = ContentEditorControls.ButtonGroup(rules, "初始规则");
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

    private static (VBoxContainer Side, LineEdit Search, VBoxContainer Form) BuildBrowser(
        TabContainer parent, string name, int width, bool scrollForm)
    {
        var split = new HSplitContainer { Name = name, SplitOffsets = [width] };
        parent.AddChild(split);
        var side = ContentEditorControls.Sidebar(split);
        var search = new LineEdit { Name = name + "搜索", PlaceholderText = "搜索名称或 ID" };
        side.AddChild(search);
        if (scrollForm)
        {
            var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            split.AddChild(scroll);
            return (side, search, ContentEditorControls.PageBody(scroll));
        }
        var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(form);
        return (side, search, form);
    }

    private VBoxContainer BuildListPage(string name, Func<IEnumerable<(int Id, string Name)>> entries,
        Action<VBoxContainer, int> build, int? selectedId, TabContainer? parent = null, bool scrollForm = true)
    {
        var (side, search, form) = BuildBrowser(parent ?? _pages, name, 245, scrollForm);
        var list = new ItemList { Name = name + "列表", SizeFlagsVertical = SizeFlags.ExpandFill };
        side.AddChild(list);
        void Select(int row)
        {
            selectedId = row < 0 ? null : list.GetItemMetadata(row).AsInt32();
            ContentEditorControls.ClearChildren(form);
            if (selectedId is int id) build(form, id);
        }
        void Filter(string query)
        {
            list.Clear();
            int selectedRow = 0;
            foreach (var (id, label) in entries())
            {
                if (!label.Contains(query, StringComparison.OrdinalIgnoreCase) && !id.ToString().Contains(query, StringComparison.Ordinal)) continue;
                int row = list.AddItem($"{label}　[{id}]");
                list.SetItemMetadata(row, id);
                if (id == selectedId) selectedRow = row;
            }
            if (list.ItemCount > 0) list.Select(selectedRow);
            Select(list.ItemCount > 0 ? selectedRow : -1);
        }
        list.ItemSelected += row => Select((int)row);
        search.TextChanged += Filter;
        Filter("");
        return side;
    }

    private void BuildOfficerPage(int? selectedId)
    {
        var side = BuildListPage("武将安置", OfficerChoices, BuildOfficer, selectedId);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        side.AddChild(actions);
        var deploy = ContentEditorControls.ActionButton(actions, "批量安置", () => DeploymentDialog(false), "AddSharedOfficer");
        var withdraw = ContentEditorControls.ActionButton(actions, "撤回安置", () => DeploymentDialog(true));
        deploy.SizeFlagsHorizontal = withdraw.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    private int _forceTab;

    private void BuildForce(VBoxContainer host, int id)
    {
        var workspace = _workspace!;
        ForceInitialState Current() => workspace.Document.Forces.Single(f => f.Id == id);
        void Change(Func<ForceInitialState, ForceInitialState> update) => workspace.Update(workspace.Document with
        { Forces = workspace.Document.Forces.Select(f => f.Id == id ? update(f) : f).ToArray() });
        var force = Current();
        _browsedForce = id;
        var tabs = new TabContainer { Name = "ForceTabs", SizeFlagsVertical = SizeFlags.ExpandFill };
        host.AddChild(tabs);
        var settings = new ScrollContainer { Name = "势力资料", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(settings);
        var roster = new MarginContainer { Name = "据点与武将" };
        foreach (string edge in new[] { "left", "top", "right", "bottom" }) roster.AddThemeConstantOverride("margin_" + edge, 12);
        tabs.AddChild(roster);
        BuildForceRoster(roster, force);
        tabs.CurrentTab = _forceTab;
        tabs.TabChanged += tab => _forceTab = (int)tab;
        var body = ContentEditorControls.PageBody(settings);
        var grid = Grid(body, workspace.Name(force.NameKey));
        void FitFields() => grid.Columns = grid.Size.X >= 580 ? 4 : 2;
        grid.Resized += FitFields;
        Number(grid, "信望", force.Trust, 0, 1000, value => Change(f => f with { Trust = (int)value }));
        Number(grid, "基础信望", force.BaseTrust, 0, 1000, value => Change(f => f with { BaseTrust = (int)value }));
        var officers = workspace.Document.Officers.Where(o => o.OwnerId == id && o.Status <= OfficerStatus.Serving && o.IsDormant == force.IsDormant
            && !workspace.Document.Information.Corps.Any(c => !c.IsRoot && c.LeaderId == o.Id)).Select(o => (o.Id, Name: workspace.OfficerName(o.Id))).ToArray();
        Reference(grid, "君主", force.LeaderId, officers.Where(o => o.Id != force.AdviserId),
            value => RunChange(() => workspace.SetForceOffice(id, value, true), id));
        Reference(grid, "军师", force.AdviserId, officers.Where(o => o.Id != force.LeaderId),
            value => RunChange(() => workspace.SetForceOffice(id, value, false), id));
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
        ContentEditorControls.ActionButton(body, "删除势力", () => EditOperation("删除势力", form =>
        {
            ContentEditorControls.Caption(form, $"从当前剧本删除「{workspace.Name(force.NameKey)}」？\n所属据点转为无主，在职武将就地转为在野。\n军团及其物资一并移除，相关外交配置随之调整。");
            return () => workspace.DeleteForce(id);
        }));
    }

    private void BuildForceRoster(MarginContainer body, ForceInitialState force)
    {
        var workspace = _workspace!;
        var doc = workspace.Document;
        var corps = doc.Information.Corps.Where(c => c.OwnerId == force.Id).OrderByDescending(c => c.IsRoot).ThenBy(c => c.Id).ToArray();
        int? rootId = corps.FirstOrDefault(c => c.IsRoot)?.Id;
        var places = doc.Settlements.Where(s => s.OwnerId == force.Id).OrderBy(s => s.Kind != SettlementKind.City).ThenBy(s => s.Id).ToArray();
        var people = doc.Officers.Where(o => o.OwnerId == force.Id && o.Status != OfficerStatus.Dead)
            .OrderBy(o => o.Status).ThenBy(o => o.Id).ToArray();
        var placed = people.Where(o => o.Status <= OfficerStatus.Serving && o.UnitId is null && o.SettlementId.HasValue)
            .ToLookup(o => o.SettlementId!.Value);
        var section = ContentEditorControls.Section(body, $"{workspace.Name(force.NameKey)} · {places.Length} 处据点 · {people.Length} 名武将");
        var tree = new Tree { Name = "ForceRoster", HideRoot = true, Columns = 3, ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row, SizeFlagsVertical = SizeFlags.ExpandFill };
        section.AddChild(tree);
        tree.SetColumnTitle(0, "军团 / 据点 / 武将");
        tree.SetColumnTitle(1, "兵力");
        tree.SetColumnTitle(2, "驻将");
        tree.SetColumnExpandRatio(0, 3);
        tree.SetColumnTitleAlignment(0, HorizontalAlignment.Left);
        var root = tree.CreateItem();
        var destinations = new Dictionary<TreeItem, (string Page, int Id)>();
        var shown = new HashSet<int>();
        void AddOfficer(TreeItem parent, ScenarioOfficerState officer)
        {
            var item = tree.CreateItem(parent);
            item.SetText(0, $"{workspace.OfficerName(officer.Id)}　{OfficerStatusNames[(int)officer.Status]}" + (officer.IsDormant ? "（休眠）" : ""));
            destinations[item] = ("武将安置", officer.Id);
        }
        void AddGroup(string label, SettlementInitialState[] members)
        {
            var group = tree.CreateItem(root);
            group.SetText(0, label);
            group.SetTextAlignment(1, HorizontalAlignment.Center);
            group.SetTextAlignment(2, HorizontalAlignment.Center);
            group.SetText(1, members.Sum(s => (long)s.Soldiers).ToString("N0"));
            group.SetText(2, members.Sum(s => placed[s.Id].Count()).ToString());
            foreach (var place in members)
            {
                var item = tree.CreateItem(group);
                item.SetText(0, workspace.Name(place.NameKey) + (place.IsDormant ? "（休眠）" : ""));
                item.SetTextAlignment(1, HorizontalAlignment.Center);
                item.SetTextAlignment(2, HorizontalAlignment.Center);
                item.SetText(1, place.Soldiers.ToString("N0"));
                item.SetText(2, placed[place.Id].Count().ToString());
                destinations[item] = (place.Kind == SettlementKind.City && !place.IsDormant ? "开局地图" : "据点", place.Id);
                foreach (var officer in placed[place.Id]) { AddOfficer(item, officer); shown.Add(officer.Id); }
                item.Collapsed = true;
            }
        }
        foreach (var c in corps)
            AddGroup(c.IsRoot ? $"直属军团 [{c.Id}]" : $"军团 [{c.Id}]", places.Where(s => (s.CorpsId ?? rootId) == c.Id).ToArray());
        var unassigned = places.Where(s => !corps.Any(c => c.Id == (s.CorpsId ?? rootId))).ToArray();
        if (unassigned.Length > 0) AddGroup("待分配军团", unassigned);
        var other = people.Where(o => !shown.Contains(o.Id)).ToArray();
        if (other.Length > 0)
        {
            var group = tree.CreateItem(root);
            group.SetText(0, $"其他所属武将（{other.Length}）");
            foreach (var officer in other) AddOfficer(group, officer);
            group.Collapsed = true;
        }
        tree.ItemSelected += () =>
        {
            if (!destinations.TryGetValue(tree.GetSelected(), out var destination)) return;
            Callable.From(() =>
            {
                if (!IsInstanceValid(tree) || !tree.IsInsideTree()) return;
                if (destination.Page == "开局地图") _selectedCity = destination.Id;
                BuildPages(_pages.GetNode<Control>(destination.Page).GetIndex(), destination.Id);
            }).CallDeferred();
        };
    }

    private void BuildSettlement(VBoxContainer body, int id)
    {
        var workspace = _workspace!; var settlement = workspace.Document.Settlements.Single(s => s.Id == id);
        void Change(Func<SettlementInitialState, SettlementInitialState> update) => workspace.Update(workspace.Document with
        { Settlements = workspace.Document.Settlements.Select(s => s.Id == id ? update(s) : s).ToArray() });
        var grid = Grid(body, workspace.Name(settlement.NameKey));
        ContentEditorControls.Caption(grid, "所属势力");
        ContentEditorControls.ActionButton(grid, (settlement.OwnerId is int owner ? ForceName(owner) : "无主") + " · 移交",
            () => TransferDialog(settlement)).Disabled = settlement.IsDormant;
        Reference(grid, "所属军团", settlement.CorpsId, workspace.Document.Information.Corps.Where(c => c.OwnerId == settlement.OwnerId).Select(c => (c.Id, $"{c.Id} · {ForceName(c.OwnerId)}")), value => Change(s => s with { CorpsId = value }));
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
            ContentEditorControls.ClearChildren(body);
            BuildOfficer(body, id);
        });
        Reference(grid, "所在据点", officer.SettlementId, workspace.Document.Settlements.Select(s => (s.Id, workspace.Name(s.NameKey))), value => Change(o => o with { SettlementId = value }));
        Reference(grid, "所在地域", officer.RegionId, RegionChoices(), value => Change(o => o with { RegionId = value }));
        ContentEditorControls.Choices(grid, "身份", OfficerStatusNames, (int)officer.Status,
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
        var placement = workspace.GetTreasurePlacement(id);
        var grid = Grid(body, definition.Name);
        ContentEditorControls.Caption(grid, "当前安置");
        grid.AddChild(new Label { Text = (placement is null && definition.DefaultPlacement is not null ? "共享默认 · " : "") + ((placement ?? definition.DefaultPlacement) switch
        {
            OfficerTreasurePlacement held => "武将持有 · " + workspace.OfficerName(held.OfficerId),
            RegionTreasurePlacement buried => "地域埋藏 · " + workspace.RegionName(buried.RegionId),
            _ => "未投放"
        }) });
        void Place(TreasurePlacement? value)
        {
            workspace.SetTreasurePlacement(id, value);
            ContentEditorControls.ClearChildren(body);
            BuildTreasure(body, id);
        }
        var actions = ContentEditorControls.ButtonGroup(body, "安置宝物");
        var officers = workspace.Document.Officers.Where(o => o.Status != OfficerStatus.Dead)
            .Select(o => ($"{workspace.OfficerName(o.Id)} [{o.Id}]", o.Id)).ToArray();
        var holder = SearchableReferenceField.Add(actions, "持有武将", officers,
            (placement as OfficerTreasurePlacement)?.OfficerId ?? -1, person => Place(new OfficerTreasurePlacement(person)),
            "TreasureHolder", "按姓名或 ID 搜索本剧本存活武将。", "投放到武将", showCaption: false, keepFirstVisible: false);
        holder.Text = "投放到武将";
        var regions = workspace.RegionIds.Select(region => ($"{workspace.RegionName(region)} [{region}]", region)).ToArray();
        var regionButton = SearchableReferenceField.Add(actions, "埋藏地域", regions,
            (placement as RegionTreasurePlacement)?.RegionId ?? -1, region => Place(new RegionTreasurePlacement(region)),
            "TreasureRegion", "按名称或 ID 搜索埋藏地域。", "埋藏到地域", showCaption: false, keepFirstVisible: false);
        regionButton.Text = "埋藏到地域";
        ContentEditorControls.ActionButton(actions, "清除剧本安置", () => Place(null), "RemoveTreasurePlacement").Disabled = placement is null;
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
