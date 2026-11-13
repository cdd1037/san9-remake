using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Equipment slots and items use the same catalog and confirmation windows as the information screens.</summary>
public partial class OfficerEquipmentView : ModalView, IModalInputRouter
{
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public int OfficerId { get; init; }
    public Action Closed { get; set; } = null!;
    private Control? _page;
    private static readonly CatalogPage[] SlotsPage = [new("装备", [
        new("slot", "部位", 12, CatalogAlignment.Left), new("name", "装备", 22, CatalogAlignment.Left),
        new("level", "品阶", 8, CatalogAlignment.Right), new("effect", "效果", 28, CatalogAlignment.Left)])];
    private static readonly CatalogPage[] ItemsPage = [new("装备", [
        new("name", "名称", 18, CatalogAlignment.Left), new("rarity", "稀有度", 8, CatalogAlignment.Center),
        new("level", "品阶／熟练", 12, CatalogAlignment.Right), new("progress", "经验", 10, CatalogAlignment.Right),
        new("state", "状态", 10, CatalogAlignment.Left), new("effect", "效果", 24, CatalogAlignment.Left)])];
    private static readonly CatalogPage[] ActionsPage = [new("操作", [
        new("action", "操作", 14, CatalogAlignment.Left), new("reason", "说明", 44, CatalogAlignment.Left)])];

    public override void _Ready() { Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop; ShowSlots(); }
    private void Replace(Control page)
    {
        if (_page is not null) { RemoveChild(_page); _page.QueueFree(); }
        _page = page; AddChild(page);
    }
    private void ShowSlots()
    {
        var data = OfficerEquipment.Read(Session, Texts, OfficerId);
        var rows = data.Slots.Select((slot, index) =>
        {
            var item = slot.Items.FirstOrDefault(i => i.Equipped);
            return new CatalogRow(index, [slot.Name, item?.Name ?? "—", item is null ? "—" : $"{item.Grade}阶", item?.Effect ?? ""]);
        }).ToArray();
        string person = Texts.Resolve(Session.View.Officers.Single(o => o.Id == OfficerId).NameKey);
        Replace(new CatalogView { Title = "装备", HintText = $"{person}　选择部位查看装备。", CloseTitle = "关闭",
            Textures = Textures, Service = Service, Pages = SlotsPage, ReadRows = _ => rows, Closed = Closed,
            Activated = index => ShowItems(data.Slots[index].Slot) });
    }
    private void ShowItems(EquipmentSlot selected)
    {
        var slot = OfficerEquipment.Read(Session, Texts, OfficerId).Slots.Single(s => s.Slot == selected);
        var rows = slot.Items.Select((item, index) => new CatalogRow(index, [item.Name,
            item.Rarity switch { EquipmentRarity.Ordinary => "普通", EquipmentRarity.Precious => "珍贵", _ => "稀世" },
            $"{item.Grade}／{item.Level}", item.Slot is EquipmentSlot.Mount or EquipmentSlot.Treasure or EquipmentSlot.Tally or EquipmentSlot.Book ? "固定" :
                item.Level == 5 ? "满级" : $"{item.Experience}/100", item.Equipped ? "已装备" : item.InInventory ? "库存" : "持有", item.Effect])).ToArray();
        Replace(new CatalogView { Title = slot.Name, HintText = rows.Length == 0 ? "此部位没有可查看的物品。" : "选择物品查看可用操作；宝物通过赏赐或没收转移。", CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = ItemsPage, ReadRows = _ => rows, Closed = ShowSlots,
            ItemIcon = index => FeaturedEquipmentIcons.Find(slot.Items[index].DefinitionId, slot.Items[index].Name), PortraitSize = new(80, 80),
            Activated = index => ShowActions(selected, slot.Items[index].Id) });
    }
    private void ShowActions(EquipmentSlot selected, long itemId)
    {
        var data = OfficerEquipment.Read(Session, Texts, OfficerId);
        var item = data.Slots.Single(s => s.Slot == selected).Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) { ShowItems(selected); return; }
        var rows = new[] {
            new CatalogRow(0, [item.Equipped ? "卸下" : "装备", item.Equipped ? (data.CanManage ? "卸下当前装备" : "当前仅供查看") : item.CanEquip ? item.Effect : item.UnavailableReason ?? "当前无法装备"]),
            new CatalogRow(1, ["入库", item.IsTreasure ? "宝物通过赏赐或没收转移" : item.InInventory ? "已在库存" : data.CanManage ? "收回至势力库存" : "当前仅供查看"]) };
        string origin = item.Origin switch { EquipmentOrigin.Project => "项目自创", EquipmentOrigin.Historical => "史实", EquipmentOrigin.Romance => "演义", _ => "" };
        Replace(new CatalogView { Title = item.Name, HintText = $"{item.Effect}　基础属性要求：{item.Requirements}" + (origin.Length == 0 ? "" : $"　来源：{origin}"), CloseTitle = "返回",
            Textures = Textures, Service = Service, Pages = ActionsPage, ReadRows = _ => rows, Closed = () => ShowItems(selected),
            ItemIcon = _ => FeaturedEquipmentIcons.Find(item.DefinitionId, item.Name), PortraitSize = new(80, 80),
            Activated = action =>
            {
                bool allowed = action == 0 ? item.Equipped ? data.CanManage : item.CanEquip : data.CanManage && !item.InInventory && !item.IsTreasure;
                if (!allowed) { Notice(rows[action].Cells[1].Text, () => ShowActions(selected, itemId)); return; }
                string verb = action == 1 ? "入库" : item.Equipped ? "卸下" : "装备";
                Confirm($"{verb} {item.Name}？", () => Execute(selected, item, action), () => ShowActions(selected, itemId));
            } });
    }
    private void Execute(EquipmentSlot slot, OfficerEquipmentItem item, int action)
    {
        if (Session.View.PlayerForceId is not int force) { Notice("当前无法操作装备。", () => ShowItems(slot)); return; }
        var result = action == 1
            ? Session.Execute(new("player"), new TransferEquipmentCommand(Guid.NewGuid(), force, Session.Snapshot.Simulation.NextSequence, item.Id, null))
            : Session.Execute(new("player"), new ConfigureEquipmentCommand(Guid.NewGuid(), force, Session.Snapshot.Simulation.NextSequence, OfficerId, slot, item.Equipped ? null : item.Id));
        if (result.Submission.Accepted) ShowSlots();
        else Notice("操作失败，请检查回合、武将和装备条件。", () => ShowItems(slot));
    }
    private void Confirm(string message, Action yes, Action back)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Confirmed += yes; dialog.Dismissed += back; dialog.Closed += back; dialog.SetMessage(message);
    }
    private void Notice(string message, Action back)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; Replace(dialog);
        dialog.Closed += back; dialog.SetNotice(message);
    }
}
