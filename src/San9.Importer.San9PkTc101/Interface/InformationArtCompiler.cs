using San9.Content.Scenarios;
using San9.Content.Images;

namespace San9.Importer.San9PkTc101.Interface;

/// <summary>Resolves scenario illustrations to the source catalog; never decodes or writes an image.</summary>
public static class InformationArtCompiler
{
    public static Dictionary<string,string> Compile(byte[] scenarioBytes, ScenarioInitialState scenario, TextureLibrary library, PortraitDefinition[] portraits)
    {
        using var stream = new MemoryStream(scenarioBytes);
        var people = San9ScenarioPersonArchive.ReadKnownRecords(stream);
        stream.Position = 0;
        var treasures = San9ScenarioItemArchive.ReadKnownRecords(stream);
        Dictionary<string,string> roles = [];
        string Image(string archive, int index) => library.Archives.Single(a => a.Id == archive)
            .Variants.Single(v => v.ImageIndex == index && v.PaletteIndex == (archive == "g_still" ? index : 0)).Image.Id;
        foreach (var person in scenario.Officers)
        {
            int portrait = people[person.State.Id].PortraitResourceCode;
            string image = portraits.Single(p => p.Id == portrait).Image.Id;
            roles.Add($"information-person-{person.State.Id}", image);
            roles.Add($"catalog-person-{person.State.Id}", image);
        }
        foreach (var treasure in scenario.Information.Treasures)
            roles.Add($"information-treasure-{treasure.Id}", Image("g_still", treasures[treasure.Id].ImageId + 172));
        return roles;
    }
}
