using Godot;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Content.Packages;
using San9.Rendering;
using static San9.Client.Godot.Diagnostics.VerificationSupport;

namespace San9.Client.Godot.Diagnostics;

public partial class ClientVerification
{
    private async Task RunMapResourceChecks()
    {
        using var firstFrame = new ManualResetEventSlim();
        using var lastFrame = new ManualResetEventSlim();
        var extra = new[] { "fixture-visible", "fixture-frame-0", "fixture-frame-1", "fixture-error" }
            .Select(id => new ImageAsset(id, id + ".png", 8, 8)).ToArray();
        var clip = new MapAnimationClip("fixture-animation", [extra[1].Id, extra[2].Id], 100, []);
        var map = World.Package! with { Assets = [.. World.Package.Assets, .. extra],
            Animations = new([.. World.Package.Animations.Clips, clip], []) };
        var service = new TextureService();
        service.Initialize(new ClientContentPackage(0, [], map, null!, Hud.Texts, Hud.Skin, null!), (file, token) =>
        {
            if (file.Path == extra[1].Path) firstFrame.Wait(token);
            if (file.Path == extra[2].Path) lastFrame.Wait(token);
            if (file.Path == extra[3].Path) throw new InvalidDataException("Controlled offscreen texture failure.");
            return new(file.Width, file.Height, file.Channels, new byte[file.Width * file.Height * file.Channels]);
        });
        AddChild(service);
        using var terrain = new TerrainChunkCache<ImageTexture>(pixels =>
        {
            using var image = Image.CreateFromData(1, 1, false, Image.Format.Rgb8, pixels.Pixels);
            return ImageTexture.CreateFromImage(image);
        }, texture => texture.Dispose(), render: (_, _) => new(1, 1, 3, [0, 0, 0]));
        var scene = new MapScene(map, World.Scene!.Terrain, World.Scene.Season, World.Session.View, service, () => World.UnitPositionTime);
        // The visible sprite starts outside the viewport, but four columns intersect it.
        var region = new MapSceneRegion(scene, new(0, 0, 512, 512), [], [], [], [],
            [new(extra[0].Id, 80, 100), new(extra[3].Id, 400, 100)], [new(clip.Id, 300, 100, 0, -1)]);
        MapSceneView.Frame Frame(float x) => new(region, new(x, 100, 1), new(32, 32),
            service.CreateScope(service.MapAssets), terrain.Acquire([new(scene.Terrain, 0, 0)]), false);
        using var transition = new MapFrameTransition<MapSceneView.Frame>(f => f.Ready, f => f.Error, f => f.Dispose());
        using var prefetch = service.CreateScope(service.MapAssets, true);
        async Task Until(Func<bool> condition)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                terrain.Pump(() => true);
                Check(watch.Elapsed.TotalSeconds < 10, "Map resource fixture timed out.");
                await Frames(this);
            }
        }
        try
        {
            var home = Frame(100); transition.Request(home);
            prefetch.Request(region.Assets);
            await Until(() => home.Ready);
            Check(transition.TryCommit(), "Offscreen loading blocked the visible frame.");
            Check(home.Assets.TryGetReady(extra[0].Id, out _), "Cross-boundary sprite was not required.");
            Check(!service.Cache.Contains(extra[2].File), "Delayed offscreen frame unexpectedly loaded.");
            long misses = service.Cache.Misses;
            Check(!home.Assets.TryGetReady(extra[2].Id, out _), "Offscreen texture became a drawing dependency.");
            Check(service.Cache.Misses == misses, "Readiness-only drawing initiated a load.");
            var target = Frame(300); transition.Request(target);
            Check(!transition.TryCommit(), "Navigation entered incomplete animation.");
            firstFrame.Set();
            await Until(() => service.Cache.Contains(extra[1].File));
            Check(!target.Ready, "Animation committed without all its seasonal frames.");
            Check(home.CanPresent(new(100, 100, 1), new(32, 32)), "Pending animation blocked return to the old view.");
            transition.CancelPending();
            target = Frame(300); transition.Request(target);
            lastFrame.Set();
            await Until(() => target.Ready && prefetch.Error is not null);
            Check(target.Error is null && transition.TryCommit(), "Failed offscreen image poisoned the visible frame.");
            var broken = Frame(400); transition.Request(broken);
            await Until(() => broken.Error is not null);
            Check(!transition.TryCommit() && ReferenceEquals(transition.Presented, target), "Failed visible image replaced the complete frame.");
            GD.Print("MAP_RESOURCES_OK offscreen-delay boundary-intersection full-animation return offscreen-failure");
        }
        finally
        {
            firstFrame.Set(); lastFrame.Set();
            transition.Dispose(); prefetch.Dispose();
            await Until(() => service.Cache.PendingBytes == 0);
            service.QueueFree(); await Frames(this, 3);
        }
    }
}
