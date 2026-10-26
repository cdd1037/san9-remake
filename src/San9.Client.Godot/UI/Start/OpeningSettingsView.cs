using Godot;
using San9.Core.Domain;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Pre-session settings draft; returning to force selection retains this page and its values.</summary>
public partial class OpeningSettingsView : ModalView
{
    public ScenarioSelectionPresentation Definition { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public StartMenuSprite Help { get; init; } = null!;
    public StartMenuSprite Close { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action QuitRequested { get; init; } = null!;
    public Func<NewGameSettings, int, Task> Confirmed { get; init; } = null!;
    private bool _creating;
    private Label _hint = null!;
    public bool Advanced { get; private set; } = true;
    public bool Fictional { get; private set; }
    private int[] _values = new int[6];
    private readonly List<Label> _summary = [];
    private readonly GameTextPresenter _text = new();
    private TextureScope _textures = null!;
    private OpeningCombatSettingsDialog? _dialog;
    private readonly List<(OpeningSettingButton Button, Func<bool> Selected)> _choices = [];
    private double _elapsed;
    public int MoneyBonus => _values[0];
    public int FoodBonus => _values[1];
    public int DomesticBonus => _values[2];
    public int SoldierBonus => _values[3];
    public int MoraleBonus => _values[4];
    public int DamageBonusPercent => _values[5];
    private OpeningSettingsPresentation Art => Definition.Settings;

    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _textures = Service.CreateScope(Service.LibraryAssets);
        _textures.Request(Art.Assets.Concat([Help.Asset, Close.Asset]));
        Label("开局设定", new(48, 16), new(224, 32), OpeningSettingsTextStyles.Heading);
        Label("难度", new(100, 156), new(360, 24), OpeningSettingsTextStyles.Field);
        Choice("Beginner", "初级", new(80, 175), () => Advanced = false, () => !Advanced);
        Choice("Advanced", "高级", new(200, 175), () => Advanced = true, () => Advanced);
        Label("模式", new(100, 240), new(360, 24), OpeningSettingsTextStyles.Field);
        Choice("Historical", "史实", new(80, 259), () => Fictional = false, () => !Fictional);
        Choice("Fictional", "虚拟", new(200, 259), () => Fictional = true, () => Fictional);
        Button(Art.Combat, "Combat", "电脑的战力设定", EditCombat, textStyle: OpeningSettingsTextStyles.Combat);
        for (int i = 0; i < OpeningCombatSettingsDialog.Fields.Length; i++)
        {
            int x = i < 3 ? 118 : 298, y = 384 + i % 3 * 30;
            Label(OpeningCombatSettingsDialog.Fields[i].Title, new(x, y), new(72, 24), OpeningSettingsTextStyles.Field);
            _summary.Add(Label("", new(x + 56, y), new(108, 24), OpeningSettingsTextStyles.Value, HorizontalAlignment.Right));
        }
        Button(Art.Confirm, "Confirm", "决定", Confirm, GameButtonTextRole.ConfirmYes, OpeningSettingsTextStyles.Confirm);
        Button(Art.Back, "Back", "返回", Closed, GameButtonTextRole.ConfirmNo);
        Button(Help, "Help", "", null);
        Button(Close, "Close", "", QuitRequested);
        _hint = Label("设定游戏环境。", new(0, 736), new(1024, 24), GameTextStyles.For(GameTextRole.Message), HorizontalAlignment.Center);
        Refresh();
    }
    private async void Confirm()
    {
        if (_creating) return;
        _creating = true;
        foreach (var button in GetChildren().OfType<BaseButton>()) button.Disabled = true;
        _hint.Text = "正在创建新游戏…";
        try
        {
            await Confirmed(new NewGameSettings
            {
                Difficulty = Advanced ? GameDifficulty.Advanced : GameDifficulty.Beginner,
                Mode = Fictional ? GameMode.Fictional : GameMode.Historical,
                MoneyBonus = MoneyBonus, FoodBonus = FoodBonus, DomesticBonus = DomesticBonus,
                SoldierBonus = SoldierBonus, MoraleBonus = MoraleBonus
            }, DamageBonusPercent);
        }
        catch (OperationCanceledException) when (!IsInstanceValid(this) || !IsInsideTree()) { }
        catch (Exception error)
        {
            GD.PushError($"创建新游戏失败：{error}");
            if (IsInstanceValid(this) && IsInsideTree()) _hint.Text = $"创建失败：{error.Message}。请重试。";
        }
        finally
        {
            if (IsInstanceValid(this) && IsInsideTree())
            {
                _creating = false;
                foreach (var button in GetChildren().OfType<BaseButton>()) button.Disabled = false;
            }
        }
    }
    public void FocusDefault() => GetNode<OpeningSettingButton>(Advanced ? "Advanced" : "Beginner").GrabFocus();
    private void Choice(string name, string title, Vector2 position, Action choose, Func<bool> selected)
    {
        var image = Service.LibraryAssets[Art.ChoiceHighlight];
        var button = new OpeningSettingButton { Name = name, Textures = _textures, Highlight = Art.ChoiceHighlight,
            Title = title, Position = position, Size = new(image.Width, image.Height) };
        button.Pressed += () => { choose(); Refresh(); };
        AddChild(button); _choices.Add((button, selected));
    }
    private void Refresh()
    {
        foreach (var (button, selected) in _choices) button.Selected = selected();
        for (int i = 0; i < _summary.Count; i++) _summary[i].Text = $"+{_values[i]}{(i == 5 ? "%" : "")}";
    }
    private void EditCombat()
    {
        if (_dialog is not null) return;
        _dialog = new OpeningCombatSettingsDialog { Name = "CombatSettings", Service = Service, LibraryTextures = _textures,
            Skin = Definition.Forces.MapSkin, Background = Art.CombatDialog, Initial = _values, Closed = CloseCombat, Confirmed = values =>
            { _values = values; Refresh(); CloseCombat(); } };
        AddChild(_dialog);
    }
    private void CloseCombat()
    {
        RemoveChild(_dialog!); _dialog!.QueueFree(); _dialog = null;
    }
    private Label Label(string value, Vector2 position, Vector2 size, GameTextStyle style,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label { Text = value, Position = position, Size = size,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.Apply(label, style);
        return label;
    }
    private StartMenuButton Button(StartMenuSprite sprite, string name, string title, Action? action, GameButtonTextRole? role = null,
        Func<bool, bool, bool, GameTextStyle>? textStyle = null)
    {
        var image = Service.LibraryAssets[sprite.Asset];
        var button = new StartMenuButton { Name = name, Textures = _textures, Sprite = sprite, Title = title,
            TextRole = role, TextStyle = textStyle, Position = new(sprite.X, sprite.Y), Size = new(image.Width / sprite.Columns, image.Height) };
        AddChild(button); if (action is not null) button.Pressed += action; return button;
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || _dialog is not null || _creating) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Closed(); }
    }
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || !_textures.IsReady) return;
        _elapsed += delta; QueueRedraw();
    }
    public override void _Draw()
    {
        if (!_textures.IsReady) { _textures.BindRedraw(this); return; }
        foreach (var sprite in Art.Foreground.Concat(Art.Bars))
            DrawTexture(_textures.Get(sprite.Asset, this), new(sprite.X, sprite.Y));
        StartMenuArtwork.PaintAnimation(this, _textures, Art.Illustration, _elapsed);
    }
    public override void _ExitTree() { _textures.Dispose(); _text.Dispose(); }
}
