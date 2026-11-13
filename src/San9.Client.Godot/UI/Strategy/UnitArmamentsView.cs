using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Lists individual detachments and submits one confirmed equipment change at a time.</summary>
public partial class UnitArmamentsView : ModalView, IModalInputRouter
{
    public GameSession Session { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public int UnitId { get; init; }
    public Action Closed { get; init; } = null!;
    private Control? _page;
    private static readonly CatalogPage[] Slots = [new("分队", [new("officer", "武将", 12, CatalogAlignment.Left),
        new("slot", "部位", 8, CatalogAlignment.Left), new("item", "军械", 16, CatalogAlignment.Left),
        new("coverage", "配发／兵力", 18, CatalogAlignment.Right)])];
    private static readonly CatalogPage[] Choices = [new("选择", [new("item", "军械", 18, CatalogAlignment.Left),
        new("available", "可配", 12, CatalogAlignment.Right), new("condition", "条件", 24, CatalogAlignment.Left)])];

    public override void _Ready()
    {
        Textures.RequestPrefixes("catalog-", "information-");
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop; ShowSlots();
    }
    private void Replace(Control page)
    {
        if (_page is not null) { RemoveChild(_page); _page.QueueFree(); }
        _page = page; AddChild(page);
    }
    private void ShowSlots()
    {
        var unit = Session.Snapshot.Units.FirstOrDefault(item => item.Id == UnitId);
        if (unit is null) { Closed(); return; }
        var slots = unit.Detachments.OrderBy(pair => pair.Key).SelectMany(pair =>
            new[] { (Officer: pair.Key, Slot: ArmamentSlot.Weapon), (Officer: pair.Key, Slot: ArmamentSlot.Armor) }).ToArray();
        var rows = slots.Select((entry, index) =>
        {
            var detachment = unit.Detachments[entry.Officer];
            var issue = entry.Slot == ArmamentSlot.Weapon ? detachment.Weapon : detachment.Armor;
            string name = issue is not null && Session.ArmamentDefinitions.TryGetValue(issue.DefinitionId, out var item) ? item.Name : "基础";
            string officer = Texts.Resolve(Session.View.Officers.Single(person => person.Id == entry.Officer).NameKey);
            return new CatalogRow(index, [officer, entry.Slot == ArmamentSlot.Weapon ? "武器" : "防具", name,
                $"{issue?.RemainingSets ?? 0}/{detachment.Soldiers}"]);
        }).ToArray();
        Replace(new CatalogView { Title = "调整军械", HintText = "选择分队和部位；确认后才会更换。", CloseTitle = "关闭",
            Textures = Textures, Service = Service, Pages = Slots, ReadRows = _ => rows, Closed = Closed,
            Activated = index => ShowChoices(slots[index].Officer, slots[index].Slot) });
    }
    private void ShowChoices(int officerId, ArmamentSlot slot)
    {
        var unit = Session.Snapshot.Units.FirstOrDefault(item => item.Id == UnitId);
        if (unit is null || !unit.Detachments.TryGetValue(officerId, out var detachment) || Session.View.PlayerForceId is not int forceId)
        { Closed(); return; }
        var force = Session.Snapshot.Forces.Single(item => item.Id == forceId);
        var previous = slot == ArmamentSlot.Weapon ? detachment.Weapon : detachment.Armor;
        var choices = Session.ArmamentDefinitions.Values.Where(item => item.Slot == slot && item.Supports(detachment.TroopTypeId))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        uint Available(ArmamentDefinition item)
        {
            ulong count = (force.ArmamentInventory?.GetValueOrDefault(item.Id) ?? 0) +
                (previous?.DefinitionId == item.Id ? (ulong)previous.RemainingSets : 0);
            return (uint)Math.Min((ulong)detachment.Soldiers, count);
        }
        var rows = new[] { new CatalogRow(0, ["基础军械", "—", previous is null ? "当前使用" : "回收原有军械"]) }
            .Concat(choices.Select((item, index) => new CatalogRow(index + 1, [item.Name, Available(item),
                item.ResearchProjectId is string project && !force.CompletedResearch.Contains(project) ? "尚未研究" :
                Available(item) == 0 ? "库存不足" : "可配发"]))).ToArray();
        Replace(new CatalogView { Title = slot == ArmamentSlot.Weapon ? "选择武器" : "选择防具",
            HintText = "选择军械；旧装备会先返回库存。", CloseTitle = "返回", Textures = Textures, Service = Service,
            Pages = Choices, ReadRows = _ => rows, Closed = ShowSlots, Activated = index =>
            {
                var choice = index == 0 ? null : choices[index - 1];
                var command = new ConfigureUnitArmamentCommand(Guid.NewGuid(), forceId, Session.Snapshot.Simulation.NextSequence,
                    UnitId, officerId, slot, choice?.Id);
                if (!Session.Validate(new("player"), command).Accepted)
                {
                    Notice("无法调整：请检查据点地域、部队状态、研究条件及库存。", () => ShowChoices(officerId, slot));
                    return;
                }
                string message = choice is null ? "改用基础军械，并回收现有装备？" : $"更换为{choice.Name}，配发 {Available(choice)} 套？";
                var confirm = new GameDialog { Textures = Textures, Size = Size };
                confirm.Dismissed += () => ShowChoices(officerId, slot);
                confirm.Confirmed += () =>
                {
                    var result = Session.Execute(new("player"), command with { CommandId = Guid.NewGuid(), Sequence = Session.Snapshot.Simulation.NextSequence });
                    if (result.Submission.Accepted) ShowSlots();
                    else Notice("调整失败，请重新检查条件和库存。", () => ShowChoices(officerId, slot));
                };
                Replace(confirm); confirm.SetMessage(message);
            } });
    }
    private void Notice(string message, Action back)
    {
        var notice = new GameDialog { Textures = Textures, Size = Size };
        notice.Closed += back; Replace(notice); notice.SetNotice(message);
    }
}
