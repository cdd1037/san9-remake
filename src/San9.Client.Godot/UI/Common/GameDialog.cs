using Godot;

namespace San9.Client.Godot;

/// <summary>Reusable modal message/confirmation dialog. Its caller owns the outcome.</summary>
public partial class GameDialog : Control
{
    public TextureScope Textures { get; init; } = null!;
    public event Action? Confirmed;
    public event Action? Dismissed;
    public event Action? Closed;
    private float _preferredWidth = 384;
    public float PreferredWidth
    {
        get => _preferredWidth;
        set
        {
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            _preferredWidth = value;
            QueueLayout();
        }
    }
    private PanelContainer _panel = null!;
    private ScrollContainer _body = null!;
    private Label _message = null!;
    private StrategyButton _confirm = null!, _dismiss = null!;
    private StyleBoxTexture _style = null!;
    private int _lineOverflow, _bodyGap;
    private bool _ready, _layoutQueued;
    private (string Message, string? Confirm, string? Dismiss, bool Notice)? _content;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _style = new StyleBoxTexture
        {
            Texture = Textures["dialog-frame"],
            TextureMarginLeft = 96, TextureMarginRight = 96,
            TextureMarginTop = 56, TextureMarginBottom = 56,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
            ContentMarginLeft = 56, ContentMarginRight = 56,
            ContentMarginTop = 40, ContentMarginBottom = 40
        };
        _panel = new PanelContainer();
        _panel.AddThemeStyleboxOverride("panel", _style);
        center.AddChild(_panel);
        var column = new VBoxContainer();
        var font = GetThemeFont("font");
        int lineSpacing = GameTextMetrics.LineSpacing(font, 24);
        _lineOverflow = Math.Max(0, -lineSpacing);
        _bodyGap = Math.Max(0, 16 - _lineOverflow);
        column.AddThemeConstantOverride("separation", _bodyGap);
        _panel.AddChild(column);
        _body = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        column.AddChild(_body);
        // Keep complete glyph metrics inside the scroll clip. Only baseline spacing is 24px;
        // the last line's descent occupies part of the gap before the buttons.
        _message = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { Font = font, FontSize = 24, LineSpacing = lineSpacing, FontColor = Colors.Black }
        };
        _body.AddChild(_message);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        column.AddChild(buttons);
        _confirm = Button("yes", GameButtonTextRole.ConfirmYes, () =>
        {
            if (_content?.Notice == true) Closed?.Invoke();
            else Confirmed?.Invoke();
        });
        _dismiss = Button("no", GameButtonTextRole.ConfirmNo, () => Dismissed?.Invoke());
        buttons.AddChild(_confirm); buttons.AddChild(_dismiss);
        Resized += QueueLayout;
        _message.MinimumSizeChanged += QueueLayout;
        _ready = true;
        ApplyContent();
        QueueLayout();
    }

    public void SetMessage(string message, string? confirm = "是", string? dismiss = "否")
    {
        var content = (message, confirm, dismiss, false);
        if (_content == content) return;
        _content = content;
        if (_ready) ApplyContent();
    }

    /// <summary>Acknowledgement only: one green close button and content-sized width.</summary>
    public void SetNotice(string message)
    {
        var content = (message, (string?)"关闭", (string?)null, true);
        if (_content == content) return;
        _content = content;
        if (_ready) ApplyContent();
    }

    public void FocusDefaultButton() => _confirm.GrabFocus();

    private void ApplyContent()
    {
        var content = _content ?? ("", "是", "否", false);
        _message.Text = content.Item1;
        _confirm.Visible = content.Item2 is not null;
        _confirm.Title = content.Item2 ?? "";
        _dismiss.Visible = content.Item3 is not null;
        _dismiss.Title = content.Item3 ?? "";
        _body.ScrollVertical = 0;
        QueueLayout();
    }

    private StrategyButton Button(string kind, GameButtonTextRole role, Action action)
    {
        var frames = Enumerable.Range(0, 4).Select(frame => Textures[$"dialog-{kind}-{frame}"]).ToArray();
        var button = new StrategyButton { Frames = frames, TextRole = role, CustomMinimumSize = frames[0].GetSize() };
        button.Pressed += action;
        return button;
    }

    private void QueueLayout()
    {
        if (!_ready || _layoutQueued) return;
        _layoutQueued = true;
        Callable.From(UpdateLayout).CallDeferred();
    }

    private void UpdateLayout()
    {
        _layoutQueued = false;
        if (!IsInsideTree()) return;
        float buttonsWidth = (_dismiss.Visible ? _dismiss.CustomMinimumSize.X : 0) +
            (_confirm.Visible ? _confirm.CustomMinimumSize.X : 0) + (_confirm.Visible && _dismiss.Visible ? 16 : 0);
        float minWidth = Math.Max(192, buttonsWidth + 112);
        float preferred = PreferredWidth;
        if (_content?.Notice == true)
        {
            float textWidth = _message.Text.Split('\n').Max(line => _message.LabelSettings.Font.GetStringSize(line, fontSize: 24).X);
            // Measure modern glyphs, then fit the native 24px panel tiles; never count source bytes.
            preferred = 192 + 24 * MathF.Ceiling(Math.Max(0, Math.Max(textWidth, buttonsWidth) - 80) / 24);
        }
        float width = Math.Max(minWidth, Math.Min(preferred, Size.X - 32));
        _panel.CustomMinimumSize = new(width, 184);
        // Content margins are independent of the wider decorative nine-patch corners.
        float minBodyHeight = 48 + _lineOverflow;
        float maxBodyHeight = Math.Max(minBodyHeight, Size.Y - 32 - 80 - _bodyGap - _dismiss.CustomMinimumSize.Y);
        _body.CustomMinimumSize = new(width - 112, Math.Clamp(_message.GetMinimumSize().Y, minBodyHeight, maxBodyHeight));
    }

    public override void _GuiInput(InputEvent @event) => AcceptEvent();
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsVisibleInTree() || _content?.Notice != true) return;
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape or Key.Enter or Key.KpEnter })
        {
            GetViewport().SetInputAsHandled();
            Closed?.Invoke();
        }
    }
    public override void _ExitTree() { _ready = false; _style?.Dispose(); }
}
