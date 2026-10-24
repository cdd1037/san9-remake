using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>A retained record owns its row until it leaves the immediate ten-record viewport.</summary>
internal partial class MapRecordRow : Control
{
    public TextureScope Textures { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public Action<MapCell> LocationRequested { get; init; } = null!;
    public MapMessageRecord? Entry { get; private set; }
    private GameRichTextLabel _label = null!;
    private ColorRect _flag = null!, _flagColor = null!;
    private string _markup = "", _style = "";
    private bool _hovered, _pressed;

    public override void _Ready()
    {
        Size = new(376, 16); MouseFilter = MouseFilterEnum.Stop;
        _label = new() { Textures = Textures, Position = new(16, 0), Size = new(360, 16),
            MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        AddChild(_label);
        _flag = new() { Position = new(2, 2), Size = new(12, 12), Color = new("080808"), MouseFilter = MouseFilterEnum.Ignore };
        _flagColor = new() { Position = new(3, 3), Size = new(10, 10), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_flag); AddChild(_flagColor);
        MouseEntered += () => { _hovered = true; Style("map-record-hover"); };
        MouseExited += () => { _hovered = _pressed = false; Style("map-record-normal"); };
    }

    public void Bind(MapMessageRecord entry)
    {
        if (ReferenceEquals(Entry, entry)) return;
        Entry = entry; _pressed = false;
        _markup = entry.Message.BbCode.Replace('\n', ' '); _style = "";
        MouseDefaultCursorShape = entry.Location is null ? CursorShape.Arrow : CursorShape.PointingHand;
        _flag.Visible = _flagColor.Visible = entry.ForceColorCode is not null;
        if (entry.ForceColorCode is int color) _flagColor.Color = new(Skin.Colors[$"overview-force-{color}"]);
        Style(_hovered ? "map-record-hover" : "map-record-normal");
    }

    public void Release()
    {
        Entry = null; _hovered = _pressed = false; Hide();
    }

    private void Style(string role)
    {
        if (Entry is null || _style == role) return;
        _style = role;
        _label.Present(_markup, new(12, new Color(Skin.Colors[role]), GameTextEffect.Outline, Colors.Black),
            San9.Rendering.MessageEmphasisMode.Foreground);
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse || Entry is null) return;
        bool clicked = _pressed && !mouse.Pressed;
        _pressed = mouse.Pressed;
        Style(_pressed ? "map-record-pressed" : "map-record-hover");
        if (clicked && Entry.Location is { } location) LocationRequested(location);
        AcceptEvent();
    }
}
