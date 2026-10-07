using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Command navigation; the caller owns executable workflows.</summary>
public partial class MapCommandPopup : Control
{
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
    public string Hint => _hovered is {} entry ? Texts.Resolve(entry.UnavailableReasonKey ?? CommandMenuSkin.HintKey(entry.Id)) : "";

    public bool HintEnabled => _hovered?.Enabled ?? true;

    public override void _Ready()
    {
        Size = new(1024, 768); ZIndex = 19; MouseFilter = MouseFilterEnum.Stop; Hide();
    }

    public void Open(IReadOnlyList<MapCommandEntry> entries, MapCommandSummary summary, Vector2 point)
    {
        Close();
        if (entries.Count == 0) return;
        _summary = summary;
        var (x, y) = CommandMenuGeometry.Primary(point.X, point.Y, entries.Count);
        _menu = new(new(x, y), new(CommandMenuGeometry.PrimaryWidth, 32 + entries.Count * 28));
        var (artX, artY) = CommandMenuGeometry.Illustration(x, y); _art = new(artX, artY);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var button = AddButton(entry, new(x + 11, y + 16 + 28 * i), false);
            _primary.Add((button, entry));
        }
        Show(); QueueRedraw();
    }

    private InformationMenuButton AddButton(MapCommandEntry entry, Vector2 point, bool secondary)
    {
        var button = new InformationMenuButton {
            Title = entry.Title, Disabled = !entry.Enabled, Textures = Textures, Position = point, Size = new(68, 28),
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
        var (x, y) = CommandMenuGeometry.Secondary(selected.Position.X, selected.Position.Y, entry.Children.Count);
        _submenu = new(new(x, y), new(68, entry.Children.Count * 28));
        for (int i = 0; i < entry.Children.Count; i++)
        {
            var child = entry.Children[i];
            var button = AddButton(child, new(x, y + 28 * i), true);
            _secondary.Add((button, child));
        }
        QueueRedraw();
    }

    public void Close()
    {
        Hide();
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _primary.Clear(); _secondary.Clear(); _submenu = null; _summary = null;
        _hovered = _illustrated = null; _hoverTime = 0; _conditionVisible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
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
        DrawTexture(Textures["command-summary-" + summary.Kind], new(8, 120));
        Text(summary.Name, new(38, 142), 120, 24);
        Text(summary.Status, new(177, 145), 32);
        if (summary.Icon is {} icon) DrawTexture(Textures["command-summary-" + icon], new(225, 142));
        foreach (var row in summary.Rows)
        {
            Text(row.Label, new(48, row.Y), 64);
            Text(row.Value, new(148, row.Y), 85, alignment: row.Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Center);
        }
        DrawTexture(Textures["command-top"], _menu.Position);
        for (int i = 0; i < _primary.Count; i++) DrawTexture(Textures["command-middle"], _menu.Position + new Vector2(0, 16 + i * 28));
        DrawTexture(Textures["command-bottom"], _menu.Position + new Vector2(0, _menu.Size.Y - 16));
        if (_illustrated is {} entry)
        {
            DrawTexture(Textures["command-illustration-frame"], _art);
            DrawTexture(Textures["command-illustration-" + entry.Id], _art + new Vector2(14, 18));
            if (_conditionVisible && _hovered == entry)
            {
                var card = _art + new Vector2(8, 162);
                DrawTexture(Textures["command-condition"], card);
                string[] lines = Texts.Resolve(CommandMenuSkin.DescriptionKey(entry.Id)).Split('\n');
                for (int i = 0; i < lines.Length; i++) Text(lines[i], card + new Vector2(2, 6 + i * 16), 168, alignment: HorizontalAlignment.Left);
            }
        }
    }
}
