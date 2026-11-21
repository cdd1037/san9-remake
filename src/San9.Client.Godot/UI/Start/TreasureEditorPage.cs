using System.Collections.Immutable;
using Godot;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class TreasureEditorPage : SharedCatalogEditorPage
{
    public TextureService? Service { get; init; }
    private SharedCatalogActions _actions = null!;
    public override bool HasPendingChanges => _actions.Busy;
    public override bool CommitPending() => !_actions.Busy;

    protected override void BuildSidebarActions(VBoxContainer sidebar)
    {
        _actions = new()
        {
            Page = this, Subject = "宝物", Identity = "Treasure", Current = () => Current,
            CanDelete = id => Workspace.Treasures.Any(t => t.Id == id), DeleteUnavailableTooltip = "请先选择宝物。",
            EntryName = id => Workspace.GetTreasure(id).Name,
            Create = async (id, token) => (await Workspace.CreateTreasureAsync(id, token)).Id,
            Delete = Workspace.DeleteTreasureAsync, Select = SelectEntry,
            SuccessMessage = "已更新共享宝物草稿，尚未保存。"
        };
        sidebar.AddChild(_actions);
    }

    protected override void RefreshActions()
    {
        _actions.Refresh();
    }

    private const string RetentionGroupTooltip = "AI 送出宝物时，通常每组留一件。\n"
        + "能力组保留对应加成较高的宝物。\n"
        + "“其他”组比较忠诚加成。\n"
        + "持有人偏好优先于加成高低。";
    private const string AutomaticOfferTooltip = "允许 AI 自动把此宝物用于\n"
        + "外交补偿或赏赐武将。\n"
        + "实际送出哪件，还会考虑分组和持有人偏好。";
    private const string FavoredOwnerTooltip = "AI 优先让名单中的武将持有此宝物。\n"
        + "已由他们持有时，会保留且不会没收。\n"
        + "接收人在名单中时，可选此宝物送出。\n"
        + "双方都在名单中时，当前持有人优先。";

    protected override string Subject => "宝物";
    protected override IEnumerable<(int Id, string Name)> Entries => Workspace.Treasures.Select(t => (t.Id, t.Name));
    protected override Action<int, string> Rename => (id, value) => Edit(id, t => t with { Name = value });
    protected override bool Changed(int id) => Workspace.IsTreasureChanged(id);
    protected override void Restore(int id) => Workspace.RestoreTreasure(id);

    private void Edit(int id, Func<TreasureDefinition, TreasureDefinition> change) => Workspace.SetTreasure(change(Workspace.GetTreasure(id)));

    private void BuildDefaultPlacement(TreasureDefinition treasure)
    {
        var group = Options("默认初始安置", "剧本未单独安置此宝物时生效。默认持有人未入局时不投放。");
        void Place(TreasurePlacement? placement)
        {
            Edit(treasure.Id, t => t with { DefaultPlacement = placement });
            Rebuild();
        }
        var holder = treasure.DefaultPlacement as OfficerTreasurePlacement;
        var region = treasure.DefaultPlacement as RegionTreasurePlacement;
        SearchableReferenceField.Add(group, "持有武将", Workspace.Officers.Select(o => ($"{o.Name} [{o.Id}]", o.Id)).ToArray(),
            holder?.OfficerId ?? -1, Guard<int>(id => Place(new OfficerTreasurePlacement(id))),
            "DefaultTreasureHolder", "按姓名或 ID 选择共享武将。", "选择持有武将", showCaption: false, keepFirstVisible: false);
        (string, int)[] Regions()
        {
            try { return Workspace.RegionNames.Select(p => ($"{p.Value} [{p.Key}]", p.Key)).ToArray(); }
            catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                ReportStatus("读取地域失败：" + error.Message, true);
                return [];
            }
        }
        var regionLabel = region is null ? "选择埋藏地域" : Regions().FirstOrDefault(p => p.Item2 == region.RegionId).Item1 ?? $"地域 [{region.RegionId}]";
        SearchableReferenceField.Add(group, "埋藏地域", Regions, region?.RegionId ?? -1, regionLabel,
            Guard<int>(id => Place(new RegionTreasurePlacement(id))),
            "DefaultTreasureRegion", "按名称或 ID 选择地域。", showCaption: false, keepFirstVisible: false);
        ContentEditorControls.ActionButton(group, "不投放", () => Place(null)).Disabled = treasure.DefaultPlacement is null;
    }

    protected override void BuildForm(int id)
    {
        var treasure = Workspace.GetTreasure(id);
        var introduction = new HBoxContainer();
        introduction.AddThemeConstantOverride("separation", 18); Form.AddChild(introduction);
        BuildIllustration(introduction, id, treasure);
        var description = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        introduction.AddChild(description);
        var descriptionField = ContentEditorControls.TextArea(description, "说明", treasure.Description,
            Guard<string>(value => Edit(id, t => t with { Description = value })), "TreasureDescription");
        descriptionField.CustomMinimumSize = new(0, 128);
        BuildDefaultPlacement(treasure);
        var basics = Section("基本属性");
        Choice(basics, "种类", ["名马", "武器", "兵书", "政书", "史书", "经书", "论文", "地图", "医书", "奇书", "玉玺", "宝物", "装饰"],
            treasure.Category, value => Edit(id, t => t with { Category = value }));
        Number(basics, "忠诚加成", treasure.LoyaltyBonus, 0, int.MaxValue, value => Edit(id, t => t with { LoyaltyBonus = (int)value }), "TreasureLoyaltyBonus");
        var bonuses = Section("持有能力加成");
        Number(bonuses, "统率", treasure.AbilityBonuses.Leadership, 0, int.MaxValue, value => Edit(id, t => t with { AbilityBonuses = t.AbilityBonuses with { Leadership = (int)value } }), "TreasureLeadership");
        Number(bonuses, "武力", treasure.AbilityBonuses.War, 0, int.MaxValue, value => Edit(id, t => t with { AbilityBonuses = t.AbilityBonuses with { War = (int)value } }), "TreasureWar");
        Number(bonuses, "智力", treasure.AbilityBonuses.Intelligence, 0, int.MaxValue, value => Edit(id, t => t with { AbilityBonuses = t.AbilityBonuses with { Intelligence = (int)value } }), "TreasureIntelligence");
        Number(bonuses, "政治", treasure.AbilityBonuses.Politics, 0, int.MaxValue, value => Edit(id, t => t with { AbilityBonuses = t.AbilityBonuses with { Politics = (int)value } }), "TreasurePolitics");
        var effects = Section("持有效果");
        var definitions = Workspace.Definitions;
        var tactics = definitions.Information.Tactics.Select(t => (t.Id, t.Name))
            .Concat((definitions.Military?.Tactics ?? []).Where(t => !definitions.Information.Tactics.Any(catalog => catalog.Id == t.Id))
                .Select(t => (t.Id, Name: $"兵法 [{t.Id}]"))).OrderBy(t => t.Id).ToArray();
        List<int?> tacticIds = [null, .. tactics.Select(t => (int?)t.Id)];
        List<string> names = ["无", .. tactics.Select(t => t.Name)];
        int selected = tacticIds.IndexOf(treasure.TacticId);
        if (selected < 0) { selected = tacticIds.Count; tacticIds.Add(treasure.TacticId); names.Add($"当前兵法 [{treasure.TacticId}]"); }
        Choice(effects, "持有兵法", names, selected, value => Edit(id, t => t with { TacticId = tacticIds[value] }));
        Toggle(Form, "持有时可安然撤退", treasure.EscapeMount, value => Edit(id, t => t with { EscapeMount = value }), "TreasureEscapeMount");
        var diplomacy = Section("自动外交补偿", AutomaticOfferTooltip);
        Choice(diplomacy, "保留分组", ["其他", "武力", "智力", "政治", "统率"], (int)treasure.RetentionGroup,
            value => Edit(id, t => t with { RetentionGroup = (TreasureRetentionGroup)value }), RetentionGroupTooltip);
        Toggle(Form, "允许自动作为外交补偿", treasure.AutomaticDiplomacyOfferAllowed,
            value => Edit(id, t => t with { AutomaticDiplomacyOfferAllowed = value }), "TreasureAutomaticOffer", AutomaticOfferTooltip);
        var favorites = Options("持有人偏好", FavoredOwnerTooltip);
        var removePreference = Guard<int>(person =>
        {
            Edit(id, t => t with { FavoredOwnerIds = t.FavoredOwnerIds.Remove(person) }); Rebuild();
        });
        foreach (int person in treasure.FavoredOwnerIds)
        {
            string name = Workspace.Officers.SingleOrDefault(o => o.Id == person)?.Name ?? $"人物 {person}（不在共享目录）";
            var remove = ContentEditorControls.ActionButton(favorites, name + " ×", () => removePreference(person));
            remove.TooltipText = $"将{name}移出偏好名单。";
        }
        (string, int?)[] Candidates() => Workspace.Officers.Where(o => !treasure.FavoredOwnerIds.Contains(o.Id))
            .Select(o => (Label: $"{o.Name} [{o.Id}]", Value: (int?)o.Id)).Prepend(("＋ 添加武将", (int?)null)).ToArray();
        var addPreference = SearchableReferenceField.Add(favorites, "武将", Candidates, (int?)null, "＋ 添加武将", Guard<int?>(value =>
        {
            if (value is not int person) return;
            Edit(id, t => t with { FavoredOwnerIds = t.FavoredOwnerIds.Append(person).Distinct().Order().ToImmutableArray() }); Rebuild();
        }), "TreasureFavoredOwner", "按姓名或 ID 搜索武将。\n选定后直接加入偏好名单。", showCaption: false);
        addPreference.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        addPreference.Disabled = !Workspace.Officers.Any(o => !treasure.FavoredOwnerIds.Contains(o.Id));
    }
}
