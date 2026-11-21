using Godot;
using San9.Application.Authoring;
using San9.Content.Authoring;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Client.Godot.Persistence;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot;

/// <summary>Opening host that loads shared resources and catalog summaries without creating a world.</summary>
public partial class StartMenuPreview : Node
{
    [Export] public string ContentRoot { get; set; } = "../../local/generated/content";
    public Func<string, int[], San9.Core.Domain.NewGameSettings, int, NewGameSetup, Task> BeginGame { get; init; } = null!;
    public Func<SlotSaveDocument, Task> LoadGame { get; init; } = null!;
    public ClientDataPaths? DataPaths { get; init; }
    public UiPreferencesSession Preferences { get; init; } = new();
    public UserSettings Settings { get; init; } = UserSettings.Default;
    public bool InitiallyVisible { get; init; } = true;
    private StartMenuView _view = null!;
    private SceneMusicPlayer _music = null!;
    private string _musicRole = "";
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _prepared = new();
    public Task Prepared => _prepared.Task;
    public override async void _Ready()
    {
        var token = _lifetime.Token;
        try
        {
            string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ContentRoot));
            var catalog = await ContentCatalogFiles.LoadAsync(root, token);
            var opening = await ContentCatalogFiles.LoadOpeningAsync(root, catalog, token);
            var paths = DataPaths ?? ClientDirectories.Resolve(OS.HasFeature("template"), OS.GetName(),
                OS.GetExecutablePath(), ProjectSettings.GlobalizePath("res://"), ProjectSettings.GlobalizePath("user://"), OS.GetSystemDir(OS.SystemDir.Documents)).UserData;
            var textures = await Task.Run(() => TexturePackageFiles.LoadAsync(PackageFiles.Resolve(root, catalog.Textures), token,
                paths.PortraitPack, paths.TreasureIllustrations), token);
            token.ThrowIfCancellationRequested();
            opening.Menu.Validate(textures.Assets); opening.Selection.Validate(textures.Assets);
            var service = new TextureService(); service.Initialize(textures, PackageFiles.Resolve(root, catalog.Textures)); AddChild(service);
            var music = new SceneMusicPlayer { ContentRoot = root, Tracks = opening.Audio.Sounds };
            _music = music; _musicRole = opening.Audio.Music;
            AddChild(music);
            music.SetEnabled(Settings.MusicEnabled);
            if (InitiallyVisible) music.SetMapTrack(opening.Audio.Music);
            async Task EnterGame(Func<Task> enter)
            {
                music.SetMapTrack(null);
                // The transition cue finishes after the opening view has handed over to the map.
                var cue = new SceneSoundPlayer { Name = "OpeningEntrySound", ContentRoot = root, Sounds = opening.Audio.Sounds };
                GetParent().AddChild(cue);
                try
                {
                    cue.PlayAndFree(opening.Audio.GameStartSound, Settings.SoundEnabled);
                    await enter();
                }
                catch
                {
                    if (IsInstanceValid(cue)) cue.QueueFree();
                    if (!token.IsCancellationRequested) music.SetMapTrack(opening.Audio.Music);
                    throw;
                }
            }
            var view = new StartMenuView { Definition = opening.Menu, SelectionDefinition = opening.Selection, Service = service, Visible = InitiallyVisible,
                DataPaths = paths, Preferences = Preferences, ContentRoot = root, Store = new SlotSaveStore(paths.SaveSlots), LoadGame = document => EnterGame(() => LoadGame(document)),
                BeginGame = (id, players, settings, damage, setup) => EnterGame(() => BeginGame(id, players, settings, damage, setup)),
                LoadScenario = (id, cancellation) => Task.Run(async () =>
                {
                    var entry = catalog.RequireScenario(id);
                    var content = await ScenarioContentFiles.LoadSelectionAsync(PackageFiles.Resolve(root, entry.Package), cancellation, paths.CreatedOfficers);
                    cancellation.ThrowIfCancellationRequested();
                    var source = await ScenarioContentFiles.LoadScenarioAsync(PackageFiles.Resolve(root, entry.Package), cancellation, paths.CreatedOfficers);
                    return new NewGameDraft(source, content.Text, content.IllustrationRoles);
                }, cancellation) };
            _view = view;
            AddChild(view);
            while (!view.IsReady)
            {
                if (view.ResourceError is { } error) throw error;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                token.ThrowIfCancellationRequested();
            }
            GD.Print("START_MENU_READY");
            _prepared.TrySetResult();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { _prepared.TrySetCanceled(token); }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            _prepared.TrySetException(error);
            GD.PushError($"Opening menu preview failed: {error}");
        }
    }
    public void Activate()
    {
        _view.Show(); _view.FocusDefault(); _music.SetMapTrack(_musicRole);
    }
    public override void _ExitTree()
    {
        _lifetime.Cancel(); _prepared.TrySetCanceled(_lifetime.Token); _lifetime.Dispose();
    }
}
