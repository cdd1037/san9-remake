using Godot;
using San9.Application.Authoring;
using San9.Content.Authoring;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Pre-session force choices. Confirmation is a draft; world creation belongs to the next startup step.</summary>
public partial class ForceSelectionView : ModalView
{
    public ScenarioSelectionPresentation Definition { get; init; } = null!;
    public ScenarioPreviewEntry Entry { get; init; } = null!;
    public Func<CancellationToken, Task<NewGameDraft>> LoadScenario { get; init; } = null!;
    private ScenarioPreparation<NewGameDraft> _preparation = null!;
    private CancellationToken _token;
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _openingMap;
    private NewGameDraft? _draft;
    private OpeningSetupDialog? _setupDialog;
    public NewGameSetup Setup => _draft?.Setup ?? NewGameSetup.Empty;
    private Label _hint = null!;
    private Label _createdCount = null!;
    public TextureService Service { get; init; } = null!;
    public StartMenuSprite Help { get; init; } = null!;
    public StartMenuSprite Close { get; init; } = null!;
    public Action Closed { get; init; } = null!;
    public Action QuitRequested { get; init; } = null!;
    public Settings.UiPreferencesSession Preferences { get; init; } = new();
    public Persistence.ClientDataPaths DataPaths { get; init; } = null!;
    public string? ContentRoot { get; init; }
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
        OpeningPageHeading.Add(this, _text, "选择势力");
        Label($"{Entry.Year}年{Entry.Month}月 {Entry.Title}", new(32, 108), new(960, 28), new("00f878"));
        Button(Definition.Forces.Choose, "ChooseForces", "选择势力", () => OpenPrepared(draft => OpenPreparedMap(draft.Selection())));
        Button(Definition.Forces.CreateForces, "CreateForces", "创作新势力", () => OpenSetup(create: true)).Disabled = true;
        Button(Definition.Forces.DeployOfficers, "DeployOfficers", "部署新武将", () => OpenSetup()).Disabled = true;
        Button(Definition.Forces.CancelDeployment, "CancelDeployment", "取消部署", () => OpenSetup(cancel: true)).Disabled = true;
        _createdCount = Label("势力数 0/8", new(268, 274), new(160, 16), font: 16);
        Button(Definition.Forces.Back, "Back", "返回", Return);
        Button(Definition.Forces.Confirm, "Confirm", "决定", Confirmed).Disabled = true;
        if (ContentRoot is not null)
        {
            var edit = new Button { Name = "EditOpeningOfficers", Text = "编辑武将资料", Position = new(40, 560) };
            AddChild(edit); edit.Pressed += () => OpenPrepared(OpenOfficerEditor);
        }
        Button(Help, "Help", "", null);
        Button(Close, "Close", "", QuitRequested);
        Vector2[] slots = [new(536, 152), new(672, 152), new(808, 152), new(640, 304), new(776, 304), new(600, 456), new(736, 456), new(872, 456)];
        foreach (var p in slots)
        {
            _names.Add(Label("", p + new Vector2(0, 96), new(64, 16), Colors.White, 16, HorizontalAlignment.Center));
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
            _draft = await task;
            RefreshDraft();
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
    private async void OpenPrepared(Action<NewGameDraft> open)
    {
        if (_dialog is not null || _setupDialog is not null || _openingMap) return;
        _openingMap = true;
        _hint.Text = "正在准备势力数据…";
        try
        {
            await _firstFrame.Task;
            _token.ThrowIfCancellationRequested();
            _draft = await _preparation.PrepareAsync();
            _token.ThrowIfCancellationRequested();
            if (!IsVisibleInTree()) { _hint.Text = "请选择势力。"; return; }
            _hint.Text = "请选择势力。";
            open(_draft);
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (Exception error) { ShowPreparationError(error); }
        finally { _openingMap = false; }
    }
    private void OpenOfficerEditor(NewGameDraft previous)
    {
        var parent = GetParent();
        bool saved = false;
        var editor = new SharedDataEditor { DataPaths = DataPaths, ReferenceScenarioId = Entry.Id, Preferences = Preferences, ContentRoot = ContentRoot!, Service = Service, OpenOfficers = true,
            SavedChanges = () => saved = true };
        editor.Closed = async () =>
        {
            editor.QueueFree();
            if (saved)
            {
                _preparation.Dispose();
                _preparation = new(LoadScenario); _token = _preparation.Token;
                _draft = null;
                try
                {
                    _draft = await _preparation.PrepareAsync();
                    var skipped = _draft.RestoreOpeningChoices(previous);
                    _selected = _draft.PlayerForceIds.ToHashSet();
                    RefreshDraft();
                    if (skipped.Count > 0)
                    {
                        var names = skipped.Select(id => previous.Source.OfficerCatalog[id].Name);
                        _hint.Text = $"已取消无效的开局安排：{string.Join("、", names)}";
                        _hint.TooltipText = _hint.Text;
                    }
                }
                catch (OperationCanceledException) when (_token.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    _selected = _draft?.PlayerForceIds.ToHashSet() ?? [];
                    RefreshDraft();
                    _hint.Text = $"请重新检查开局选择：{error.Message}";
                    if (_draft is null) GetNode<StartMenuButton>("Confirm").Disabled = true;
                }
            }
            Show();
        };
        Hide(); parent.AddChild(editor);
    }

    private void OpenPreparedMap(ScenarioForceSelection scenario)
    {
        _dialog = new ForceSelectionMapView { Name = "ForceMap", Definition = Definition.Map, Scenario = scenario, Service = Service,
            Skin = Definition.Forces.MapSkin,
            InitialSelection = _selected, Closed = CloseMap, Confirmed = selected =>
            {
                _draft!.SetPlayerForces(selected);
                _selected = selected.ToHashSet();
                RefreshDraft();
                CloseMap();
            } };
        AddChild(_dialog);
    }
    private void OpenSetup(bool create = false, bool cancel = false) => OpenPrepared(draft =>
    {
        if (create) foreach (var child in GetChildren().OfType<CanvasItem>()) child.Hide();
        _setupDialog = new OpeningSetupDialog { Name = "OpeningSetup", Draft = draft, Service = Service, Definition = Definition.Forces, Geography = Definition.Map,
            Help = Help, Close = Close, QuitRequested = QuitRequested,
            CreateForces = create, CancelDeployment = cancel, Closed = CloseSetup,
            Changed = () => { RefreshDraft(); CloseSetup(); } };
        AddChild(_setupDialog);
        QueueRedraw();
    });
    private void CloseSetup()
    {
        RemoveChild(_setupDialog!); _setupDialog!.QueueFree(); _setupDialog = null;
        foreach (var child in GetChildren().OfType<CanvasItem>()) child.Show();
        QueueRedraw();
    }
    private void RefreshDraft()
    {
        if (_draft is null) return;
        GetNode<StartMenuButton>("CancelDeployment").Disabled = Setup.Placements.IsEmpty;
        GetNode<StartMenuButton>("DeployOfficers").Disabled = !_draft.Candidates.Any(p =>
            NewGameComposer.CanDeploy(p, _draft.Source) && !Setup.Forces.Any(f => f.RulerId == p.Id) &&
            !Setup.Placements.Any(a => a.OfficerId == p.Id));
        GetNode<StartMenuButton>("CreateForces").Disabled = Setup.Forces.IsEmpty &&
            (!_draft.Candidates.Any(p => NewGameComposer.CanDeploy(p, _draft.Source)) ||
             !_draft.Source.Settlements.Any(s => s.Kind == San9.Core.Domain.SettlementKind.City && s.OwnerId is null && !s.IsDormant && s.CanHostPersons));
        // An empty player selection needs neither force totals nor economic forecasts.
        OpeningForce[] forces = [];
        if (_selected.Count > 0)
        {
            var scenario = _draft.Selection();
            _selected.IntersectWith(scenario.Forces.Select(f => f.Id));
            _draft.SetPlayerForces(_selected);
            forces = scenario.Forces.Where(f => _selected.Contains(f.Id)).ToArray();
        }
        GetNode<StartMenuButton>("Confirm").Disabled = _selected.Count == 0;
        for (int i = 0; i < _names.Count; i++)
        {
            _names[i].Text = i < forces.Length ? forces[i].Name : "";
            _portraits[i].Role = i < forces.Length ? forces[i].Portrait : null;
        }
        string prompt = _draft.Candidates.IsEmpty ? "当前剧本没有待部署武将，可在武将资料中添加。" :
            !_draft.Candidates.Any(p => NewGameComposer.CanDeploy(p, _draft.Source)) ? "没有15至90岁的新武将，请检查人物出生年。" : "请选择势力。";
        _createdCount.Text = $"势力数 {Setup.Forces.Length}/{NewGameSetup.MaximumCreatedForces}";
        _hint.TooltipText = "";
        _hint.Text = prompt;
    }
    private void CloseMap()
    {
        RemoveChild(_dialog!); _dialog!.QueueFree(); _dialog = null;
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || _dialog is not null || _setupDialog is not null || _openingMap) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); Return(); }
    }
    public override void _Draw()
    {
        if (_setupDialog is { CreateForces: true }) return;
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
