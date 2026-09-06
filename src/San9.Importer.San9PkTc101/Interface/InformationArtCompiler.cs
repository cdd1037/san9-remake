using San9.Content.Interface;
using San9.Content.Images;
using San9.Content.Scenarios;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Interface;

public static class InformationArtCompiler
{
    public static InterfaceSkin Compile(InterfaceSkin skin, byte[] scenarioBytes, ScenarioInitialState scenario,
        string dataDirectory, Func<string, byte[]> read, Func<string, string, PngImage, ImageAsset> save)
    {
        using var stream = new MemoryStream(scenarioBytes);
        var people = San9ScenarioPersonArchive.ReadKnownRecords(stream);
        stream.Position = 0;
        var treasures = San9ScenarioItemArchive.ReadKnownRecords(stream);
        var faces = IndexedImageArchive.ReadImages(read(Path.Combine(dataDirectory, "G_FaceL.s9")), 786);
        var facePalettes = IndexedImageArchive.ReadPalettes(read(Path.Combine(dataDirectory, "P_Face.s9")), 2);
        var still = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_Still.s9")), 283, 283);
        List<ImageAsset> assets = [.. skin.Assets]; var roles = new Dictionary<string, string>(skin.Roles);
        void Add(string role, IndexedImage source, byte[] palette)
        {
            var rgb = IndexedImageArchive.Decode(source, palette, false, false);
            byte[] rgba = new byte[source.Width * source.Height * 4];
            for (int i = 0; i < source.Width * source.Height; i++)
            { rgb.Pixels.AsSpan(i * 3, 3).CopyTo(rgba.AsSpan(i * 4, 3)); rgba[i * 4 + 3] = 255; }
            string id = "ui-" + role;
            assets.Add(save(id, role + ".png", new(source.Width, source.Height, 4, rgba))); roles.Add(role, id);
        }
        foreach (var person in scenario.Officers)
        {
            int portrait = people[person.State.Id].PortraitResourceCode;
            if (portrait >= faces.Length) throw new InvalidDataException("Unsupported expansion portrait in this scenario.");
            Add($"information-person-{person.State.Id}", faces[portrait], facePalettes[0]);
        }
        foreach (var treasure in scenario.Information.Treasures)
        {
            int image = treasures[treasure.Id].ImageId + 172;
            Add($"information-treasure-{treasure.Id}", still.Images[image], still.Palettes[image]);
        }
        return skin with { Assets = assets.ToArray(), Roles = roles };
    }
}
