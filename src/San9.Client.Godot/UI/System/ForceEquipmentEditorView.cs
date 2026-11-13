using System.Collections.Immutable;
using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Catalog-based world editor for forces and equipment instances.</summary>
public partial class ForceEquipmentEditorView : ModalView, IModalInputRouter
{
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public bool Equipment { get; init; }
    public IReadOnlySet<int> AvailableColors { get; init; } = new HashSet<int>();
    private Control? _page;
    private readonly Dictionary<int, (int Trust, int Color)> _forces = [];
    private readonly Dictionary<(int First, int Second), int> _relations = [];
    private readonly Dictionary<long, EquipmentInstanceEdit> _equipment = [];
    private readonly CatalogTableState _listState = new();
    private long _itemId;
    private int _forceId;
    private static readonly CatalogPage[] ForcePage = [new("势力", [new("name", "势力", 16, CatalogAlignment.Left),
        new("base", "基础信望", 12, CatalogAlignment.Right), new("trust", "信望", 8, CatalogAlignment.Right),
        new("color", "颜色", 8, CatalogAlignment.Right)])];
    private static readonly CatalogPage[] ItemPage = [new("装备", [new("name", "装备", 15, CatalogAlignment.Left),
        new("slot", "类别", 8, CatalogAlignment.Left), new("grade", "品阶", 6, CatalogAlignment.Right),
        new("level", "等级", 6, CatalogAlignment.Right), new("experience", "经验", 6, CatalogAlignment.Right),
        new("holder", "持有／库存", 14, CatalogAlignment.Left), new("status", "状态", 10, CatalogAlignment.Left)])];
    private static readonly CatalogPage[] DetailPage = [new("编辑", [new("name", "项目", 20, CatalogAlignment.Left),
        new("value", "数值／说明", 32, CatalogAlignment.Left)])];
    private static readonly CatalogPage[] ChoosePage = [new("选择", [new("name", "名称", 22, CatalogAlignment.Left),
        new("info", "说明", 26, CatalogAlignment.Left)])];
    public override void _Ready() { Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop; ShowList(); }
    private void Replace(Control next)
    {
        if (_page is not null) { RemoveChild(_page); _page.QueueFree(); }
        _page = next; AddChild(next);
    }
    private string Person(int id) => Texts.Resolve(Session.View.MessageSpeaker(id).NameKey);
    private string Force(int id) => Texts.Resolve(Session.View.Forces.Single(f => f.Id == id).NameKey);
    private string ItemName(EquipmentDefinition d) => d.LegacyTreasureId is int id
        ? Texts.Resolve(Session.View.Information.Treasures.Single(t => t.Id == id).NameKey) : d.Name;
    private WorldEditOperation Operation()
    {
        if (Equipment) return new(WorldEditorKind.Equipment, []) { EquipmentEdits = _equipment.Values.OrderBy(e => e.Id).ToImmutableArray() };
        var ids = _forces.Keys.Concat(_relations.Keys.Select(p => p.First)).Distinct().Order().ToArray();
        var edits = ids.Select(id =>
        {
            var source = Session.Snapshot.Forces.Single(f => f.Id == id);
            var values = _forces.GetValueOrDefault(id, (source.BaseTrust, source.ColorCode));
            return (WorldEdit)new ForceEdit(id, values.Item1, values.Item2, _relations.Where(r => r.Key.First == id)
                .OrderBy(r => r.Key.Second).Select(r => new ForceRelation(r.Key.First, r.Key.Second, r.Value)).ToImmutableArray());
        }).ToImmutableArray();
        return new(WorldEditorKind.Forces, edits);
    }
    private bool Changed => _forces.Count != 0 || _relations.Count != 0 || _equipment.Count != 0;
    private WorldSnapshot Preview() => Changed ? Session.PreviewEditor(Operation()) : Session.Snapshot;
    private void ShowList()
    {
        var world = Preview();
        if (!Equipment)
        {
            var forces = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated).OrderBy(f => f.Id).ToArray();
            var rows = forces.Select(f => new CatalogRow(f.Id, [Force(f.Id), f.BaseTrust, f.Trust, f.ColorCode])).ToArray();
            Replace(new CatalogView { Title = "编辑势力", HintText = "选择势力编辑；决定后统一保存。", CloseTitle = "关闭",
                Textures = Textures, Service = Service, Pages = ForcePage, ReadRows = _ => rows, Closed = RequestClose, TableState = _listState,
                Confirmed = Commit, Activated = id => { _forceId = id; ShowForce(); } });
            return;
        }
        var items = world.Equipment.OrderBy(e => e.Id).ToArray();
        var rows2 = items.Select((item, index) =>
        {
            var d = Session.EquipmentDefinitions[item.DefinitionId];
            string holder = item.OfficerId is int p ? Person(p) : item.InventoryForceId is int f ? Force(f) + "库存" : "—";
            string status = item.SearchPlaceId is not null || item.EventGiftKey is not null ? "未发现" : item.IsLost ? "遗失" : item.LegacyTreasureId is int treasure && world.Treasures.Single(t => t.Id == treasure).Discovery != TreasureDiscovery.Discovered
                ? "未发现" : item.Equipped ? "已装备" : "未装备";
            return new CatalogRow(index, [ItemName(d), OfficerEquipment.SlotName(d.Slot), d.Grade, item.Level, item.Experience, holder, status]);
        }).ToArray();
        Replace(new CatalogView { Title = "编辑装备", HintText = "选择装备编辑；包括未发现及遗失物品。", CloseTitle = "关闭",
            Textures = Textures, Service = Service, Pages = ItemPage, ReadRows = _ => rows2, Closed = RequestClose, TableState = _listState,
            Confirmed = Commit, Activated = index => { _itemId = items[index].Id; ShowItem(); } });
    }
    private void ShowForce()
    {
        var world = Preview(); var force = world.Forces.Single(f => f.Id == _forceId);
        var peers = world.Forces.Where(f => !f.IsDormant && !f.IsEliminated && f.Id != _forceId).OrderBy(f => f.Id).ToArray();
        var rows = new List<CatalogRow> { new(0, ["基础信望", force.BaseTrust]), new(1, ["当前信望", force.Trust]), new(2, ["颜色", force.ColorCode]) };
        rows.AddRange(peers.Select((peer, i) => new CatalogRow(i + 3, ["关系 · " + Force(peer.Id),
            world.Relations.SingleOrDefault(r => r.FirstId == Math.Min(peer.Id, _forceId) && r.SecondId == Math.Max(peer.Id, _forceId))?.Value ?? 0])));
        Replace(new CatalogView { Title = Force(_forceId), HintText = "当前信望由基础信望和势力状态计算。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = DetailPage, ReadRows = _ => rows.ToArray(), Closed = ShowList,
            SearchTitle = "取消修改", Search = ResetForce,
            SearchEnabled = _forces.ContainsKey(_forceId) || _relations.Keys.Any(p => p.First == _forceId || p.Second == _forceId),
            Activated = id =>
            {
                if (id == 1) { Notice("当前信望由规则计算，不能直接修改。", ShowForce); return; }
                int max = id == 0 ? 1000 : id == 2 ? 50 : 100;
                int value = (int)rows[id].Cells[1].Number!.Value;
                Number(id == 0 ? "基础信望" : id == 2 ? "颜色" : "势力关系", value, max, n =>
                {
                    if (id == 2 && !AvailableColors.Contains(n)) { Notice("当前内容包没有该颜色的完整旗帜资源。", ShowForce); return; }
                    if (id < 3)
                    {
                        var original = Session.Snapshot.Forces.Single(f => f.Id == _forceId);
                        var draft = _forces.GetValueOrDefault(_forceId, (original.BaseTrust, original.ColorCode));
                        draft = id == 0 ? (n, draft.Item2) : (draft.Item1, n);
                        if (draft == (original.BaseTrust, original.ColorCode)) _forces.Remove(_forceId); else _forces[_forceId] = draft;
                    }
                    else
                    {
                        int other = peers[id - 3].Id; var pair = (Math.Min(other, _forceId), Math.Max(other, _forceId));
                        int initial = Session.Snapshot.Relations.SingleOrDefault(r => r.FirstId == pair.Item1 && r.SecondId == pair.Item2)?.Value ?? 0;
                        if (n == initial) _relations.Remove(pair); else _relations[pair] = n;
                    }
                    ShowForce();
                }, ShowForce);
            } });
    }
    private void ResetForce()
    {
        _forces.Remove(_forceId);
        foreach (var pair in _relations.Keys.Where(p => p.First == _forceId || p.Second == _forceId).ToArray()) _relations.Remove(pair);
        ShowForce();
    }
    private EquipmentInstanceEdit DraftItem()
    {
        var source = Preview().Equipment.Single(e => e.Id == _itemId);
        return new(source.Id, source.OfficerId, source.InventoryForceId, source.Equipped, source.Level, source.Experience);
    }
    private void SetItem(EquipmentInstanceEdit value, Action back)
    {
        var view = Preview(); var original = view.Equipment.Single(e => e.Id == value.Id);
        var definition = Session.EquipmentDefinitions[original.DefinitionId];
        if (value.Equipped)
        {
            var wearer = view.Officers.SingleOrDefault(p => p.Id == value.OfficerId);
            if (wearer is null || !wearer.IsServing) { Notice("装备需要由在职武将持有。", back); return; }
            if (!EquipmentRules.MeetsRequirements(definition, wearer.BaseAbilities))
            { Notice("武将基础属性未达到装备要求：" + OfficerEquipment.Requirements(definition.MinimumAbilities), back); return; }
            var other = view.Equipment.FirstOrDefault(e => e.Id != value.Id && e.OfficerId == wearer.Id && e.Equipped &&
                Session.EquipmentDefinitions[e.DefinitionId].Slot == (definition.Slot == EquipmentSlot.MainHand ? EquipmentSlot.OffHand : EquipmentSlot.MainHand));
            if (other is not null && definition.Slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand)
            {
                var otherDefinition = Session.EquipmentDefinitions[other.DefinitionId];
                bool compatible = definition.Slot == EquipmentSlot.MainHand ? EquipmentRules.Compatible(definition, otherDefinition) : EquipmentRules.Compatible(otherDefinition, definition);
                if (!compatible) { Notice("主武器与副武器不兼容。", back); return; }
            }
        }
        if (value.Level == 5 && value.Experience != 0) { Notice("满级装备不能继续累积经验。", back); return; }
        bool had = _equipment.TryGetValue(value.Id, out var previous); _equipment[value.Id] = value;
        try { _ = Preview(); }
        catch (ArgumentException) { if (had) _equipment[value.Id] = previous!; else _equipment.Remove(value.Id); Notice("无法应用，请检查持有人、等级和装备搭配。", back); return; }
        var initial = Session.Snapshot.Equipment.Single(e => e.Id == value.Id);
        if (value == new EquipmentInstanceEdit(initial.Id, initial.OfficerId, initial.InventoryForceId, initial.Equipped, initial.Level, initial.Experience)) _equipment.Remove(value.Id);
        back();
    }
    private void ShowItem()
    {
        var item = Preview().Equipment.Single(e => e.Id == _itemId); var d = Session.EquipmentDefinitions[item.DefinitionId];
        string holder = item.OfficerId is int p ? Person(p) : item.InventoryForceId is int f ? Force(f) + "库存" : "无";
        CatalogRow[] rows = [new(0, ["持有／库存", holder]), new(1, ["装备状态", item.Equipped ? "已装备" : "未装备"]),
            new(2, ["等级", item.Level]), new(3, ["经验", item.Experience]), new(4, ["品阶", d.Grade]),
            new(5, ["装备要求", OfficerEquipment.Requirements(d.MinimumAbilities)])];
        Replace(new CatalogView { Title = ItemName(d), HintText = "选择项目编辑；品阶与要求由物品定义。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = DetailPage, ReadRows = _ => rows, Closed = ShowList,
            SearchTitle = "取消修改", SearchEnabled = _equipment.ContainsKey(_itemId), Search = () => { _equipment.Remove(_itemId); ShowItem(); },
            Activated = id =>
            {
                if (id == 0) { ShowHolders(); return; }
                if (id == 1) { SetItem(DraftItem() with { Equipped = !item.Equipped }, ShowItem); return; }
                if (id is 2 or 3 && d.Slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand or EquipmentSlot.HiddenWeapon or EquipmentSlot.Helmet or EquipmentSlot.Armor)
                { Number(id == 2 ? "等级" : "经验", id == 2 ? item.Level : item.Experience, id == 2 ? 5 : 99,
                    n => SetItem(id == 2 ? DraftItem() with { Level = n, Experience = n == 5 ? 0 : item.Experience } : DraftItem() with { Experience = n }, ShowItem), ShowItem, id == 2 ? 1 : 0); return; }
                Notice("此项目不能修改。", ShowItem);
            } });
    }
    private void ShowHolders()
    {
        CatalogRow[] rows = [new(0, ["武将持有", "选择武将"]), new(1, ["势力库存", "选择势力"])];
        Replace(new CatalogView { Title = "选择持有方式", HintText = "转移后默认卸下装备。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = ChoosePage, ReadRows = _ => rows, Closed = ShowItem,
            Activated = id => ShowOwners(id == 0) });
    }
    private void ShowOwners(bool people)
    {
        var world = Preview();
        var targets = people ? world.Officers.Where(p => !p.IsDormant && p.IsVisible).OrderBy(p => p.Id).Select(p => (p.Id, Name: Person(p.Id))).ToArray() :
            world.Forces.Where(f => !f.IsDormant && !f.IsEliminated).OrderBy(f => f.Id).Select(f => (f.Id, Name: Force(f.Id))).ToArray();
        var rows = targets.Select(t => new CatalogRow(t.Id, [t.Name, people ? "持有" : "库存"])).ToArray();
        Replace(new CatalogView { Title = people ? "选择武将" : "选择势力", HintText = "选择新持有人或库存。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = ChoosePage, ReadRows = _ => rows, Closed = ShowHolders,
            Activated = id => SetItem(DraftItem() with { OfficerId = people ? id : null, InventoryForceId = people ? null : id, Equipped = false }, ShowItem) });
    }
    private void Number(string title, int value, int max, Action<int> apply, Action back, int min = 0)
    {
        Replace(new NumberInputDialog { Textures = Textures, Subject = title, State = new((uint)min, (uint)max, (uint)value),
            Closed = back, Confirmed = n => apply((int)n) });
    }
    private void RequestClose()
    {
        if (!Changed) { Closed(); return; }
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Confirmed += Closed; dialog.Dismissed += ShowList; dialog.Closed += ShowList;
        dialog.SetRichMessage(Texts.ResolveRich(WorldEditorText.ConfirmDiscardKey)); dialog.FocusDefaultButton();
    }
    private void Commit()
    {
        if (!Changed) { Closed(); return; }
        var result = Session.Apply(Session.Request(Operation()));
        if (result.Disposition == SessionDisposition.Accepted) { Closed(); return; }
        Notice("编辑未能执行，草稿仍保留。", ShowList);
    }
    private void Notice(string text, Action back)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Closed += back; dialog.SetNotice(text);
    }
}
