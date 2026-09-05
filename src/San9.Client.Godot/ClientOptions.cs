using System.Globalization;

namespace San9.Client.Godot;

public sealed record ClientOptions(string PackageRoot, int CenterX, int CenterY, float Zoom, string? Season,
    double? AnimationTime, bool Smoke, bool UiSmoke, string? Screenshot, bool Quit)
{
    public static ClientOptions Parse(string[] args, string projectRoot)
    {
        string? Argument(string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return null;
            return index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing value for {name}.");
        }
        int x = 89, y = 64;
        if (Argument("--map-center") is {} center)
        {
            var pair = center.Split(',');
            if (pair.Length != 2 || !int.TryParse(pair[0], out x) || !int.TryParse(pair[1], out y) || x is < 0 or >= 200 || y is < 0 or >= 200)
                throw new ArgumentException("Invalid map center.");
        }
        float zoom = Argument("--map-zoom") is {} z ? float.Parse(z, CultureInfo.InvariantCulture) : 1;
        if (!float.IsFinite(zoom) || zoom <= 0) throw new ArgumentException("Invalid zoom.");
        double? time = Argument("--map-time-ms") is {} t ? double.Parse(t, CultureInfo.InvariantCulture) : null;
        if (time is {} ms && (!double.IsFinite(ms) || ms < 0)) throw new ArgumentException("Invalid animation capture time.");
        return new(Argument("--content-package") ?? Path.GetFullPath(Path.Combine(projectRoot, "../../local/generated/content/scenario-190")),
            x, y, zoom, Argument("--map-season"), time, args.Contains("--map-smoke") || args.Contains("--ui-smoke"),
            args.Contains("--ui-smoke"), Argument("--map-screenshot"), args.Contains("--map-quit"));
    }
}
