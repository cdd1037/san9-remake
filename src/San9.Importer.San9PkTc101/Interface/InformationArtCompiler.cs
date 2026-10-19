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
        // RE-0111: image-control mode 2 code 26 resolves to G_Still 71.
        roles.Add("personnel-city-transfer",Image("g_still",71));
        // RE-0182: surrender result mode2 code19 resolves to G_Still64.
        roles.Add("diplomacy-eliminated",Image("g_still",64));
        roles.Add("event-founded",Image("g_still",65));
        roles.Add("event-consolidation",Image("g_stilpk",0));
        roles.Add("event-court",Image("g_still",68));
        roles.Add("capture-settlement",Image("g_still",69));
        roles.Add("capture-fortification",Image("g_still",70));
        roles.Add("event-visit",Image("g_still",57));
        roles.Add("event-alliance-victory",Image("g_still",67));
        foreach (var person in scenario.Officers)
        {
            int portrait = people[person.State.Id].PortraitResourceCode;
            string image = portraits.Single(p => p.Id == portrait).Image.Id;
            roles.Add($"information-person-{person.State.Id}", image);
            roles.Add($"catalog-person-{person.State.Id}", image);
        }
        foreach (var treasure in scenario.Information.Treasures)
            roles.Add($"information-treasure-{treasure.Id}", Image("g_still", treasures[treasure.Id].ImageId + 172));
        // RE-0137: 0x423B10 resolves fixed-building illustrations through 0x653210.
        int[] workImages = [276, 281, 282, 277, 278, 279, 274, 275, 272, 273, 280];
        foreach (var facility in scenario.Military!.Facilities.Where(f => f.Buildable))
            roles.Add($"catalog-facility-{facility.Id}", Image("g_still", workImages[facility.Id-6]));
        // Original formation catalog order maps to the G_Jinkei three-state diagram groups.
        int[] diagrams = [9, 12, 3, 6, 15, 18, 0, 21, 42, 45, 39, 36, 48, 48, 48, 48, 48, 48, 48, 48, 24, 27, 30, 33, 48, 48, 48, 48];
        foreach (var formation in scenario.Information.Formations)
        {
            int diagram = diagrams[formation.Id];
            roles.Add($"formation-diagram-{formation.Id}", Image("g_jinkei", diagram));
            roles.Add($"formation-available-{formation.Id}", Image("g_jinkei", diagram + 1));
        }
        foreach (var (role, index, palette) in new[] { ("formation-member", 51, 1), ("formation-commander", 52, 1),
            ("formation-panel-deployment", 65, 3), ("formation-panel-detail", 67, 3) })
            roles.Add(role, library.Archives.Single(a => a.Id == "g_jinkei").Variants.Single(v => v.ImageIndex == index && v.PaletteIndex == palette).Image.Id);
        return roles;
    }
}
