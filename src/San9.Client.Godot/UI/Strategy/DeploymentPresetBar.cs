using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Text;
using static San9.Client.Godot.DeploymentPresetStyle;

namespace San9.Client.Godot;

/// <summary>A view of the campaign's presets; shortcut slots own no saved state or resource reservations.</summary>
public partial class DeploymentPresetBar : Control
{
    public DeploymentDraft Draft { get; init; } = null!;
    public IReadOnlyList<DeploymentPreset> Presets { get; init; } = [];
    public TextCatalog Texts { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public int InitialScroll { get; init; }
    public Action<DeploymentPreset> Activated { get; init; } = null!;
    public Action SaveRequested { get; init; } = null!;
    public Action<DeploymentPreset> UpdateRequested { get; init; } = null!;
    public int ScrollPosition => _scroll?.ScrollVertical ?? InitialScroll;
    private ScrollContainer? _scroll;
    private readonly GameTextPresenter _text = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var background = new Panel { Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        background.AddThemeStyleboxOverride("panel", Surface("111e2d", "566273")); AddChild(background);
        Text(this, "快捷编队", new(0, 0, Size.X, 24), 14, Gold);
        const int saveHeight = 28;
        float saveTop = Size.Y - 4 - saveHeight;
        _scroll = new ScrollContainer
        {
            Position = new(4, 28), Size = new(Size.X - 8, saveTop - 6 - 28),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        AddChild(_scroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 6); _scroll.AddChild(rows);
        var presets = Presets.Where(p => p.ForceId == Draft.Force.Id)
            .Select(p => (Preset: p, Check: Draft.CheckPreset(p)))
            .OrderByDescending(p => p.Check.CanApply);
        foreach (var (preset, check) in presets) AddPreset(rows, preset, check);
        var save = Slot(this, saveHeight, false, false);
        save.Position = new((Size.X - 64) / 2, saveTop);
        save.Size = new(64, saveHeight);
        save.Disabled = !Draft.CanProceed;
        save.TooltipText = Draft.CanProceed
            ? "保存当前编队，使用当前兵力。\n名称、兵力偏好及其他管理可在“常用编队”中调整。"
            : "完成武将、阵形和兵力配置后，可保存当前编队。";
        Text(save, "＋ 保存", new(0, 0, 64, saveHeight), 14, Draft.CanProceed ? Gold : Muted);
        save.Pressed += () => SaveRequested();
        CallDeferred(nameof(RestoreScroll));
    }

    private void RestoreScroll() => _scroll!.ScrollVertical = InitialScroll;

    private void AddPreset(Control rows, DeploymentPreset preset, DeploymentPresetCheck check)
    {
        int? commander = Draft.PresetCommanderId(preset);
        bool source = preset.Id == Draft.SourcePresetId;
        bool modified = source && Draft.HasPresetChanges;
        var group = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        group.AddThemeConstantOverride("separation", 2); rows.AddChild(group);
        var slot = Slot(group, 90, source && !modified, check.HasMissingMembers || modified);
        slot.Disabled = commander is null;
        string name = commander is int id ? Person(id) : "无可用主将";
        string soldiers = preset.SoldierMode == DeploymentSoldierMode.Maximum ? "当前最大兵力" : $"{preset.Soldiers}人";
        string formation = Draft.View.Information.Formations.FirstOrDefault(f => f.Id == preset.FormationId) is { } info
            ? info.Name : "无";
        string status = check.DescribeAvailability(preset, Draft.View, Texts);
        slot.TooltipText = $"{preset.Name}\n主将：{name}\n{preset.Members.Length - check.MissingMembers.Length}/{preset.Members.Length}将 · {formation} · {soldiers}\n{status}";
        if (modified) slot.TooltipText += "\n当前配置已修改；点击下方“更新”覆盖此编队。";
        if (commander is int person)
            slot.AddChild(new AsyncPortrait
            {
                Service = Textures.Service, Role = $"catalog-person-{person}", View = San9.Content.Images.PortraitView.Face,
                Position = new(8, 6), Size = new(48, 60), MouseFilter = MouseFilterEnum.Ignore
            });
        else Text(slot, "—", new(8, 6, 48, 60), 24, Muted);
        Text(slot, preset.Name, new(3, 66, 58, 22), 14,
            commander is null ? Muted : check.HasMissingMembers || modified ? Warning : source ? Gold : Ink);
        slot.Pressed += () => Activated(preset);
        if (modified)
        {
            var update = Slot(group, 28, false, true);
            update.Disabled = !Draft.CanProceed;
            update.TooltipText = $"用当前出征配置更新“{preset.Name}”，保留名称和兵力偏好。";
            Text(update, "更新", new(0, 0, 64, 28), 14, Draft.CanProceed ? Gold : Muted);
            update.Pressed += () => UpdateRequested(preset);
        }
    }

    private static Button Slot(Control rows, int height, bool applied, bool missing)
    {
        var slot = new Button
        {
            CustomMinimumSize = new(64, height), SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None
        };
        StyleButton(slot, warning: missing, selected: applied);
        rows.AddChild(slot); return slot;
    }
    private string Person(int id) => Texts.Resolve(Draft.View.Officers.Single(o => o.Id == id).NameKey);
    private void Text(Control parent, string value, Rect2 bounds, int size, Color color)
    {
        var label = new Label
        {
            Text = value, Position = bounds.Position, Size = bounds.Size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, ClipText = true
        };
        parent.AddChild(label); _text.Apply(label, new(size, color, GameTextEffect.None, Colors.Transparent));
    }
    public override void _ExitTree() => _text.Dispose();
}
