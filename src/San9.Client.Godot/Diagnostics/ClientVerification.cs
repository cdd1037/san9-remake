using Godot;
using San9.Application.Presentation;
using San9.Rendering;

namespace San9.Client.Godot.Diagnostics;

public partial class ClientVerification : Node
{
    public MapWorld World { get; init; } = null!;
    public StrategySelection Selection { get; init; } = null!;
    public StrategyHud Hud { get; init; } = null!;
    public ClientOptions Options { get; init; } = null!;
    private bool _started;
    public override async void _Process(double delta)
    {
        if (_started) return;
        if (World.LoadError is not null) { _started = true; GetTree().Quit(1); return; }
        if (!World.IsVisibleReady) return;
        _started = true;
        try
        {
            if (Options.InformationSmoke) await RunInformationChecks();
            if (Options.Smoke) await RunSmoke();
            if (Options.RuntimeSmoke) await RunRuntimeChecks();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!World.IsVisibleReady)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (timeout.Elapsed.TotalSeconds > 30 || World.LoadError is not null) throw new InvalidOperationException("Capture readiness failed.");
            }
            if (Options.Screenshot is {} path)
            {
                if (Options.UiSmoke) await CaptureUiVariants(path);
                await Capture(path);
            }
            else if (Options.Quit) GetTree().Quit();
        }
        catch (Exception e) { GD.PushError($"CLIENT_VERIFICATION_FAILED {e}"); GetTree().Quit(1); }
    }
    private async Task RunSmoke()
    {
        try
        {
            void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
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
            List<double> frameTimes = [];
            var frameWatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 120; i++)
            {
                World.FocusWorld(new(origin.X + i * 18, origin.Y + i * 6));
                Check(World.HasCompleteView, "Pan exposed missing terrain.");
                double previousTime = frameWatch.Elapsed.TotalMilliseconds;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frameTimes.Add(frameWatch.Elapsed.TotalMilliseconds - previousTime);
            }
            frameTimes.Sort();
            Check(World.CacheBytes <= 256L * 1024 * 1024, "Chunk cache exceeded its budget.");
            GD.Print($"MAP_PAN frames=120 median_ms={frameTimes[60]:F2} p95_ms={frameTimes[114]:F2} peak_working_set_mib={System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1048576}");
            World.FocusWorld(new(origin.X, origin.Y));
            await WaitForView();
            await RunNavigationChecks();
            World.FocusWorld(new(origin.X, origin.Y)); await WaitForView();
            if (Options.UiSmoke) await Hud.RunSmokeAsync();
            GD.Print($"MAP_SMOKE_OK picking zoom drag bounds seasons; cache_mib={World.CacheBytes / 1048576}");
        }
        catch (Exception e) { throw new InvalidOperationException("Map smoke failed.", e); }
    }
    private async Task RunRuntimeChecks()
    {
        var session = World.Session;
        var before = session.Snapshot;
        var officer = before.Officers.Where(o => o.SettlementId == 15 && o.OwnerId == 5 && !o.Acted)
            .OrderByDescending(o => o.Abilities.Politics).First();
        if (Options.Screenshot is { } path) await Capture(Path.Combine(Path.GetDirectoryName(path)!, "runtime-before.png"), false);
        var result = session.Develop(new(5, 15, [officer.Id], San9.Core.Domain.DomesticActivity.Commerce));
        if (!result.Accepted || session.Snapshot.Forces.Single(f => f.Id == 5).Money != before.Forces.Single(f => f.Id == 5).Money-50 ||
            session.Snapshot.Settlements.Single(s => s.Id == 15).Commerce <= 300 || session.Snapshot.Clock != before.Clock)
            throw new InvalidOperationException("Runtime development transaction failed.");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (World.StateRevision != session.Snapshot.Revision || Hud.DisplayedMoney != session.Snapshot.Forces.Single(f => f.Id == 5).Money.ToString())
            throw new InvalidOperationException("Runtime map or HUD did not consume the committed snapshot.");
        var after = session.Snapshot;
        if (session.Develop(new(5, 15, [officer.Id], San9.Core.Domain.DomesticActivity.Commerce)).Accepted || session.Snapshot.StateHash != after.StateHash)
            throw new InvalidOperationException("Rejected runtime command changed the world.");
        GD.Print($"RUNTIME_SMOKE_OK development={result.Increment} cost={result.Cost} revision={World.StateRevision} old_snapshot_money={before.Forces.Single(f => f.Id == 5).Money}");
    }
    private async Task WaitForView()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!World.IsVisibleReady)
        {
            if (!World.HasCompleteView) throw new InvalidOperationException("Presented viewport has missing terrain.");
            if (World.LoadError is not null || watch.Elapsed.TotalSeconds > 30) throw new InvalidOperationException("Viewport preparation failed or timed out.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task RunNavigationChecks()
    {
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
            await Capture(Path.Combine(Path.GetDirectoryName(path)!, "navigation-waiting.png"), false);
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
            await Capture(Path.Combine(Path.GetDirectoryName(targetPath)!, "navigation-target.png"), false);
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
    private async Task CaptureUiVariants(string path)
    {
        string Variant(string suffix) => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + suffix + ".png");
        bool boundaries = World.ShowBoundaries;
        World.ShowBoundaries = false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture(Variant("-boundaries-off"), false);
        World.ShowBoundaries = boundaries;
        Hud.Overview.Open();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture(Variant("-overview"), false);
        Hud.Overview.Toggle(San9.Application.Presentation.OverviewLayer.Forces);
        Hud.Overview.Toggle(San9.Application.Presentation.OverviewLayer.Facilities);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture(Variant("-overview-terrain"), false);
        Hud.Overview.Toggle(San9.Application.Presentation.OverviewLayer.Forces);
        Hud.Overview.Toggle(San9.Application.Presentation.OverviewLayer.Facilities);
        Hud.Overview.Close();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Hud.FocusFacility(Hud.Session.View.Forces.Single(f => f.Id == Hud.Session.View.PlayerForceId).LeaderSettlementId!.Value);
        await WaitForView();
        foreach (var (suffix, point) in new[] { ("-hover", new Vector2(726, 28)), ("-disabled-hover", new Vector2(974, 698)) })
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(Variant(suffix), false);
        }
        await Hud.CaptureFacilityStatesAsync(async suffix =>
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(Variant(suffix), false);
        });
        Selection.Clear();
        foreach (var (suffix, id) in new[] { ("-region-gate", 99), ("-region-port", 100) })
        {
            var building = World.Package!.Buildings.Single(b => b.Id == id);
            var ground = MapGeometry.Center(building.X, building.Y);
            World.FocusWorld(new(ground.X, ground.Y));
            await WaitForView();
            var pointer = World.WorldToViewport(new(ground.X, ground.Y));
            GetViewport().PushInput(new InputEventMouseMotion { Position = pointer, GlobalPosition = pointer }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(Variant(suffix), false);
        }
        var initial = MapGeometry.Center(89, 64); World.FocusWorld(new(initial.X, initial.Y));
        await WaitForView();
        GetViewport().PushInput(new InputEventMouseMotion { Position = new(512, 400), GlobalPosition = new(512, 400) }, true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Capture(string path, bool quit = true)
    {
        // Two settled draw frames also discover resources requested by custom _Draw methods.
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        int settled = 0;
        while (settled < 2)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (World.Textures.ActiveError is { } error) throw error;
            settled = World.Textures.ActiveReady && World.IsVisibleReady ? settled + 1 : 0;
            if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("Texture capture readiness failed.");
        }
        GD.Print(World.Textures.Statistics);
        using var image = GetViewport().GetTexture().GetImage();
        GD.Print($"MAP_CAPTURE_GEOMETRY window={GetWindow().Size} gpu_content={image.GetWidth()}x{image.GetHeight()} logical={GetViewport().GetVisibleRect().Size}");
        string absolute = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var result = image.SavePng(absolute); GD.Print($"MAP_CAPTURE {result}: {absolute}");
        if (Options.Quit && quit) GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
