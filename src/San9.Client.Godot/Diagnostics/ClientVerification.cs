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
            if (Options.Smoke) await RunSmoke();
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
            World._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(512, 384) });
            Check(Selection.Square == (89, 64), "Center picking failed.");
            var before = World.ViewportToWorld(new(700, 400));
            World._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new(700, 400) });
            Check(World.ZoomFactor > 1 && World.ViewportToWorld(new(700, 400)).DistanceTo(before) < 0.01f, "Mouse-centered zoom failed.");
            var selected = Selection.Square;
            World._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(-20, 300) });
            Check(selected == Selection.Square, "Letterbox input changed selection.");
            World._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new(700, 400) });
            var previous = World.CameraPosition;
            World._UnhandledInput(new InputEventMouseMotion { Position = new(700, 400), Relative = new(120, 60) });
            Check(previous.DistanceTo(World.CameraPosition) > 50, "Drag failed.");
            World.SetZoom(0.01f); Check(World.ZoomFactor == 0.25f, "Minimum zoom failed.");
            World.SetZoom(20); Check(World.ZoomFactor == 2, "Maximum zoom failed.");
            World.FocusWorld(new(-10000, -10000)); Check(World.CameraPosition.X > 0 && World.CameraPosition.Y > 0, "Camera limits failed.");
            World.SetZoom(1); World.FocusWorld(new(origin.X, origin.Y));
            foreach (string season in new[] { "winter", "summer", "autumn", "spring" })
            {
                World.PreviewSeason(season); var timeout = System.Diagnostics.Stopwatch.StartNew();
                do { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("Season switch timeout."); } while (World.IsLoading);
                Check(World.LoadError is null && World.Season == season, "Season switch failed.");
            }
            List<double> frameTimes = [];
            var frameWatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 120; i++)
            {
                World.FocusWorld(new(origin.X + i * 18, origin.Y + i * 6));
                double previousTime = frameWatch.Elapsed.TotalMilliseconds;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frameTimes.Add(frameWatch.Elapsed.TotalMilliseconds - previousTime);
            }
            frameTimes.Sort();
            Check(World.CacheBytes <= 256L * 1024 * 1024, "Chunk cache exceeded its budget.");
            GD.Print($"MAP_PAN frames=120 median_ms={frameTimes[60]:F2} p95_ms={frameTimes[114]:F2} peak_working_set_mib={System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1048576}");
            World.FocusWorld(new(origin.X, origin.Y));
            if (Options.UiSmoke) await Hud.RunSmokeAsync();
            GD.Print($"MAP_SMOKE_OK picking zoom drag bounds seasons; cache_mib={World.CacheBytes / 1048576}");
        }
        catch (Exception e) { throw new InvalidOperationException("Map smoke failed.", e); }
    }
    private async Task CaptureUiVariants(string path)
    {
        string Variant(string suffix) => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + suffix + ".png");
        bool boundaries = World.ShowBoundaries;
        World.ShowBoundaries = false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture(Variant("-boundaries-off"), false);
        World.ShowBoundaries = boundaries; World.SetZoom(.5f);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("Zoom capture readiness failed.");
        } while (!World.IsVisibleReady);
        await Capture(Variant("-small"), false);
        World.SetZoom(1);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Hud.FocusFacility(Selection.PlayerFacilities[0].Id);
        foreach (var (suffix, point) in new[] { ("-hover", new Vector2(726, 28)), ("-disabled-hover", new Vector2(974, 698)) })
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(Variant(suffix), false);
        }
        Selection.Clear();
        foreach (var (suffix, id) in new[] { ("-region-gate", 99), ("-region-port", 100) })
        {
            var building = World.Package!.Buildings.Single(b => b.Id == id);
            var ground = MapGeometry.Center(building.X, building.Y);
            World.FocusWorld(new(ground.X, ground.Y));
            var pointer = World.WorldToViewport(new(ground.X, ground.Y));
            GetViewport().PushInput(new InputEventMouseMotion { Position = pointer, GlobalPosition = pointer }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(Variant(suffix), false);
        }
        var initial = MapGeometry.Center(89, 64); World.FocusWorld(new(initial.X, initial.Y));
        GetViewport().PushInput(new InputEventMouseMotion { Position = new(512, 400), GlobalPosition = new(512, 400) }, true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Capture(string path, bool quit = true)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        GD.Print($"MAP_CAPTURE_GEOMETRY window={GetWindow().Size} gpu_content={image.GetWidth()}x{image.GetHeight()} logical={GetViewport().GetVisibleRect().Size}");
        string absolute = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var result = image.SavePng(absolute); GD.Print($"MAP_CAPTURE {result}: {absolute}");
        if (Options.Quit && quit) GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
