using Godot;
using San9.Content.Images;
using San9.Content.Packages;
using San9.Rendering;

namespace San9.Client.Godot.Diagnostics;

/// <summary>Engine regression for delayed textures shared across unrelated canvas roots.</summary>
public partial class TextureRedrawVerification : Node
{
    public override async void _Ready()
    {
        using var release = new ManualResetEventSlim();
        TextureScope? scope = null;
        try
        {
            var options = ClientOptions.Parse(OS.GetCmdlineUserArgs(), Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../local/generated/content")));
            string root = options.PackageRoot ?? (options.ScenarioId is { } id
                ? PackageFiles.Resolve(options.ContentRoot, (await ContentCatalogFiles.LoadAsync(options.ContentRoot)).RequireScenario(id).Package)
                : throw new ArgumentException("Texture verification requires an explicit scenario."));
            var package = await ClientContentFiles.LoadAsync(root);
            var service = new TextureService();
            service.Initialize(package, (file, token) =>
            {
                release.Wait(token);
                return new PngImage(file.Width, file.Height, file.Channels,
                    Enumerable.Repeat((byte)255, file.Width * file.Height * file.Channels).ToArray());
            });
            AddChild(service);
            var hud = new Control();
            var overlay = new Control();
            AddChild(hud); AddChild(overlay);
            scope = service.CreateScope(new Dictionary<string, ImageAsset>
            { ["fixture"] = new("fixture", "fixture.png", 8, 8) });
            scope.Request(["fixture"]);
            var visible = new TextureRedrawProbe { Textures = scope };
            var closed = new TextureRedrawProbe { Textures = scope };
            var unbound = new TextureRedrawProbe { Textures = scope, Observe = false };
            overlay.AddChild(visible); overlay.AddChild(closed); overlay.AddChild(unbound);
            await Until(() => visible.DrawCount > 0 && closed.DrawCount > 0 && unbound.DrawCount > 0);
            Require(!visible.HadAtlas, "The cold draw must precede the texture upload.");
            overlay.RemoveChild(closed); closed.Free();
            Require(scope.RedrawConsumerCount == 1, "A departing consumer must unsubscribe.");
            overlay.RemoveChild(visible);
            Require(scope.RedrawConsumerCount == 0, "Removing a retained page must unsubscribe.");
            overlay.AddChild(visible);
            await Until(() => scope.RedrawConsumerCount == 1);
            int before = visible.DrawCount;
            int unboundBefore = unbound.DrawCount;
            release.Set();
            await Until(() => scope.IsReady && visible.DrawCount > before && visible.HadAtlas);
            Require(unbound.DrawCount == unboundBefore && !unbound.HadAtlas,
                "The unbound control must reproduce the missed redraw outside the owner's subtree.");
            Require(scope.RedrawConsumerCount == 1, "Repeated draws must not duplicate subscriptions.");
            var cached = new TextureRedrawProbe { Textures = scope };
            overlay.AddChild(cached);
            await Until(() => cached.DrawCount > 0);
            Require(cached.HadAtlas, "Reopening with a cached texture must draw immediately.");
            overlay.RemoveChild(visible); visible.Free();
            overlay.RemoveChild(cached); cached.Free();
            overlay.RemoveChild(unbound); unbound.Free();
            Require(scope.RedrawConsumerCount == 0, "Closing pages must detach all drawing consumers.");
            scope.Dispose();
            Require(scope.RedrawConsumerCount == 0, "Disposing a scope must detach its remaining consumers.");
            GD.Print("TEXTURE_REDRAW_OK cold_upload cached_reopen shared_overlay consumer_reentry consumer_exit scope_dispose");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
        finally { release.Set(); scope?.Dispose(); }
    }

    private async Task Until(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("Texture redraw did not complete.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public partial class TextureRedrawProbe : Control
{
    public TextureScope Textures { get; init; } = null!;
    public bool Observe { get; init; } = true;
    public int DrawCount { get; private set; }
    public bool HadAtlas { get; private set; }
    public override void _Draw()
    {
        DrawCount++;
        var texture = Observe ? Textures.Get("fixture", this) : Textures.GetResource("fixture");
        HadAtlas = ((AtlasTexture)texture).Atlas is not null;
        DrawTexture(texture, Vector2.Zero);
    }
}
