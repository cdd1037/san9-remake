using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Opening menu presentation only. The host will own future navigation and commands.</summary>
public partial class StartMenuView : FixedPage
{
    protected override Color PageBackground => Colors.Black;
    public StartMenuPresentation Definition { get; init; } = null!;
    public ScenarioSelectionPresentation SelectionDefinition { get; init; } = null!;
    public Func<string, CancellationToken, Task<ScenarioForceSelection>> LoadScenario { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    private TextureScope _textures = null!;
    private StartMenuArtwork _artwork = null!;
    private Label _hint = null!;
    private Control _controls = null!;
    private ScenarioSelectionView? _selection;
    private ForceSelectionView? _forces;
    private OpeningSettingsView? _settings;
    private GameDialog? _quit;
    private TextureScope _dialogTextures = null!;
    private Control? _returnPage, _returnFocus;
    private readonly GameTextPresenter _text = new();
    public bool IsReady => _textures.IsReady;
    public Exception? ResourceError => _textures.Error;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _textures = Service.CreateScope(Service.LibraryAssets);
        _textures.Request(Definition.Assets);
        _dialogTextures = Service.CreateScope(Service.Roles);
        _dialogTextures.RequestPrefixes("dialog-");
        _artwork = new StartMenuArtwork { Definition = Definition, Textures = _textures, Size = Size,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_artwork);
        _controls = new Control { Name = "OpeningControls", Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_controls);
        _hint = new Label { Position = new(0, 736), Size = new(1024, 24), Text = Definition.Prompt,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore };
        _controls.AddChild(_hint);
        _text.Apply(_hint, GameTextStyles.For(GameTextRole.Message));
        StartMenuButton? first = null;
        foreach (var entry in Definition.Entries)
        {
            var button = AddButton(entry.Button, entry.Label);
            button.Name = entry.Id;
            if (entry.Id == "new-game") button.Pressed += OpenScenarioSelection;
            if (entry.Id == "exit") button.Pressed += RequestQuit;
            button.MouseEntered += () => { button.GrabFocus(); Select(entry); };
            button.FocusEntered += () => Select(entry);
            button.MouseExited += RestorePrompt;
            first ??= button;
        }
        AddButton(Definition.Help, "").MouseEntered += RestorePrompt;
        var close = AddButton(Definition.Close, "");
        close.Name = "Close"; close.MouseEntered += RestorePrompt; close.Pressed += RequestQuit;
        first?.GrabFocus();
        RestorePrompt();
    }
    private StartMenuButton AddButton(StartMenuSprite sprite, string title)
    {
        var image = Service.LibraryAssets[sprite.Asset];
        var button = new StartMenuButton { Textures = _textures, Sprite = sprite, Title = title,
            Position = new(sprite.X, sprite.Y), Size = new(image.Width / sprite.Columns, image.Height) };
        _controls.AddChild(button);
        return button;
    }
    private void OpenScenarioSelection()
    {
        if (_selection is null)
        {
            _selection = new ScenarioSelectionView { Name = "ScenarioSelection", Definition = SelectionDefinition,
                Service = Service, Help = Definition.Help, Close = Definition.Close, Closed = ReturnToMenu, QuitRequested = RequestQuit, Selected = OpenForces };
            AddChild(_selection);
        }
        _controls.Hide(); _artwork.MenuVisible = false; _selection.Show(); _selection.FocusSelected();
    }
    private void OpenForces(ScenarioPreviewEntry entry)
    {
        if (_forces is not null) return;
        _selection!.Hide();
        _forces = new ForceSelectionView { Name = "ForceSelection", Definition = SelectionDefinition, Entry = entry,
            LoadScenario = cancellation => LoadScenario(entry.Id, cancellation),
            Service = Service, Help = Definition.Help, Close = Definition.Close, QuitRequested = RequestQuit,
            Confirmed = OpenSettings, Closed = () =>
            {
                _settings?.QueueFree(); _settings = null;
                _forces!.QueueFree(); _forces = null; _selection.Show(); _selection.FocusSelected();
            } };
        AddChild(_forces);
    }
    private void OpenSettings()
    {
        if (_settings is null)
        {
            _settings = new OpeningSettingsView { Name = "OpeningSettings", Definition = SelectionDefinition,
                Service = Service, Help = Definition.Help, Close = Definition.Close, QuitRequested = RequestQuit,
                Closed = () =>
                {
                    _settings!.Hide(); _forces!.Show(); _forces.GetNode<StartMenuButton>("Confirm").GrabFocus();
                } };
            AddChild(_settings);
        }
        _forces!.Hide(); _settings.Show(); _settings.FocusDefault();
    }
    private void ReturnToMenu()
    {
        _selection!.Hide(); _artwork.MenuVisible = true; _controls.Show();
        _controls.GetNode<StartMenuButton>("new-game").GrabFocus(); RestorePrompt();
    }
    private void RequestQuit()
    {
        if (_quit is not null) return;
        _returnFocus = GetViewport().GuiGetFocusOwner();
        _returnPage = _settings is { Visible: true } ? _settings : _forces is { Visible: true } ? _forces : _selection is { Visible: true } ? _selection : _controls;
        _returnPage.Hide();
        _artwork.MenuVisible = true; _artwork.IllustrationVisible = false;
        _quit = new GameDialog { Name = "QuitConfirmation", Textures = _dialogTextures, Size = Size, PreferredWidth = 288 };
        _quit.SetRichMessage(Definition.QuitPrompt);
        _quit.Confirmed += () => GetTree().Quit();
        _quit.Dismissed += CancelQuit;
        AddChild(_quit); _quit.FocusDefaultButton();
    }
    private void CancelQuit()
    {
        if (_quit is null) return;
        RemoveChild(_quit); _quit.QueueFree(); _quit = null;
        _artwork.IllustrationVisible = true;
        _artwork.MenuVisible = _returnPage == _controls;
        _returnPage!.Show();
        if (IsInstanceValid(_returnFocus)) _returnFocus!.GrabFocus();
        _returnPage = null; _returnFocus = null;
    }
    public override void _Input(InputEvent input)
    {
        if (_quit is null || input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        GetViewport().SetInputAsHandled(); CancelQuit();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_quit is not null || !_controls.Visible) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { GetViewport().SetInputAsHandled(); RequestQuit(); }
    }
    private void Select(StartMenuEntry entry)
    {
        _artwork.Select(entry);
        _hint.Text = entry.Hint;
    }
    private void RestorePrompt() => _hint.Text = Definition.Prompt;
    public override void _ExitTree() { _textures.Dispose(); _dialogTextures.Dispose(); _text.Dispose(); }
}
