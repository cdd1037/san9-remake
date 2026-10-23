using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

public static class OpeningSettingsPresentationCompiler
{
    public static OpeningSettingsPresentation Compile(TextureLibrary library)
    {
        string Asset(string archive, int index) => library.Archives.Single(a => a.Id == archive)
            .Variants.Single(v => v.ImageIndex == index).Image.Id;
        StartMenuSprite Start(int index, int x, int y, int columns = 1) => new(Asset("g_start", index), x, y, columns);
        StartMenuSprite Selection(int index, int x, int y) => new(Asset("g_start2", index), x, y);
        // RE-0071: original settings chrome and animation. The merged page moves the combat section down two rows.
        return new([Start(38, 448, 48), new(Asset("g_strtpk", 2), 0, 0), Start(20, 0, 640)],
            [Selection(76, 0, 180), Selection(77, 0, 264)], Asset("g_start2", 75),
            new([Asset("g_start2", 66)], 8, 452, 96, .25), Start(27, 0, 348, 4),
            Start(25, 744, 672, 4), Start(26, 856, 672, 4), new(Asset("g_strtpk", 15), 316, 183));
    }
}
