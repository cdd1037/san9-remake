using Godot;
using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>Opening host that loads shared resources and catalog summaries without creating a world.</summary>
public partial class StartMenuPreview : Node
{
    [Export] public string ContentRoot { get; set; } = "../../local/generated/content";
    private readonly CancellationTokenSource _lifetime = new();
    public override async void _Ready()
    {
        var token = _lifetime.Token;
        try
        {
            WindowStartup.Apply(GetWindow());
            string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ContentRoot));
            var catalog = await ContentCatalogFiles.LoadAsync(root, token);
            var opening = await ContentCatalogFiles.LoadOpeningAsync(root, catalog, token);
            var textures = await Task.Run(() => TexturePackageFiles.LoadAsync(PackageFiles.Resolve(root, catalog.Textures), token), token);
            token.ThrowIfCancellationRequested();
            opening.Menu.Validate(textures.Assets); opening.Selection.Validate(textures.Assets);
            var service = new TextureService(); service.Initialize(textures, PackageFiles.Resolve(root, catalog.Textures)); AddChild(service);
            var view = new StartMenuView { Definition = opening.Menu, SelectionDefinition = opening.Selection, Service = service };
            AddChild(view);
            while (!view.IsReady)
            {
                if (view.ResourceError is { } error) throw error;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                token.ThrowIfCancellationRequested();
            }
            GD.Print("START_MENU_READY");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            GD.PushError($"Opening menu preview failed: {error}"); GetTree().Quit(1);
        }
    }
    public override void _ExitTree() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
