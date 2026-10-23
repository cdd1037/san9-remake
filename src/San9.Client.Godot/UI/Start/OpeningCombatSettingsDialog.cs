using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Images;

namespace San9.Client.Godot;

/// <summary>Six-value editor. Only confirmation publishes its local draft to the opening page.</summary>
public partial class OpeningCombatSettingsDialog : Control
{
    internal static readonly (string Id, string Title, uint Maximum)[] Fields =
    [
        ("Money", "资金", 50000), ("Food", "兵粮", 500000), ("Domestic", "内政", 1000),
        ("Soldiers", "士兵", 100000), ("Morale", "士气", 100), ("Strength", "强度", 200)
    ];
    public TextureService Service { get; init; } = null!;
    public TextureScope LibraryTextures { get; init; } = null!;
    public StartMenuSprite Background { get; init; } = null!;
    public IReadOnlyDictionary<string, ImageAsset> Skin { get; init; } = null!;
    public int[] Initial { get; init; } = null!;
    public Action<int[]> Confirmed { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    private readonly GameTextPresenter _text = new();
    private readonly List<Label> _values = [];
    private readonly List<CatalogBulkButton> _editButtons = [];
    private int[] _draft = null!;
    private TextureScope _textures = null!;
    private TextureScope _skin = null!;
    private NumberInputDialog? _editor;
    private Vector2 Origin => new(Background.X, Background.Y);

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _draft = (int[])Initial.Clone();
        _textures = Service.CreateScope(Service.Roles);
        _textures.RequestPrefixes("deployment-number-", "message-");
        _skin = Service.CreateScope(Skin);
        _skin.RequestPrefixes("catalog-bulk-", "catalog-accept-", "information-action-");
        var title = new Label { Text = "战力设定", Position = Origin + new Vector2(36, 14), Size = new(104, 24),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(title); _text.Apply(title, OpeningSettingsTextStyles.DialogTitle);
        for (int i = 0; i < Fields.Length; i++)
        {
            int index = i;
            var button = new CatalogBulkButton { Name = Fields[i].Id, Title = Fields[i].Title, Textures = _skin,
                Position = Origin + new Vector2(43, 66 + i * 44), Size = new(96, 32), TextStyle = OpeningSettingsTextStyles.EditButton };
            button.Pressed += () => Edit(index); AddChild(button); _editButtons.Add(button);
            // The original text surface reserves a four-pixel right inset and two trailing spaces.
            var value = new Label { Position = Origin + new Vector2(110, 60 + i * 44), Size = new(124, 40),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore };
            AddChild(value); _text.Apply(value, OpeningSettingsTextStyles.DialogValue); _values.Add(value);
        }
        ActionButton("Confirm", "执行", new(145, 355), true, () => Confirmed(_draft));
        ActionButton("Cancel", "中止", new(232, 355), false, Closed);
        Icon("Help", "help", new(317, 0), () => { });
        Icon("Close", "close", new(352, 25), Closed);
        Refresh(); _editButtons[0].GrabFocus();
    }
    private void ActionButton(string name, string title, Vector2 position, bool confirm, Action action)
    {
        var button = new InformationActionButton { Name = name, Textures = _skin, Title = title,
            Position = Origin + position, Size = new(80, 48), SkinPrefix = confirm ? "catalog-accept-" : "information-action-",
            TextRole = confirm ? GameButtonTextRole.ConfirmYes : GameButtonTextRole.ConfirmNo };
        button.Pressed += action; AddChild(button);
    }
    private void Icon(string name, string role, Vector2 position, Action action)
    {
        var button = new TextureButton { Name = name, Position = Origin + position, Size = new(40, 32),
            TextureNormal = _textures.Get("deployment-number-" + role, this),
            TextureHover = _textures.Get("deployment-number-" + role + "-1", this),
            TexturePressed = _textures.Get("deployment-number-" + role + "-2", this) };
        button.Pressed += action; AddChild(button);
    }
    private void Edit(int index)
    {
        if (_editor is not null) return;
        foreach (var button in GetChildren().OfType<BaseButton>()) button.FocusMode = FocusModeEnum.None;
        _editor = new NumberInputDialog { Name = "NumberInput", Textures = _textures,
            Subject = Fields[index].Title, State = new(0, Fields[index].Maximum, (uint)_draft[index]),
            Closed = () => CloseEditor(index), Confirmed = value => { _draft[index] = (int)value; Refresh(); CloseEditor(index); } };
        AddChild(_editor);
    }
    private void CloseEditor(int index)
    {
        RemoveChild(_editor!); _editor!.QueueFree(); _editor = null;
        foreach (var button in GetChildren().OfType<BaseButton>()) button.FocusMode = FocusModeEnum.All;
        _editButtons[index].GrabFocus();
    }
    private void Refresh()
    {
        for (int i = 0; i < _values.Count; i++) _values[i].Text = $"+{_draft[i]}{(i == 5 ? "%" : "")}  ";
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || _editor is not null) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero, Size), new(0, 0, 0, .45f));
        if (LibraryTextures.TryGetReady(Background.Asset, this, out var texture)) DrawTexture(texture, Origin);
    }
    public override void _ExitTree() { _textures.Dispose(); _skin.Dispose(); _text.Dispose(); }
}
