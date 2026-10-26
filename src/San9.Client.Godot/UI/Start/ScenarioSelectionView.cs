using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

public partial class ScenarioSelectionView : ModalView
{
    public ScenarioSelectionPresentation Definition { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public StartMenuSprite Help { get; init; } = null!;
    public StartMenuSprite Close { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action QuitRequested { get; init; } = null!;
    public Action<ScenarioPreviewEntry> Selected { get; init; } = null!;
    public ScenarioSelectionState State { get; private set; } = null!;
    public void FocusSelected() => _rows.GetNode<StartMenuButton>(State.Selected.Id).GrabFocus();
    private TextureScope _textures = null!;
    private readonly GameTextPresenter _text = new();
    private readonly Dictionary<string, ScenarioCategoryButton> _categories = [];
    private ScrollContainer _scroll = null!;
    private Control _rows = null!;
    private Label _heading = null!;
    private Label _hint = null!;
    private GameRichTextLabel _description = null!;
    private OpeningMapView _map = null!;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        State = new(Definition);
        _textures = Service.CreateScope(Service.LibraryAssets);
        _textures.Request(Definition.Assets.Concat([Help.Asset, Close.Asset]));
        _map = new OpeningMapView { Name = "PreviewMap", Definition = Definition.Map, Textures = _textures,
            CityColors = State.Selected.CityColors, Preview = Definition.Preview, Position = new(Definition.Preview.Background.X, Definition.Preview.Background.Y) };
        AddChild(_map);
        var panels = new Control { MouseFilter = MouseFilterEnum.Ignore };
        panels.Draw += () =>
        {
            if (!_textures.IsReady) { _textures.BindRedraw(panels); return; }
            foreach (var sprite in new[] { Definition.DescriptionPanel, Definition.CategoryBar, Definition.PreviewHeading })
                panels.DrawTexture(_textures.Get(sprite.Asset, panels), new(sprite.X, sprite.Y));
        };
        AddChild(panels);
        AddLabel(Definition.Title, new(48, 16), new(224, 32), 24, Colors.White);
        _heading = AddLabel("", new(548, 108), new(476, 24), 24, new("00f878"));
        _hint = AddLabel(Definition.Prompt, new(0, 736), new(1024, 24), 16, Colors.White, HorizontalAlignment.Center);
        _description = new GameRichTextLabel { Name = "Description", Textures = _textures,
            Position = new(602, 478), Size = new(400, 164), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_description);
        foreach (var category in Definition.Categories)
        {
            var button = new ScenarioCategoryButton { Name = "Category-" + category.Id, Textures = _textures,
                Highlight = Definition.CategoryHighlight, Title = category.Label,
                Position = new(category.Position.X, category.Position.Y), Size = new(144, 44) };
            button.Pressed += () => ChangeCategory(category.Id);
            button.MouseEntered += () => _hint.Text = category.Hint;
            button.MouseExited += () => _hint.Text = Definition.Prompt;
            _categories.Add(category.Id, button); AddChild(button);
        }
        _scroll = new ScrollContainer { Position = new(0, 180), Size = new(648, 420),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        AddChild(_scroll);
        var back = AddButton(Definition.Back, "Back", Definition.BackLabel, GameButtonTextRole.ConfirmNo);
        back.Pressed += Closed;
        AddButton(Help, "Help");
        AddButton(Close, "Close").Pressed += QuitRequested;
        BuildRows(); RefreshPreview();
    }
    private Label AddLabel(string text, Vector2 position, Vector2 size, int fontSize, Color color,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, Position = position, Size = size,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
        AddChild(label); _text.Apply(label, new(fontSize, color, GameTextEffect.Outline, Colors.Black));
        return label;
    }
    private StartMenuButton AddButton(StartMenuSprite sprite, string name, string title = "", GameButtonTextRole? textRole = null)
    {
        var image = Service.LibraryAssets[sprite.Asset];
        var button = new StartMenuButton { Name = name, Textures = _textures, Sprite = sprite, Title = title, TextRole = textRole,
            Position = new(sprite.X, sprite.Y), Size = new(image.Width / sprite.Columns, image.Height) };
        AddChild(button); return button;
    }
    private void BuildRows()
    {
        if (_rows is not null) { _scroll.RemoveChild(_rows); _rows.QueueFree(); }
        _rows = new Control { CustomMinimumSize = new(648, State.Entries.Count * 42) };
        _scroll.AddChild(_rows); _scroll.ScrollVertical = 0;
        StartMenuButton? first = null;
        for (int i = 0; i < State.Entries.Count; i++)
        {
            var entry = State.Entries[i];
            // Additional authored scenarios can scroll; the source skin does not limit collection size.
            var sprite = Definition.Rows[Math.Min(i, Definition.Rows.Length - 1)];
            var image = Service.LibraryAssets[sprite.Asset];
            var button = new StartMenuButton { Name = entry.Id, Textures = _textures, Sprite = sprite,
                Title = entry.Title, Position = new(0, i * 42), Size = new(image.Width / sprite.Columns, image.Height) };
            _rows.AddChild(button);
            var year = new Label { Text = $"{entry.Year}年", Position = new(52, 10), Size = new(48, 16),
                MouseFilter = MouseFilterEnum.Ignore };
            button.AddChild(year); _text.Apply(year, new(16, new Color("f8d860"), GameTextEffect.Outline, new("603000")));
            button.MouseEntered += () => button.GrabFocus();
            button.FocusEntered += () => Preview(entry.Id);
            button.Pressed += () => { Preview(entry.Id); Selected(entry); };
            first ??= button;
        }
        first?.GrabFocus();
    }
    private void ChangeCategory(string id)
    {
        if (!State.SelectCategory(id)) return;
        BuildRows(); RefreshPreview();
    }
    private void Preview(string id)
    {
        if (State.Preview(id)) RefreshPreview();
    }
    private void RefreshPreview()
    {
        var entry = State.Selected;
        _map.CityColors = entry.CityColors; _map.QueueRedraw();
        _heading.Text = $"{entry.Year}年{entry.Month}月 {entry.Title}";
        _description.Present(entry.Description, GameTextStyles.For(GameTextRole.Message));
        _description.AddThemeConstantOverride("line_separation", GameTextMetrics.LineSpacing(_description.GetThemeFont("normal_font"), 16) + 4);
        foreach (var category in _categories) category.Value.Selected = category.Key == State.CategoryId;
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (!_textures.IsReady) { _textures.BindRedraw(this); return; }
        void Draw(StartMenuSprite sprite) => DrawTexture(_textures.Get(sprite.Asset, this), new(sprite.X, sprite.Y));
        foreach (var sprite in Definition.Foreground) Draw(sprite);
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _ExitTree() { _textures.Dispose(); _text.Dispose(); }
}
