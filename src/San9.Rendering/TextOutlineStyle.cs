namespace San9.Rendering;

/// <summary>Shared native outline calibration for text adapters and inline markup.</summary>
public static class TextOutlineStyle
{
    // Adapter units, not pixels; see DEC-UI-0002 for the calibrated geometric radius.
    public const int Size = 4;
    public const float Alpha = .57f;
    public static byte AlphaByte => (byte)MathF.Round(Alpha * byte.MaxValue);
}
