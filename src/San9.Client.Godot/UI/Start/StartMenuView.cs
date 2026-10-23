using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Opening menu presentation only. The host will own future navigation and commands.</summary>
public partial class StartMenuView : FixedPage
{
    protected override Color PageBackground => Colors.Black;
    public StartMenuPresentation Definition { get; init; } = null!;
    public ScenarioSelectionPresentation SelectionDefinition { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    private TextureScope _textures = null!;
    private StartMenuArtwork _artwork = null!;
    private Label _hint = null!;
    private Control _controls = null!;
    private ScenarioSelectionView? _selection;
    private readonly GameTextPresenter _text = new();
    public bool IsReady => _textures.IsReady;
    public Exception? ResourceError => _textures.Error;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _textures = Service.CreateScope(Service.LibraryAssets);
        _textures.Request(Definition.Assets);
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
            button.MouseEntered += () => { button.GrabFocus(); Select(entry); };
            button.FocusEntered += () => Select(entry);
            button.MouseExited += RestorePrompt;
            first ??= button;
        }
        AddButton(Definition.Help, "").MouseEntered += RestorePrompt;
        AddButton(Definition.Close, "").MouseEntered += RestorePrompt;
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
                Service = Service, Help = Definition.Help, Close = Definition.Close, Closed = ReturnToMenu };
            AddChild(_selection);
        }
        _controls.Hide(); _artwork.MenuVisible = false; _selection.Show(); _selection.FocusSelected();
    }
    private void ReturnToMenu()
    {
        _selection!.Hide(); _artwork.MenuVisible = true; _controls.Show();
        _controls.GetNode<StartMenuButton>("new-game").GrabFocus(); RestorePrompt();
    }
    private void Select(StartMenuEntry entry)
    {
        _artwork.Select(entry);
        _hint.Text = entry.Hint;
    }
    private void RestorePrompt() => _hint.Text = Definition.Prompt;
    public override void _ExitTree() { _textures.Dispose(); _text.Dispose(); }
}
