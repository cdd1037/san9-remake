namespace San9.Rendering;

// RE-0042: a finite presentation at the new quarter's xun boundary.
// Resource preparation may extend the covered stage, never the simulation date.
public sealed class SeasonChangeAnimation
{
    public const double ResourceSwitchTime = .401;
    public const double FlashStart = .901;
    public const double Duration = 2.641;
    public double Elapsed { get; private set; }
    public bool NeedsNewSeason => Elapsed >= ResourceSwitchTime;
    public bool IsComplete => Elapsed >= Duration;
    public bool ShowsNewBadge => Elapsed >= .400;
    public int CoverFrame => Elapsed < .1 ? 0 : Elapsed < .2 ? 1 : Elapsed < .3 ? 2 : 3;
    public float CoverAlpha => Elapsed < .401 ? 163f / 256 : Elapsed < .501 ? 122f / 256 :
        Elapsed < .601 ? 81f / 256 : Elapsed < .701 ? 40f / 256 : 0;

    public float BadgeBrightness(string season)
    {
        double time = Elapsed - FlashStart;
        if (time < 0 || time >= 1.74) return 1;
        // Three bright pulses, separated and followed by the unmodified badge.
        double cycle = time % .52;
        if (time >= 1.56 || cycle < .18) return 1;
        float first = season switch { "summer" => 1.12f, "winter" => 1.10f, _ => 1.16f };
        float step = season switch { "summer" => .12f, "winter" => .10f, _ => .16f };
        return first + (cycle >= .30 && cycle < .40 ? step : 0);
    }

    public void Advance(double seconds, bool resourcesReady)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        double next = Math.Min(Duration, Elapsed + seconds);
        // Stop once even with a large delta: the owner must request and present the new resources.
        if (Elapsed < ResourceSwitchTime && next >= ResourceSwitchTime) next = ResourceSwitchTime;
        else if (NeedsNewSeason && !resourcesReady) return;
        Elapsed = next;
    }
}
