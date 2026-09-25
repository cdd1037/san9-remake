using Godot;

namespace San9.Client.Godot.Diagnostics;

internal static class VerificationSupport
{
    private const double TimeoutSeconds = 30;

    public static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task Frames(Node owner, int count = 1)
    {
        for (int frame = 0; frame < count; frame++)
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    public static async Task WaitForView(Node owner, MapWorld world, bool requirePresentedView = true)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!world.IsVisibleReady)
        {
            if (world.LoadError is { } error) throw new InvalidOperationException($"Map preparation failed: {error}");
            Check(!requirePresentedView || world.HasCompleteView, "Presented viewport has missing terrain.");
            if (watch.Elapsed.TotalSeconds > TimeoutSeconds) throw new TimeoutException("Map preparation timed out.");
            await Frames(owner);
        }
    }

    public static async Task Capture(Node owner, string path, TextureService textures, MapWorld? world = null)
    {
        Check(DisplayServer.GetName() != "headless", "Screenshots require graphical rendering.");
        // Custom draw methods may request textures; capture only after two settled draw frames.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int settled = 0;
        while (settled < 2)
        {
            await owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (textures.ActiveError is { } error) throw error;
            if (world?.LoadError is { } mapError) throw new InvalidOperationException($"Map preparation failed: {mapError}");
            settled = textures.ActiveReady && (world is null || world.IsVisibleReady) ? settled + 1 : 0;
            if (watch.Elapsed.TotalSeconds > TimeoutSeconds) throw new TimeoutException("Capture readiness timed out.");
        }
        string absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        using var image = owner.GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(absolute);
        if (result != Error.Ok) throw new IOException($"Capture failed ({result}): {absolute}");
        GD.Print(textures.Statistics);
        GD.Print($"VERIFICATION_CAPTURE window={owner.GetWindow().Size} gpu_content={image.GetWidth()}x{image.GetHeight()} logical={owner.GetViewport().GetVisibleRect().Size} path={absolute}");
    }
}
