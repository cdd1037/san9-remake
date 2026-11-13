using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot;

/// <summary>Formation diagram and member cards. The caller owns selection and any deployment draft.</summary>
public partial class FormationBoardView : Control
{
    public FormationBoardData Data { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public Action<int>? OfficerActivated { get; init; }
    public Action<int, San9.Content.Scenarios.FormationSlot>? OfficerMoved { get; init; }
    private FormationMemberButton? _held;
    private Vector2 _grab, _start;
    private bool _dragging;
    private San9.Content.Scenarios.FormationSlot? _target;
    private Label _hint = null!;
    public override void _Ready()
    {
        Textures.Request(["formation-commander", "formation-member"]);
        if (Data.Presentation?.BackgroundRole is { } background) Textures.Request([background]);
        if (Data.Presentation?.AvailableRole is { } available) Textures.Request([available]);
        MouseFilter = OfficerMoved is null ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        int columns = Data.Presentation?.Columns ?? 3, rows = Data.Presentation?.Rows ?? 3;
        foreach (var group in Data.Members.Where(m => m.Position is not null).GroupBy(m => m.Position!.Value))
        {
            int index = 0;
            foreach (var member in group)
            {
                float cellWidth = Size.X / columns, cellHeight = Size.Y / rows;
                float height = Math.Min(80, cellHeight / group.Count());
                var rect = new Rect2(group.Key.Column * cellWidth + (cellWidth - Math.Min(120, cellWidth)) / 2,
                    group.Key.Row * cellHeight + index++ * height + 4, Math.Min(120, cellWidth), height);
                var button = new FormationMemberButton
                {
                    Position = rect.Position,
                    Size = rect.Size,
                    Member = member,
                    Textures = Textures
                };
                if (OfficerMoved is null) button.Pressed += () => OfficerActivated?.Invoke(member.OfficerId);
                else button.GuiInput += input =>
                {
                    if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
                    {
                        button.AcceptEvent();
                        if (mouse.DoubleClick) { OfficerActivated?.Invoke(member.OfficerId); return; }
                        _held = button; _grab = mouse.Position; _start = button.Position;
                    }
                };
                AddChild(button);
            }
        }
        _hint = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 3 };
        _hint.AddThemeColorOverride("font_color", Colors.Black);
        _hint.AddThemeFontSizeOverride("font_size", 16);
        _hint.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("f8e890") });
        AddChild(_hint);
    }

    public override void _Input(InputEvent input)
    {
        if (_held is null) return;
        if (input is InputEventMouseMotion)
        {
            var at = GetLocalMousePosition() - _grab;
            if (!_dragging && at.DistanceTo(_start) < 4) return;
            _dragging = true; _hint.Hide(); _held.ZIndex = 2; _held.Highlighted = true;
            _held.Position = at;
            _target = null; float best = 0;
            if (Data.Presentation is { } p)
                foreach (var slot in p.Slots)
                {
                    var cell = new Rect2(slot.Column * Size.X / p.Columns, slot.Row * Size.Y / p.Rows, Size.X / p.Columns, Size.Y / p.Rows);
                    var overlap = cell.Intersection(new(at, _held.Size));
                    float area = overlap.HasArea() ? overlap.Size.X * overlap.Size.Y : 0;
                    if (area > best) { best = area; _target = slot; }
                }
            QueueRedraw(); GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) FinishDrag(true);
        else if (input is InputEventKey { Keycode: Key.Escape, Pressed: true }) FinishDrag(false);
    }

    private void FinishDrag(bool commit)
    {
        var held = _held!; var target = _target;
        held.Position = _start; held.ZIndex = 0; held.Highlighted = false;
        bool moved = _dragging && commit && target is not null;
        bool tacticClicked = !_dragging && commit && held.IsTacticPoint(_grab) &&
            held.IsTacticPoint(held.GetLocalMousePosition());
        _held = null; _target = null; _dragging = false; QueueRedraw();
        GetViewport().SetInputAsHandled();
        if (moved) OfficerMoved?.Invoke(held.Member.OfficerId, target!.Value);
        else if (tacticClicked) OfficerActivated?.Invoke(held.Member.OfficerId);
    }

    public override void _Process(double delta)
    {
        if (OfficerMoved is null || _dragging) { _hint.Hide(); return; }
        var mouse = GetLocalMousePosition();
        var hovered = GetChildren().OfType<FormationMemberButton>().LastOrDefault(b => b.IsHovered());
        foreach (var button in GetChildren().OfType<FormationMemberButton>()) button.Highlighted = button == hovered;
        _hint.Visible = hovered is not null && hovered.Member.HoverText.Length > 0;
        if (_hint.Visible) { _hint.Text = hovered!.Member.HoverText; _hint.Position = mouse + new Vector2(8, 8); }
    }
    public override void _Draw()
    {
        var presentation = Data.Presentation;
        if (presentation?.BackgroundRole is { } role) DrawTextureRect(Textures.Get(role, this), new(Vector2.Zero, Size), false);
        else DrawRect(new(Vector2.Zero, Size), new Color("171b1b"));
        int columns = presentation?.Columns ?? 3, rows = presentation?.Rows ?? 3;
        if (presentation?.AvailableRole is { } available)
            foreach (var slot in presentation.Slots)
            {
                var texture = Textures.Get(available, this);
                DrawTextureRectRegion(texture, new(slot.Column * Size.X / columns, slot.Row * Size.Y / rows, Size.X / columns, Size.Y / rows),
                    new(slot.Column * texture.GetWidth() / columns, slot.Row * texture.GetHeight() / rows, texture.GetWidth() / columns, texture.GetHeight() / rows));
            }
        else if (presentation is not null)
            foreach (var slot in presentation.Slots)
                DrawRect(new(slot.Column * Size.X / columns, slot.Row * Size.Y / rows, Size.X / columns, Size.Y / rows),
                    new Color(0.45f, 0.39f, 0.22f, 0.45f));
        for (int x = 1; x < columns; x++) DrawLine(new(x * Size.X / columns, 0), new(x * Size.X / columns, Size.Y), new Color(0,0,0,.5f));
        for (int y = 1; y < rows; y++) DrawLine(new(0, y * Size.Y / rows), new(Size.X, y * Size.Y / rows), new Color(0,0,0,.5f));
        if (_target is { } target)
            DrawRect(new(target.Column * Size.X / columns, target.Row * Size.Y / rows, Size.X / columns, Size.Y / rows), new Color(0.1f, 0.9f, 0.65f, 0.3f));
    }
}

/// <summary>Text follows the two interiors of the native 120×80 member plaque.</summary>
public partial class FormationMemberButton : BaseButton
{
    private static readonly Rect2 TacticInterior = new(24, 8, 72, 28);
    public FormationMemberView Member { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    private bool _highlighted;
    public bool Highlighted { get => _highlighted; set { if (_highlighted == value) return; _highlighted = value; QueueRedraw(); ApplyText(); } }
    private readonly List<Label> _labels = [];
    private readonly GameTextPresenter _text = new();
    public bool IsTacticPoint(Vector2 point) =>
        TacticInterior.HasPoint(point * new Vector2(120, 80) / Size);
    public override void _Draw() => DrawTextureRectRegion(Textures.Get(Member.IsCommander ? "formation-commander" : "formation-member", this), new(Vector2.Zero, Size), new(Highlighted ? 120 : 0, 0, 120, 80));

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        AddText(Member.Tactic, TacticInterior);
        AddText(Member.Name, new(8, 42, 104, 28));
    }

    private void AddText(string text, Rect2 interior)
    {
        var scale = Size / new Vector2(120, 80);
        var label = new Label {
            Text = text, Position = interior.Position * scale, Size = interior.Size * scale,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            ClipText = true, MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(label);
        _labels.Add(label); ApplyText();
    }

    private void ApplyText()
    {
        var scale = Size / new Vector2(120, 80);
        foreach (var label in _labels) _text.Apply(label, (label == _labels[0] && !Member.TacticAvailable
            ? GameTextStyles.CatalogDisabledRow : GameTextStyles.CatalogRow) with {
            FontSize = Math.Max(1, (int)(24 * Math.Min(scale.X, scale.Y))),
            EffectColor = Highlighted ? new Color("00f098") : Colors.Black });
    }

    public override void _ExitTree() => _text.Dispose();
}
