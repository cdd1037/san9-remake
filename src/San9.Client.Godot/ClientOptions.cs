using System.Globalization;

namespace San9.Client.Godot;

public sealed record ClientOptions(string? PackageRoot, int? CenterX, int? CenterY, float Zoom, string? Season,
    double? AnimationTime, bool MapSmoke, bool UiSmoke, string? Screenshot, bool Quit)
{
    public string ContentRoot { get; init; } = "";
    public string? ScenarioId { get; init; }
    public int? PlayerForceId { get; init; }
    public string? UserDataRoot { get; init; }
    public int ComputerDamageBonusPercent { get; init; }
    public bool PatrolSmoke { get; init; }
    public bool RuntimeSmoke { get; init; }
    public bool InformationSmoke { get; init; }
    public bool TextSmoke { get; init; }
    public bool RequiresWorldVerification => PatrolSmoke || MapSmoke || UiSmoke || RuntimeSmoke || InformationSmoke || Screenshot is not null;
    public San9.Core.Domain.MapCell ResolveMapCenter(int width, int height, San9.Core.Domain.MapCell? preferred = null)
    {
        var cell = new San9.Core.Domain.MapCell(CenterX ?? preferred?.X ?? width / 2, CenterY ?? preferred?.Y ?? height / 2);
        if (cell.X < 0 || cell.X >= width || cell.Y < 0 || cell.Y >= height)
            throw new ArgumentException("Map center is outside the loaded map.");
        return cell;
    }

    public static ClientOptions Parse(string[] args, string contentRoot)
    {
        string? Argument(string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return null;
            return index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing value for {name}.");
        }
        int bonus = Argument("--computer-damage-bonus") is { } b ? int.Parse(b, CultureInfo.InvariantCulture) : 0;
        ArgumentOutOfRangeException.ThrowIfNegative(bonus);
        int? x = null, y = null;
        if (Argument("--map-center") is {} center)
        {
            var pair = center.Split(',');
            if (pair.Length != 2 || !int.TryParse(pair[0], out int centerX) || !int.TryParse(pair[1], out int centerY) || centerX < 0 || centerY < 0)
                throw new ArgumentException("Invalid map center.");
            x = centerX; y = centerY;
        }
        float zoom = Argument("--map-zoom") is {} z ? float.Parse(z, CultureInfo.InvariantCulture) : 1;
        if (!float.IsFinite(zoom) || zoom <= 0) throw new ArgumentException("Invalid zoom.");
        double? time = Argument("--map-time-ms") is {} t ? double.Parse(t, CultureInfo.InvariantCulture) : null;
        if (time is {} ms && (!double.IsFinite(ms) || ms < 0)) throw new ArgumentException("Invalid animation capture time.");
        string? screenshot = Argument("--map-screenshot");
        var options = new ClientOptions(Argument("--content-package"),
            x, y, zoom, Argument("--map-season"), time, args.Contains("--map-smoke"),
            args.Contains("--ui-smoke"), screenshot is null ? null : Path.GetFullPath(screenshot), args.Contains("--map-quit"))
        {
            ContentRoot = Path.GetFullPath(Argument("--content-root") ?? contentRoot),
            ScenarioId = Argument("--scenario"),
            PlayerForceId = Argument("--player-force") is { } player ? int.Parse(player, CultureInfo.InvariantCulture) : null,
            UserDataRoot = Argument("--user-data-root") is { } dataRoot ? Path.GetFullPath(dataRoot) : null,
            ComputerDamageBonusPercent = bonus,
            PatrolSmoke = args.Contains("--patrol-smoke"),
            RuntimeSmoke = args.Contains("--runtime-smoke"),
            InformationSmoke = args.Contains("--information-smoke"),
            TextSmoke = args.Contains("--text-smoke")
        };
        if (options.PackageRoot is not null && options.ScenarioId is not null)
            throw new ArgumentException("Specify either --scenario or --content-package.");
        if (options.TextSmoke && options.RequiresWorldVerification)
            throw new ArgumentException("Text verification runs separately from world verification and map screenshots.");
        return options;
    }
}
