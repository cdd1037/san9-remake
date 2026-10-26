using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Pre-session force choices. Confirmation is a draft; world creation belongs to the next startup step.</summary>
public partial class ForceSelectionView : ModalView
{
    public ScenarioSelectionPresentation Definition { get; init; } = null!;
    public ScenarioPreviewEntry Entry { get; init; } = null!;
    public Func<CancellationToken, Task<ScenarioForceSelection>> LoadScenario { get; init; } = null!;
    private ScenarioPreparation _preparation = null!;
    private CancellationToken _token;
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _openingMap;
    private Label _hint = null!;
    public TextureService Service { get; init; } = null!;
    public StartMenuSprite Help { get; init; } = null!;
    public StartMenuSprite Close { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action QuitRequested { get; init; } = null!;
    public Action Confirmed { get; init; } = null!;
    public IReadOnlySet<int> Selected => _selected;
    private HashSet<int> _selected = [];
    private TextureScope _textures = null!;
    private readonly GameTextPresenter _text = new();
    private ForceSelectionMapView? _dialog;
    private readonly List<Label> _names = [];
    private readonly List<AsyncPortrait> _portraits = [];
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _preparation = new(LoadScenario); _token = _preparation.Token;
        _textures = Service.CreateScope(Service.LibraryAssets);
        _textures.Request(Definition.Forces.Assets.Concat([Help.Asset, Close.Asset]));
        Label("选择势力", new(48, 16), new(224, 32));
        Label($"{Entry.Year}年{Entry.Month}月 {Entry.Title}", new(32, 108), new(960, 28), new("00f878"));
        Button(Definition.Forces.Choose, "ChooseForces", "选择势力", OpenMap);
        Button(Definition.Forces.Back, "Back", "返回", Return);
        Button(Definition.Forces.Confirm, "Confirm", "决定", Confirmed).Disabled = true;
        Button(Help, "Help", "", null);
        Button(Close, "Close", "", QuitRequested);
        Vector2[] slots = [new(536, 152), new(672, 152), new(808, 152), new(640, 304), new(776, 304), new(600, 456), new(736, 456), new(872, 456)];
        foreach (var p in slots)
        {
            _names.Add(Label("", p + new Vector2(-32, 92), new(128, 24), Colors.White, 16, HorizontalAlignment.Center));
            var portrait = new AsyncPortrait { Service = Service, View = San9.Content.Images.PortraitView.Face,
                Position = p, Size = new(64, 80), MouseFilter = MouseFilterEnum.Ignore };
            AddChild(portrait); _portraits.Add(portrait);
        }
        _hint = Label("请选择势力。", new(0, 736), new(1024, 24), Colors.White, 16, HorizontalAlignment.Center);
        GetNode<StartMenuButton>("ChooseForces").GrabFocus();
        PrepareAfterFirstFrame();
    }
    private async void PrepareAfterFirstFrame()
    {
        var tree = GetTree();
        try
        {
            // Two process boundaries guarantee the page has had a drawing opportunity,
            // including when it was created during the current frame's input handling.
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            _token.ThrowIfCancellationRequested();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            _token.ThrowIfCancellationRequested();
            var task = _preparation.PrepareAsync();
            _firstFrame.TrySetResult();
            await task;
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (Exception error) { ShowPreparationError(error); }
    }
    private void ShowPreparationError(Exception error)
    {
        if (_token.IsCancellationRequested) return;
        _hint.Text = "剧本数据读取失败，请点击选择势力重试。";
        GD.PushError($"Failed to prepare selected scenario {Entry.Id}: {error}");
    }
    private void Return()
    {
        _preparation.Dispose();
        Closed();
    }
    private Label Label(string title, Vector2 p, Vector2 size, Color? color = null, int font = 24,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label { Text = title, Position = p, Size = size, ClipText = true,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.Apply(label, new(font, color ?? Colors.White, GameTextEffect.Outline, Colors.Black)); return label;
    }
    private StartMenuButton Button(StartMenuSprite sprite, string name, string title, Action? pressed)
    {
        var image = Service.LibraryAssets[sprite.Asset];
        var button = new StartMenuButton { Name = name, Textures = _textures, Sprite = sprite,
            Title = title, TextRole = name == "Back" ? GameButtonTextRole.ConfirmNo : name == "Confirm" ? GameButtonTextRole.ConfirmYes : null,
            Position = new(sprite.X, sprite.Y), Size = new(image.Width / sprite.Columns, image.Height) };
        AddChild(button);
        if (pressed is not null) button.Pressed += pressed;
        return button;
    }
    private async void OpenMap()
    {
        if (_dialog is not null || _openingMap) return;
        _openingMap = true;
        _hint.Text = "正在准备势力数据…";
        try
        {
            await _firstFrame.Task;
            _token.ThrowIfCancellationRequested();
            var scenario = await _preparation.PrepareAsync();
            _token.ThrowIfCancellationRequested();
            if (!IsVisibleInTree()) { _hint.Text = "请选择势力。"; return; }
            _hint.Text = "请选择势力。";
            OpenPreparedMap(scenario);
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (Exception error) { ShowPreparationError(error); }
        finally { _openingMap = false; }
    }
    private void OpenPreparedMap(ScenarioForceSelection scenario)
    {
        _dialog = new ForceSelectionMapView { Name = "ForceMap", Definition = Definition.Map, Scenario = scenario, Service = Service,
            Skin = Definition.Forces.MapSkin,
            InitialSelection = _selected, Closed = CloseMap, Confirmed = selected =>
            {
                _selected = selected.ToHashSet();
                GetNode<StartMenuButton>("Confirm").Disabled = _selected.Count == 0;
                var forces = scenario.Forces.Where(f => _selected.Contains(f.Id)).ToArray();
                for (int i = 0; i < _names.Count; i++)
                {
                    _names[i].Text = i < forces.Length ? forces[i].Name : "";
                    _portraits[i].Role = i < forces.Length ? forces[i].Portrait : null;
                }
                CloseMap();
            } };
        AddChild(_dialog);
    }
    private void CloseMap()
    {
        RemoveChild(_dialog!); _dialog!.QueueFree(); _dialog = null;
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || _dialog is not null) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Return(); }
    }
    public override void _Draw()
    {
        if (!_textures.IsReady) { _textures.BindRedraw(this); return; }
        foreach (var sprite in Definition.Forces.Foreground.Concat([Definition.Forces.Heading, Definition.Forces.PortraitSlots]))
            DrawTexture(_textures.Get(sprite.Asset, this), new(sprite.X, sprite.Y));
    }
    public override void _ExitTree()
    {
        _preparation.Dispose();
        _firstFrame.TrySetCanceled(_token);
        _textures.Dispose(); _text.Dispose();
    }
}
