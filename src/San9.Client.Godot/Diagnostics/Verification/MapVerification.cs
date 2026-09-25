using Godot;
using San9.Rendering;
using static San9.Client.Godot.Diagnostics.VerificationSupport;

namespace San9.Client.Godot.Diagnostics;

public partial class ClientVerification
{
    private async Task RunMapChecks()
    {
        try
        {
            var origin = MapGeometry.Center(89, 64); World.FocusWorld(new(origin.X, origin.Y));
            await WaitForView();
            World._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(512, 384) });
            Check(Selection.Square == (89, 64), "Center picking failed.");
            var before = World.ViewportToWorld(new(700, 400));
            World._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new(700, 400) });
            await WaitForView();
            Check(World.ZoomFactor > 1 && World.ViewportToWorld(new(700, 400)).DistanceTo(before) < 0.01f, "Mouse-centered zoom failed.");
            var selected = Selection.Square;
            World._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(-20, 300) });
            Check(selected == Selection.Square, "Letterbox input changed selection.");
            World._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new(700, 400) });
            var previous = World.CameraPosition;
            World._UnhandledInput(new InputEventMouseMotion { Position = new(700, 400), Relative = new(120, 60) });
            await WaitForView();
            Check(previous.DistanceTo(World.CameraPosition) > 50, "Drag failed.");
            World.SetZoom(0.01f); await WaitForView(); Check(World.ZoomFactor == 0.25f, "Minimum zoom failed.");
            World.SetZoom(20); await WaitForView(); Check(World.ZoomFactor == 2, "Maximum zoom failed.");
            foreach (var target in new[] { new Vector2(6496, -10000), new Vector2(6496, 20000), new Vector2(-20000, 3312), new Vector2(30000, 3312) })
            {
                World.FocusWorld(target); await WaitForView();
                var edge = World.CameraPosition;
                Check(edge.DistanceTo(target) > 1000, "Outside center was accepted.");
                World.SetZoom(.25f); await WaitForView();
                Check(edge.DistanceTo(World.CameraPosition) < .01f, "Zoom moved boundary center.");
                World.SetZoom(2); await WaitForView();
                Check(edge.DistanceTo(World.CameraPosition) < .01f, "Zoom moved boundary center.");
            }
            World.SetZoom(1); World.FocusWorld(new(origin.X, origin.Y));
            await WaitForView();
            foreach (string season in new[] { "winter", "summer", "autumn", "spring" })
            {
                World.PreviewSeason(season); var timeout = System.Diagnostics.Stopwatch.StartNew();
                do { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Check(World.HasCompleteView, "Season lost presented terrain."); if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("Season switch timeout."); } while (World.IsLoading);
                Check(World.LoadError is null && World.Season == season, "Season switch failed.");
            }
            for (int pass = 0; pass < 2; pass++)
            {
                World.FocusWorld(new(origin.X, origin.Y)); await WaitForView();
                List<double> frameTimes = [], requestTimes = [];
                int delayed = 0;
                long objects = World.ObjectDraws;
                var frameWatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 120; i++)
                {
                    double start = frameWatch.Elapsed.TotalMilliseconds;
                    World.FocusWorld(new(origin.X + i * 18, origin.Y + i * 6));
                    requestTimes.Add(frameWatch.Elapsed.TotalMilliseconds - start);
                    Check(World.HasCompleteView && World.Display!.Presented!.Ready, "Pan exposed missing terrain.");
                    if (!World.IsVisibleReady) delayed++;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    frameTimes.Add(frameWatch.Elapsed.TotalMilliseconds - start);
                }
                frameTimes.Sort(); requestTimes.Sort();
                Check(World.CacheBytes <= 256L * 1024 * 1024, "Chunk cache exceeded its budget.");
                GD.Print($"MAP_PAN pass={pass} frames=120 median_ms={frameTimes[60]:F2} p95_ms={frameTimes[114]:F2} max_ms={frameTimes[^1]:F2} request_p95_ms={requestTimes[114]:F2} delayed={delayed} object_rebuilds={World.ObjectDraws-objects}");
            }
            World.FocusWorld(new(origin.X, origin.Y));
            await WaitForView();
            await RunNavigationChecks();
            World.FocusWorld(new(origin.X, origin.Y)); await WaitForView();
            GD.Print($"MAP_SMOKE_OK picking zoom drag bounds seasons; cache_mib={World.CacheBytes / 1048576}");
        }
        catch (Exception e) { throw new InvalidOperationException("Map smoke failed.", e); }
    }
    private async Task RunNavigationChecks()
    {
        var prepared = World.CameraPosition;
        World.FocusWorld(prepared + new Vector2(5000, 0));
        World.FocusWorld(prepared);
        Check(World.IsVisibleReady, "Returning to prepared coverage waited for abandoned navigation.");
        await Frames(this, 2);
        long preparedDraws = World.TerrainDraws;
        for (int i = 1; i <= 10; i++)
        {
            World.FocusWorld(prepared + new Vector2(i, 0));
            Check(World.IsVisibleReady, "Prepared coverage did not move immediately.");
            await Frames(this);
        }
        Check(World.TerrainDraws == preparedDraws, "Camera-only motion rebuilt terrain commands.");
        var old = World.CameraPosition;
        World.FocusWorld(new(6000, 5000));
        if (!World.IsVisibleReady && World.CameraPosition != old) throw new InvalidOperationException("Cold navigation moved the presented camera early.");
        await WaitForView();
        GD.Print($"MAP_NAVIGATION kind=cold latency_ms={World.LastNavigationMilliseconds:F2}");
        World.FocusWorld(old); await WaitForView();
        GD.Print($"MAP_NAVIGATION kind=return latency_ms={World.LastNavigationMilliseconds:F2}");
        World.FocusWorld(new(6000, 5000));
        if (!World.IsVisibleReady) throw new InvalidOperationException("Cached navigation did not commit immediately.");
        GD.Print($"MAP_NAVIGATION kind=cached latency_ms={World.LastNavigationMilliseconds:F2}");
        World.FocusWorld(new(2000, 3000));
        if (Options.Screenshot is {} path)
            await Capture(Path.Combine(Path.GetDirectoryName(path)!, "navigation-waiting.png"));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        World.FocusWorld(new(6500, 1200));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        World.FocusWorld(new(3000, 4500)); await WaitForView();
        if (World.CameraPosition.DistanceTo(new(3000, 4500)) > .01f) throw new InvalidOperationException("Superseded navigation committed.");
        // A season's prepared view must not discard a newer navigation request.
        World.PreviewSeason("winter"); World.FocusWorld(new(10000, 2500)); await WaitForView();
        if (World.CameraPosition.DistanceTo(new(10000, 2500)) > .01f) throw new InvalidOperationException("Season discarded latest navigation.");
        World.PreviewSeason("spring"); await WaitForView();
        if (Options.Screenshot is {} targetPath)
            await Capture(Path.Combine(Path.GetDirectoryName(targetPath)!, "navigation-target.png"));
        long terrain = World.TerrainDraws, water = World.WaterDraws, objects = World.ObjectDraws;
        List<double> cpu = [];
        for (int i = 0; i < 120; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!World.HasCompleteView) throw new InvalidOperationException("Idle lost terrain.");
            cpu.Add(World.LastProcessMilliseconds);
        }
        cpu.Sort();
        GD.Print($"MAP_IDLE frames=120 process_p95_ms={cpu[114]:F3} terrain_draws={World.TerrainDraws-terrain} water_draws={World.WaterDraws-water} object_draws={World.ObjectDraws-objects} chunk_cpu_total_ms={World.TotalChunkRenderMilliseconds:F2} upload_total_ms={World.TotalUploadMilliseconds:F2}");
    }
}
