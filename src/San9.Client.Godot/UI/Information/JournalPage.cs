using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Shared journal chrome and navigation; each page owns its data and rows.</summary>
public abstract partial class JournalPage : FixedPage, IInformationPage
{
    protected override Color PageBackground => Colors.Black;
    public TextureScope Textures { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public Func<string, string> Hint { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    protected abstract string Topic { get; }
    protected abstract int Offset { get; }
    protected abstract int RowCount { get; }
    protected abstract void Scroll(int offset);
    public abstract void RefreshData();
    private readonly GameTextPresenter _text = new();
    private GameMessageBar _message = null!;
    private SkinnedScrollBar _scroll = null!;
    private Control? _help;

    protected void InitializeJournal(float scrollX)
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _message = new GameMessageBar { Textures = Textures, Text = Hint(Topic) }; AddChild(_message);
        AddAction("关闭", "close", new(875, 687), new(108, 44), Closed, "close");
        Icon("close-icon", new(964, 56), Closed);
        Icon("help", new(934, 33), ToggleHelp);
        _scroll = new SkinnedScrollBar { Skin = ScrollBarSkin.FromCatalog(Textures), Position = new(scrollX, 116), Size = new(16, 552), ContextRequested = Closed };
        AddChild(_scroll);
        _scroll.Range.Page = ProgressRecordHistory.VisibleRows;
        _scroll.Range.ValueChanged += value => Scroll((int)value);
    }

    protected void RefreshScroll()
    {
        _scroll.Range.MaxValue = Math.Max(ProgressRecordHistory.VisibleRows, RowCount);
        _scroll.Visible = RowCount > ProgressRecordHistory.VisibleRows;
    }

    protected void SyncScroll()
    {
        _scroll.Range.SetValueNoSignal(Offset);
        _scroll.UpdateArrows();
    }

    protected Label LabelAt(string value, Vector2 at, Vector2 size, int fontSize = 16, Control? parent = null)
    {
        var label = new Label { Text = value, Position = at, Size = size, ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
        (parent ?? this).AddChild(label);
        _text.Apply(label, GameTextStyles.CatalogRow with { FontSize = fontSize }); return label;
    }

    protected DetailActionButton AddAction(string title, string role, Vector2 at, Vector2 size, Action? pressed, string hint)
    {
        var button = new DetailActionButton { Title = title, Role = role, Textures = Textures, Position = at, Size = size, Disabled = pressed is null };
        TrackHint(button, hint);
        if (pressed is not null) button.Pressed += pressed;
        AddChild(button); return button;
    }

    private void Icon(string role, Vector2 at, Action pressed)
    {
        var button = new TextureButton { Position = at, TextureNormal = Textures.Get("detail-" + role, this), TextureHover = Textures.Get("detail-" + role + "-1", this), TexturePressed = Textures.Get("detail-" + role + "-2", this) };
        TrackHint(button, Topic);
        button.Pressed += pressed; AddChild(button);
    }

    private void TrackHint(BaseButton button, string role)
    {
        button.MouseEntered += () => _message.SetMessage(Hint(role), !button.Disabled);
        button.MouseExited += () => _message.SetMessage(Hint(Topic));
        button.FocusEntered += () => _message.SetMessage(Hint(role), !button.Disabled);
        button.FocusExited += () => _message.SetMessage(Hint(Topic));
    }

    private void ToggleHelp()
    {
        if (_help is not null) { RemoveChild(_help); _help.QueueFree(); _help = null; return; }
        _help = new JournalHelpView { Textures = Textures, Hint = Hint, Topic = Topic, Closed = ToggleHelp }; AddChild(_help);
    }

    private void CloseTop() { if (_help is not null) ToggleHelp(); else Closed(); }

    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.Escape) CloseTop();
            else
            {
                if (_help is not null) return;
                int? offset = key.Keycode switch { Key.Up => Offset - 1, Key.Down => Offset + 1,
                    Key.Pageup => Offset - ProgressRecordHistory.VisibleRows, Key.Pagedown => Offset + ProgressRecordHistory.VisibleRows,
                    Key.Home => 0, Key.End => RowCount, _ => null };
                if (offset is not int target) return;
                Scroll(target);
            }
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); CloseTop(); }
    }

    public override void _GuiInput(InputEvent input)
    {
        if (_help is null && input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp)
            Scroll(Offset + (mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1));
        AcceptEvent();
    }

    public override void _ExitTree() => _text.Dispose();
}
