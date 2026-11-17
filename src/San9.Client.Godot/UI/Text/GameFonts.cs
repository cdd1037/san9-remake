using Godot;

namespace San9.Client.Godot;

internal sealed record InstalledFont(string Family, string DisplayName, bool? ChineseMarked);

internal static class GameFonts
{
    internal static string[] InstalledFamilies() => OS.GetSystemFonts()
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static InstalledFont[] InstalledChoices()
    {
        var choices = new List<InstalledFont>();
        var files = new Dictionary<string, FontFaceMetadata[]>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string family in InstalledFamilies())
        {
            string path = OS.GetSystemFontPath(family);
            if (!files.TryGetValue(path, out var faces))
            {
                try { faces = path.Length == 0 ? [] : OpenTypeFontMetadata.Read(path); }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
                { faces = []; }
                files.Add(path, faces);
            }
            var face = faces.Where(candidate => candidate.Families.Contains(family, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.Regular).FirstOrDefault();
            string name = face?.ChineseName ?? "";
            choices.Add(new(family, name.Length == 0 || name == family ? family : $"{name}（{family}）",
                face?.ChineseMarked));
        }
        return choices.OrderBy(choice => choice.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static Font Create(string family, Font fallback) => family.Length == 0 ? fallback : new SystemFont
    {
        FontNames = [family], Fallbacks = [fallback], AllowSystemFallback = false
    };

    internal static void ApplyAtStartup(string family)
    {
        if (family.Length == 0) return;
        if (!InstalledFamilies().Contains(family, StringComparer.OrdinalIgnoreCase))
        {
            GD.PushWarning($"本机字体不可用，使用内置字体：{family}");
            return;
        }
        var font = GD.Load<FontVariation>("res://assets/GameFont.tres");
        font.BaseFont = Create(family, font.BaseFont);
    }
}
