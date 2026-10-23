using Godot;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Client.Godot.Persistence;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot;

/// <summary>Opening host that loads shared resources and catalog summaries without creating a world.</summary>
public partial class StartMenuPreview : Node
{
    [Export] public string ContentRoot { get; set; } = "../../local/generated/content";
    public Func<string, int[], San9.Core.Domain.NewGameSettings, int, Task> BeginGame { get; init; } = null!;
    public Func<SlotSaveDocument, Task> LoadGame { get; init; } = null!;
    public ClientDataPaths? DataPaths { get; init; }
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
            var textures = await Task.Run(() => TexturePackageFiles.LoadAsync(PackageFiles.Resolve(root, catalog.Textures), token), token);
            token.ThrowIfCancellationRequested();
            opening.Menu.Validate(textures.Assets); opening.Selection.Validate(textures.Assets);
            var service = new TextureService(); service.Initialize(textures, PackageFiles.Resolve(root, catalog.Textures)); AddChild(service);
            var paths = DataPaths ?? new ClientDataPaths(OS.HasFeature("template"), OS.GetName(), OS.GetExecutablePath(), ProjectSettings.GlobalizePath("user://"));
            var settings = new UserSettingsSession(new UserSettingsStore(paths.Settings));
            try { settings.Load(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Text.DecoderFallbackException or InvalidDataException) { }
            var music = new SceneMusicPlayer { ContentRoot = root, Tracks = opening.Audio.Sounds };
            _music = music; _musicRole = opening.Audio.Music;
            AddChild(music);
            music.SetEnabled(settings.Current.MusicEnabled);
            if (InitiallyVisible) music.SetMapTrack(opening.Audio.Music);
            async Task EnterGame(Func<Task> enter)
            {
                music.SetMapTrack(null);
                // The transition cue finishes after the opening view has handed over to the map.
                var cue = new SceneSoundPlayer { Name = "OpeningEntrySound", ContentRoot = root, Sounds = opening.Audio.Sounds };
                GetParent().AddChild(cue);
                try
                {
                    cue.PlayAndFree(opening.Audio.GameStartSound, settings.Current.SoundEnabled);
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
                Store = new SlotSaveStore(paths.SaveSlots), LoadGame = document => EnterGame(() => LoadGame(document)),
                BeginGame = (id, players, settings, damage) => EnterGame(() => BeginGame(id, players, settings, damage)),
                LoadScenario = (id, cancellation) => Task.Run(async () =>
                {
                    var entry = catalog.RequireScenario(id);
                    var content = await ScenarioContentFiles.LoadSelectionAsync(PackageFiles.Resolve(root, entry.Package), cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    return ScenarioForceSelection.Read(content.Scenario, content.Text, content.IllustrationRoles);
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
