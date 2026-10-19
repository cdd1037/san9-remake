using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Command navigation; the caller owns executable workflows.</summary>
public partial class MapCommandPopup : Control
{
    public Func<IReadOnlyList<OverlayBounds>> Obstacles { get; init; } = () => [];
    private IReadOnlyList<OverlayBounds> _lastObstacles = [];
    public TextureScope Textures { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public Action<string>? Activated { get; init; }
    private readonly List<(InformationMenuButton Button, MapCommandEntry Entry)> _primary = [], _secondary = [];
    private Rect2 _menu;
    private Rect2? _submenu;
    private Vector2 _art;
    private MapCommandSummary? _summary;
    private MapCommandEntry? _hovered, _illustrated;
    private double _hoverTime;
    private bool _conditionVisible;
    private Vector2 _anchor;
    private InformationMenuButton? _expandedButton;
    public string Hint => _hovered is {} entry ? Texts.Resolve(entry.UnavailableReasonKey ?? CommandMenuSkin.HintKey(entry.Id)) : "";

    public bool HintEnabled => _hovered?.Enabled ?? true;

    public override void _Ready()
    {
        Size = GetViewportRect().Size; ZIndex = 19; MouseFilter = MouseFilterEnum.Stop; Hide();
        GetViewport().SizeChanged += Relayout;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Relayout;

    private void Relayout()
    {
        Size = GetViewportRect().Size;
        _lastObstacles = Obstacles();
        if (_primary.Count == 0) return;
        var (x, y) = CommandMenuGeometry.Primary(_anchor.X, _anchor.Y, _primary.Count, Size.X, Size.Y);
        _menu = new(new(x, y), new(CommandMenuGeometry.PrimaryWidth, 32 + _primary.Count * 28));
        var fitted = OverlayLayout.Place(new(x, y, _menu.Size.X, _menu.Size.Y), new(268, 0, Math.Max(0, Size.X - 268), Size.Y), _lastObstacles);
        x = (float)fitted.X; y = (float)fitted.Y; _menu.Position = new(x, y);
        var (artX, artY) = CommandMenuGeometry.Illustration(x, y, Size.X); _art = new(artX, artY);
        for (int i = 0; i < _primary.Count; i++) _primary[i].Button.Position = new(x + 11, y + 16 + 28 * i);
        if (_expandedButton is not null) LayoutSubmenu();
        var occupied = _lastObstacles.Concat(new[] { new OverlayBounds(_menu.Position.X, _menu.Position.Y, _menu.Size.X, _menu.Size.Y) }).ToList();
        if (_submenu is { } sub) occupied.Add(new(sub.Position.X, sub.Position.Y, sub.Size.X, sub.Size.Y));
        var art = OverlayLayout.Place(new(_art.X, _art.Y, 188, 278), new(268, 0, Math.Max(0, Size.X - 268), Size.Y), occupied);
        _art = new((float)art.X, (float)art.Y);
        QueueRedraw();
    }

    private void LayoutSubmenu()
    {
        var (x, y) = CommandMenuGeometry.Secondary(_expandedButton!.Position.X, _expandedButton.Position.Y,
            _secondary.Count, Size.X, Size.Y);
        var fitted = OverlayLayout.Place(new(x, y, 68, _secondary.Count * 28), new(0, 0, Size.X, Size.Y),
            _lastObstacles.Concat(new[] { new OverlayBounds(_menu.Position.X, _menu.Position.Y, _menu.Size.X, _menu.Size.Y) }).ToArray());
        x = (float)fitted.X; y = (float)fitted.Y;
        _submenu = new(new(x, y), new(68, _secondary.Count * 28));
        for (int i = 0; i < _secondary.Count; i++) _secondary[i].Button.Position = new(x, y + 28 * i);
    }

    public void Open(IReadOnlyList<MapCommandEntry> entries, MapCommandSummary summary, Vector2 point)
    {
        Close();
        if (entries.Count == 0) return;
        _summary = summary;
        _anchor = point;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var button = AddButton(entry, Vector2.Zero, false);
            _primary.Add((button, entry));
        }
        Relayout(); Show();
    }

    private InformationMenuButton AddButton(MapCommandEntry entry, Vector2 point, bool secondary)
    {
        var button = new InformationMenuButton {
            Title = CommandText.Resolve(Texts, "title." + entry.Id), Disabled = !entry.Enabled, Textures = Textures, Position = point, Size = new(68, 28),
            SkinPrefix = $"command-{(secondary ? "secondary" : "primary")}-{entry.Tone.ToString().ToLowerInvariant()}"
        };
        button.Pressed += () =>
        {
            if (!entry.Enabled) return;
            if (entry.Children.Count > 0) Expand(entry, button);
            else Activated?.Invoke(entry.Id);
        };
        AddChild(button);
        return button;
    }

    private void Expand(MapCommandEntry entry, InformationMenuButton selected)
    {
        foreach (var item in _secondary) { RemoveChild(item.Button); item.Button.QueueFree(); }
        _secondary.Clear();
        _expandedButton = selected;
        for (int i = 0; i < entry.Children.Count; i++)
        {
            var child = entry.Children[i];
            var button = AddButton(child, Vector2.Zero, true);
            _secondary.Add((button, child));
        }
        Relayout();
    }

    public void Close()
    {
        Hide();
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _primary.Clear(); _secondary.Clear(); _submenu = null; _summary = null; _expandedButton = null;
        _hovered = _illustrated = null; _hoverTime = 0; _conditionVisible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        if (!_lastObstacles.SequenceEqual(Obstacles())) Relayout();
        var point = GetLocalMousePosition();
        var hovered = _secondary.Concat(_primary).FirstOrDefault(item => item.Button.GetRect().HasPoint(point)).Entry;
        if (_hovered != hovered)
        {
            _hovered = hovered; _hoverTime = 0; _conditionVisible = false;
            if (hovered is not null) _illustrated = hovered;
            QueueRedraw();
        }
        if (_hovered is { Children.Count: 0 } && !_conditionVisible)
        {
            _hoverTime += delta;
            if (_hoverTime >= 1) { _conditionVisible = true; QueueRedraw(); }
        }
    }

    public override void _Input(InputEvent input)
    {
        if (!Visible) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { Close(); GetViewport().SetInputAsHandled(); }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        {
            // Consume the release as well so dismissing cannot open the map's right-click menu.
            if (!mouse.Pressed) Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse &&
            !_menu.HasPoint(mouse.Position) && _submenu?.HasPoint(mouse.Position) != true) Close();
        AcceptEvent();
    }

    private void Text(string value, Vector2 point, int width, int size = 16, HorizontalAlignment alignment = HorizontalAlignment.Center)
    {
        var font = GetThemeFont("font");
        var style = GameTextStyles.For(size == 24 ? GameTextRole.Header : GameTextRole.DetailBody);
        float baseline = (size - font.GetHeight(size)) / 2 + font.GetAscent(size);
        float textWidth = font.GetStringSize(value, fontSize: size).X;
        float offset = alignment == HorizontalAlignment.Right ? width - textWidth : alignment == HorizontalAlignment.Center ? (width - textWidth) / 2 : 0;
        GameTextPresenter.Draw(this, font, value, point + new Vector2(offset, baseline), style);
    }

    public override void _Draw()
    {
        if (_summary is not {} summary) return;
        DrawTexture(Textures.Get("command-summary-" + summary.Kind, this), new(8, 120));
        Text(summary.Name, new(38, 142), 120, 24);
        Text(summary.Status, new(177, 145), 32);
        if (summary.Icon is {} icon) DrawTexture(Textures.Get("command-summary-" + icon, this), new(225, 142));
        foreach (var row in summary.Rows)
        {
            Text(row.Label, new(48, row.Y), 64);
            Text(row.Value, new(148, row.Y), 85, alignment: row.Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Center);
        }
        DrawTexture(Textures.Get("command-top", this), _menu.Position);
        for (int i = 0; i < _primary.Count; i++) DrawTexture(Textures.Get("command-middle", this), _menu.Position + new Vector2(0, 16 + i * 28));
        DrawTexture(Textures.Get("command-bottom", this), _menu.Position + new Vector2(0, _menu.Size.Y - 16));
        if (_illustrated is {} entry)
        {
            DrawTexture(Textures.Get("command-illustration-frame", this), _art);
            DrawTexture(Textures.Get("command-illustration-" + entry.Id, this), _art + new Vector2(14, 18));
            if (_conditionVisible && _hovered == entry)
            {
                var card = _art + new Vector2(8, 162);
                DrawTexture(Textures.Get("command-condition", this), card);
                string[] lines = Texts.Resolve(CommandMenuSkin.DescriptionKey(entry.Id)).Split('\n');
                for (int i = 0; i < lines.Length; i++) Text(lines[i], card + new Vector2(2, 6 + i * 16), 168, alignment: HorizontalAlignment.Left);
            }
        }
    }
}
