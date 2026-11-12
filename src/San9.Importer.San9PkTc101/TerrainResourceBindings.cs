namespace San9.Importer.San9PkTc101;

internal static class TerrainResourceBindings
{
    public static int? Palette(int id) => id switch
    {
        >= 10006 and <= 10197 => 0,
        >= 10198 and <= 10254 => 1,
        >= 10255 and <= 10311 => 2,
        >= 10312 and <= 10314 => 3,
        >= 10315 and <= 10330 => 4 + (id - 10315) % 4,
        >= 10331 and <= 10522 => 8,
        >= 10523 and <= 10524 => 9,
        >= 10525 and <= 10716 => 10,
        >= 10717 and <= 10773 => 11,
        >= 10774 and <= 10776 => 12,
        _ => null
    };
}
