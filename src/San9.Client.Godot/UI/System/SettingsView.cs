using Godot;

namespace San9.Client.Godot;

/// <summary>Shared settings layout and modal navigation; each page owns its draft and commit policy.</summary>
public abstract partial class SettingsView : ModalView
{
    public TextureScope Textures { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    protected abstract string Title { get; }
    protected abstract string[] Labels { get; }
    protected abstract int LabelWidth { get; }
    protected abstract int ColumnCount { get; }
    protected virtual int LabelFontSize => 16;
    protected virtual int ButtonFontSize => 24;
    protected abstract void BuildOptions();
    protected abstract void Confirm();
    private const int RowSpacing = 55;
    private readonly GameTextPresenter _text = new();
    private readonly List<(EnvironmentOptionButton Button, Func<bool> Selected)> _options = [];
    private GameDialog _notice = null!;
    private Rect2 _panel;
    private int _rowCount;
    public bool HasNotice => _notice?.Visible == true;
    public void Back() { if (HasNotice) _notice.Hide(); else Closed(); }

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        string[] labels = Labels;
        _rowCount = labels.Length;
        var panelSize = new Vector2(LabelWidth + ColumnCount * 96 + 63, 140 + _rowCount * RowSpacing);
        _panel = new(new(MathF.Floor((Size.X - panelSize.X) / 2), (Size.Y - panelSize.Y) / 2), panelSize);
        LabelAt(Title, new(13, 12), new(184, 48), 24);
        for (int row = 0; row < labels.Length; row++)
            LabelAt(labels[row], new(22, 88 + row * RowSpacing), new(LabelWidth, 40), LabelFontSize);
        BuildOptions();
        Button("yes", "执行", new(panelSize.X - 282, panelSize.Y - 60), new(112, 48), Confirm);
        Button("no", "中止", new(panelSize.X - 162, panelSize.Y - 60), new(112, 48), Closed);
        Button("close", "", new(panelSize.X - 44, 9), new(40, 32), Closed);
        _notice = new GameDialog { Textures = Textures, Size = Size, Visible = false };
        _notice.Closed += () => _notice.Hide(); AddChild(_notice);
        Refresh();
    }
    protected void ShowFailure(string message)
    {
        _notice.SetNotice(message); _notice.Show(); _notice.FocusDefaultButton();
    }
    protected void Option(string title, int column, int row, Action? choose, Func<bool> selected)
    {
        var button = new EnvironmentOptionButton { Textures = Textures, Title = title,
            Position = _panel.Position + new Vector2(LabelWidth + 30 + column * 96, 88 + row * RowSpacing),
            Size = new(96, 40), Disabled = choose is null };
        if (choose is not null) button.Pressed += () => { choose(); Refresh(); };
        AddChild(button); _options.Add((button, selected));
    }
    private void Refresh()
    {
        foreach (var (button, selected) in _options) button.Selected = selected();
    }
    private void LabelAt(string value, Vector2 at, Vector2 size, int fontSize)
    {
        var label = new Label { Text = value, Position = _panel.Position + at, Size = size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.Apply(label, new(fontSize, Colors.White, GameTextEffect.Outline, Colors.Black));
    }
    private void Button(string skin, string title, Vector2 at, Vector2 size, Action action)
    {
        var button = new SaveLoadButton { Textures = Textures, Skin = skin, Title = title,
            FontSize = ButtonFontSize, Position = _panel.Position + at, Size = size };
        button.Pressed += action; AddChild(button);
    }
    public override void _Draw()
    {
        var painter = new SaveLoadSkinPainter(this, Textures);
        painter.Background(_panel); painter.Frame(_panel, note: true);
        painter.Title(_panel.Position + new Vector2(13, 12), 184);
        for (int row = 0; row < _rowCount; row++)
            SkinPainter.Strip(this, Textures.Get("environment-label-left", this), Textures.Get("environment-label-middle", this),
                Textures.Get("environment-label-right", this), new(_panel.Position + new Vector2(22, 88 + row * RowSpacing), new(LabelWidth, 40)));
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() => _text.Dispose();
}
