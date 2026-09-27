namespace San9.Rendering;

/// <summary>Presentation-only warning pulses shared by map durability and morale bars.</summary>
public static class MapInfoBarAnimation
{
    public static int Frame(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        return (int)(Math.Floor(milliseconds / 125) % 8);
    }

    public static (float Normal, float Warning, float Critical) Sample(int current, int maximum,
        double milliseconds, int objectId)
    {
        string state = BuildingInfoGeometry.FillState(current, maximum);
        if (state == "normal") return (1, 0, 0);
        // Stable object identity keeps both building bars together across snapshot/viewport changes.
        int phase = (Frame(milliseconds) + (int)((uint)objectId * 11 % 8)) % 8;
        float alert = phase switch
        {
            0 or 1 => 0,
            2 or 7 => 55f / 255,
            3 or 6 => 200f / 255,
            _ => 1
        };
        return state == "warning" ? (1, alert, 0) : (1 - alert, 0, alert);
    }
}
