using System.Collections.Immutable;
using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Stages mixed detachment choices until the player confirms the entire preparation.</summary>
public partial class DeploymentDetachmentsView : ModalView, IModalInputRouter
{
    public DeploymentDraft Draft { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private ImmutableDictionary<int, DetachmentState> _detachments = ImmutableDictionary<int, DetachmentState>.Empty;
    private int? _naval, _siege;
    private Control? _body, _child;
    private string _message = "选择武将的兵种和兵力。";
    private readonly GameTextPresenter _text = new();

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _detachments = Draft.Detachments; _naval = Draft.NavalTrainingId; _siege = Draft.SiegeEquipmentId;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        _body.AddChild(new ColorRect { Color = new(0, 0, 0, .65f), Size = Size, MouseFilter = MouseFilterEnum.Ignore });
        _body.AddChild(new CommandWindow { Texts = Texts, Textures = Textures, Position = new(220, 140), Size = new(584, 480),
            Title = "分队兵种", AcceptTitle = "决定", CancelTitle = "中止", Accepted = Accept, Closed = Closed,
            Help = () => Notice("选择各武将的兵种和兵力。\n分队兵力合计须等于部队总兵力。") });
        void Label(string value, Rect2 rect, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var label = new Label { Text = value, Position = rect.Position, Size = rect.Size, HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
            _body.AddChild(label); _text.Apply(label, GameTextStyles.CatalogRow);
        }
        void Button(string title, Rect2 rect, Action action) => _body.AddChild(CommandWindow.CreateActionButton(Textures, title, "edit", rect, action));
        Label($"兵力合计 {_detachments.Values.Sum(d => (long)d.Soldiers)} / {Draft.Soldiers}　装备费用 {Draft.Rules.DetachmentEquipmentCost(_detachments.Values)}", new(248, 158, 520, 28));
        Label("武将", new(248, 200, 85, 26)); Label("兵种", new(342, 200, 115, 26)); Label("兵力", new(458, 200, 70, 26));
        if (!Draft.Rules.Armaments.IsEmpty) { Label("武器", new(540, 200, 90, 26)); Label("防具", new(644, 200, 90, 26)); }
        int index = 0;
        foreach (int officer in Draft.Members)
        {
            int y = 232 + index++ * 48;
            var detachment = _detachments[officer];
            Label(Texts.Resolve(Draft.View.Officers.Single(p => p.Id == officer).NameKey), new(248, y, 88, 32));
            string type = Draft.Rules.TroopTypes[detachment.TroopTypeId].Name;
            Button($"{type}{detachment.Level}级", new(338, y, 116, 40), () => SelectType(officer));
            Button(detachment.Soldiers.ToString(), new(458, y, 76, 40), () => SelectSoldiers(officer));
            if (!Draft.Rules.Armaments.IsEmpty)
            {
                string Name(ArmamentAllocation? allocation) => allocation is not null && Draft.Rules.Armaments.TryGetValue(allocation.DefinitionId, out var definition)
                    ? $"{definition.Name} {allocation.IssuedSets}" : "基础";
                Button(Name(detachment.Weapon), new(538, y, 98, 40), () => SelectArmament(officer, ArmamentSlot.Weapon));
                Button(Name(detachment.Armor), new(640, y, 98, 40), () => SelectArmament(officer, ArmamentSlot.Armor));
            }
        }
        if (Draft.NavalFormationId is not null)
            Button(_naval is int naval ? Draft.Rules.NavalTraining[naval].Name : "水军训练", new(256, 478, 192, 40), SelectNaval);
        Button(_siege is int siege ? Draft.Rules.SiegeEquipment[siege].Name : "无攻城器械", new(484, 478, 216, 40), SelectSiege);
        Label(_message, new(256, 550, 496, 42));
    }

    private static readonly CatalogPage[] ChoicePages = [new("选择", [
        new("name", "名称", 22, CatalogAlignment.Left), new("cost", "费用", 10, CatalogAlignment.Right),
        new("condition", "条件", 28, CatalogAlignment.Left)])];

    private void SelectType(int officer)
    {
        var choices = Draft.Rules.TroopTypes.Values.OrderBy(t => t.Id).SelectMany(t => Enumerable.Range(1, t.MaximumLevel)
            .Where(level => t.LevelResearchProjects.All(p => p.Key > level || Draft.Force.CompletedResearch.Contains(p.Value)))
            .Select(level => (Type: t, Level: level))).ToArray();
        OpenCatalog("选择兵种", choices.Select((c, i) => new CatalogRow(i, [$"{c.Type.Name}　{c.Level}级",
            Draft.Rules.Armaments.IsEmpty ? c.Type.EquipmentCostPerThousand.GetValueOrDefault(c.Level) : 0,
            Draft.Rules.Armaments.IsEmpty ? "每千人费用" : "基础军械"])).ToArray(), id =>
        { var c = choices[id]; _detachments = _detachments.SetItem(officer, _detachments[officer] with { TroopTypeId = c.Type.Id, Level = c.Level, Weapon = null, Armor = null }); CloseChild(); });
    }

    private void SelectSoldiers(int officer)
    {
        OpenChild(new NumberInputDialog { State = new(1, Draft.Soldiers, _detachments[officer].Soldiers), Textures = Textures,
            Confirmed = value => { _detachments = _detachments.SetItem(officer, _detachments[officer] with { Soldiers = value, Weapon = null, Armor = null }); CloseChild(); }, Closed = CloseChild });
    }

    private void SelectArmament(int officer, ArmamentSlot slot)
    {
        var current = _detachments[officer];
        uint Available(string id)
        {
            ulong reserved = 0;
            foreach (var (member, detachment) in _detachments)
            {
                if (detachment.Weapon is { } weapon && weapon.DefinitionId == id && !(member == officer && slot == ArmamentSlot.Weapon)) reserved += weapon.IssuedSets;
                if (detachment.Armor is { } armor && armor.DefinitionId == id && !(member == officer && slot == ArmamentSlot.Armor)) reserved += armor.IssuedSets;
            }
            uint stock = Draft.Force.ArmamentInventory?.GetValueOrDefault(id) ?? 0;
            return reserved >= stock ? 0 : stock - (uint)reserved;
        }
        var choices = Draft.Rules.Armaments.Values.Where(item => item.Slot == slot && item.Supports(current.TroopTypeId) && Available(item.Id) > 0)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        CatalogPage[] pages = [new("选择", [new("name", "名称", 22, CatalogAlignment.Left),
            new("stock", "库存", 12, CatalogAlignment.Right), new("coverage", "可配人数", 16, CatalogAlignment.Right)])];
        var rows = new[] { new CatalogRow(0, ["基础军械", "—", "—"]) }.Concat(choices.Select((item, index) =>
            new CatalogRow(index + 1, [item.Name, Available(item.Id), Math.Min(current.Soldiers, Available(item.Id))]))).ToArray();
        OpenChild(new CatalogView { Title = slot == ArmamentSlot.Weapon ? "选择武器" : "选择防具", CloseTitle = "中止",
            Pages = pages, ReadRows = _ => rows, Textures = Textures, Service = Service, Activated = index =>
            {
                ArmamentAllocation? selection = index == 0 ? null : new(choices[index - 1].Id,
                    Math.Min(current.Soldiers, Available(choices[index - 1].Id)), current.Soldiers, 0);
                _detachments = _detachments.SetItem(officer, slot == ArmamentSlot.Weapon ? current with { Weapon = selection } : current with { Armor = selection });
                CloseChild();
            }, Closed = CloseChild });
    }

    private void SelectNaval()
    {
        var choices = Draft.Rules.NavalTraining.Values.OrderBy(t => t.Id)
            .Where(t => t.ResearchProjectId is null || Draft.Force.CompletedResearch.Contains(t.ResearchProjectId)).ToArray();
        OpenCatalog("水军训练", choices.Select((c, i) => new CatalogRow(i, [c.Name, c.DeploymentCost, "已解锁"])).ToArray(),
            id => { _naval = choices[id].Id; CloseChild(); });
    }

    private void SelectSiege()
    {
        var choices = Draft.Rules.SiegeEquipment.Values.OrderBy(e => e.Id)
            .Where(e => Draft.Force.CompletedResearch.Contains(e.ResearchProjectId)).ToArray();
        var rows = new[] { new CatalogRow(0, ["无", "", ""]) }.Concat(choices.Select((c, i) =>
            new CatalogRow(i + 1, [c.Name, c.DeploymentCost, Draft.CanUseSiegeEquipment(c.Id) ? "可用" : "缺少兵法"]))).ToArray();
        OpenCatalog("攻城器械", rows, id =>
        { int? chosen = id == 0 ? null : choices[id - 1].Id; if (!Draft.CanUseSiegeEquipment(chosen)) { CloseChild(); Notice("需要具备相应兵法的武将。"); return; } _siege = chosen; CloseChild(); });
    }

    private void OpenCatalog(string title, CatalogRow[] rows, Action<int> activate) => OpenChild(new CatalogView
    { Title = title, CloseTitle = "中止", Pages = ChoicePages, ReadRows = _ => rows, Textures = Textures, Service = Service, Activated = activate, Closed = CloseChild });
    private void OpenChild(Control child) { _child = child; AddChild(child); }
    private void CloseChild() { if (_child is null) return; RemoveChild(_child); _child.QueueFree(); _child = null; Rebuild(); }
    private void Notice(string message)
    {
        var dialog = new GameDialog { Textures = Textures, Size = Size }; dialog.Closed += CloseChild; OpenChild(dialog); dialog.SetNotice(message);
    }
    private void Accept()
    {
        if (!Draft.CanUseSiegeEquipment(_siege) || _naval is int naval && (!Draft.Rules.NavalTraining.TryGetValue(naval, out var training) ||
            training.ResearchProjectId is string project && !Draft.Force.CompletedResearch.Contains(project)))
        { _message = "请检查水军训练与攻城器械条件。"; Rebuild(); return; }
        if (!Draft.SetDetachments(_detachments)) { _message = $"分队兵力合计须为 {Draft.Soldiers}。"; Rebuild(); return; }
        if (_naval is int selected) Draft.SetNavalTraining(selected);
        Draft.SetSiegeEquipment(_siege); Closed();
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || _child is not null) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
}
