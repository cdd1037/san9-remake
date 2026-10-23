namespace San9.Importer.San9PkTc101.Text;

/// <summary>Names the TC101 dialogue contexts without exposing source scenario numbers to runtime content.</summary>
public static class ScenarioMessageTags
{
    public const string TimelessHeroes = "timeless-heroes-dialogue";

    public static string Opening(int id) => id switch
    {
        0 => "yellow-turban-rebellion",
        2 => "warlord-expansion",
        3 => "guandu-campaign",
        4 => "red-cliffs-campaign",
        5 => "tong-pass-campaign",
        6 => "hanzhong-kingship",
        7 => "southern-campaign",
        8 => "wuzhang-plains-campaign",
        15 => "zhou-yu-survives",
        _ => throw new InvalidDataException($"Unmapped source dialogue context: {id}.")
    };

    public static string[] For(int id, int mode) =>
        (new[] { 0, 2, 3, 4, 5, 6, 7, 8, 15 }.Contains(id) ? new[] { Opening(id) } : Array.Empty<string>())
        .Concat(mode == 1 ? new[] { TimelessHeroes } : []).ToArray();
}
