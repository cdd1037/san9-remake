using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Content.Interface;

namespace San9.Content.Packages;

public sealed record ClientContentPackage(int SchemaVersion, ContentSource[] Sources, MapPackage Map, ScenarioInitialState Scenario, TextCatalog Text, InterfaceSkin Skin, San9.Content.Images.TextureLibrary Textures)
{
    [System.Text.Json.Serialization.JsonIgnore] public string TextureRoot { get; init; } = "";

    [System.Text.Json.Serialization.JsonIgnore] public San9.Content.Images.PortraitDefinition[] Portraits { get; init; } = [];

    public void ValidateLinks()
    {
        if (SchemaVersion != ScenarioContent.CurrentSchemaVersion) throw new InvalidDataException("Unsupported client content version.");
        if (Text.MessagePeople.Keys.Except(Scenario.Officers.Select(o => o.State.Id)).Any())
            throw new InvalidDataException("Message person profiles must reference scenario officers.");
        var images = Map.Assets.Concat(Map.Seasons.SelectMany(s => s.Textures)).Concat(Skin.Assets).Concat(Textures.Assets).ToArray();
        San9.Content.Images.ImageAssetFiles.ValidateCatalog(images);
        if (Scenario.Military is {} military && military.Formations.Any(f => !Map.Units.Formations.Any(v => v.FormationId == f.Id)))
            throw new InvalidDataException("Missing formation visual definition.");
        if (Map.Units.Formations.Length > 0)
        {
            var clips = Map.Animations.Clips.Select(c => c.Id).ToHashSet();
            foreach (int color in Scenario.Forces.Select(f => f.ColorCode).Distinct())
                foreach (int direction in Enumerable.Range(0, 8))
                    if (!clips.Contains($"unit-flag-{direction}-cloth-{color}"))
                        throw new InvalidDataException($"Missing unit flag color: {color}");
            foreach (int glyph in Scenario.Officers.Select(o => o.FlagGlyph).Where(g => g >= 0).Distinct())
                foreach (int direction in Enumerable.Range(0, 8))
                    if (!clips.Contains($"unit-flag-{direction}-glyph-0-{glyph}") || !clips.Contains($"unit-flag-{direction}-glyph-1-{glyph}"))
                        throw new InvalidDataException($"Missing officer flag glyph: {glyph}");
        }
        foreach (string role in Scenario.Information.Formations.Where(f => f.Presentation is not null)
            .SelectMany(f => new[] { f.Presentation!.BackgroundRole, f.Presentation.AvailableRole }).OfType<string>())
            if (!Skin.Roles.ContainsKey(role)) throw new InvalidDataException($"Missing formation diagram: {role}");
        if (Scenario.Information.Formations.Any(f => f.Presentation is not null))
            foreach (string role in new[] { "formation-member", "formation-commander", "formation-panel-deployment", "formation-panel-detail" })
                if (!Skin.Roles.ContainsKey(role)) throw new InvalidDataException($"Missing formation interface image: {role}");
        Scenario.Validate(); Text.Validate(); Skin.Validate(); Textures.Validate();
        if (Map.BuildingTypes.Any(t => !Skin.BuildingTargetOffsets.ContainsKey(t.Id)))
            throw new InvalidDataException("Missing building target marker placement.");
        var markup = Text.Entries.Values
            .Concat(MessageGraph.Walk(Text.Messages.Values.SelectMany(m => m.Nodes)).SelectMany(n => n switch
            {
                MessageLiteral l => new[] { l.BbCode },
                _ => []
            }));
        foreach (string icon in markup.SelectMany(MessageMarkup.IconIds).Distinct())
            if (!Skin.Roles.ContainsKey($"text-icon-{icon}-16") && !Skin.Roles.ContainsKey($"text-icon-{icon}-24"))
                throw new InvalidDataException($"Missing inline image: {icon}");
        foreach (var treasure in Scenario.Information.Treasures) _ = Text.ResolveRich(TreasureText.DescriptionKey(treasure.Id));
        foreach (string key in Scenario.Officers.Select(o => o.BiographyKey).OfType<string>()) _ = Text.ResolveRich(key);
        foreach (string id in San9.Content.Interface.CommandMenuSkin.EntryIds) _ = Text.ResolveRich(San9.Content.Interface.CommandMenuSkin.DescriptionKey(id));
        foreach (string role in StrategyHintText.Roles) _ = Text.ResolveRich(StrategyHintText.Key(role));
        foreach (string role in Scenario.Officers.SelectMany(o => new[] { $"information-person-{o.State.Id}", $"catalog-person-{o.State.Id}" })
                     .Concat(Scenario.Information.Treasures.Select(t => $"information-treasure-{t.Id}")))
            if (!Skin.Roles.ContainsKey(role)) throw new InvalidDataException($"Missing information illustration: {role}");
        if (Map.ScenarioId != Scenario.ScenarioId ||
            !Map.Buildings.Select(b => b.Id).Order().SequenceEqual(Scenario.Settlements.Select(s => s.Id).Order()))
            throw new InvalidDataException("Scenario and map identities differ.");
        var buildings = Map.Buildings.ToDictionary(b => b.Id);
        foreach (var s in Scenario.Settlements)
            if (s.X != buildings[s.Id].X || s.Y != buildings[s.Id].Y)
                throw new InvalidDataException("Scenario and map coordinates differ.");
        foreach (string key in Scenario.Information.Tactics.Concat(Scenario.Information.Formations).SelectMany(d => new[] { d.NameKey, d.DescriptionKey }.OfType<string>()).Concat(Scenario.Forces.Select(f => f.NameKey)).Concat(Scenario.Settlements.Select(s => s.NameKey)).Concat(Scenario.RegionNameKeys.Values).Concat(Scenario.ProvinceNameKeys.Values).Concat(Scenario.Information.Ranks.Select(r => r.DescriptionKey).OfType<string>()).Concat(Scenario.FacilityNameSuffixKeys.Values).Concat(Scenario.Officers.SelectMany(o => new[] { o.NameKey, o.CourtesyNameKey }.OfType<string>())).Concat(Scenario.Information.CountryTitles.Select(t => t.NameKey)).Concat(Scenario.Information.Ranks.Select(r => r.NameKey)).Concat(Scenario.Information.Offices.Select(o => o.NameKey)).Concat(Scenario.Information.Offices.Select(o => o.DescriptionKey).OfType<string>()).Concat(Scenario.Information.Treasures.Select(t => t.NameKey)))
            _ = Text.Resolve(key);
    }
}

public static class ClientContentFiles
{
    public static async Task<ClientContentPackage> LoadAsync(string root, CancellationToken token = default)
    {
        var instance = await ScenarioContentFiles.LoadAsync(root, token);
        string textureRoot = Path.GetFullPath(Path.Combine(root, instance.Textures.Path));
        var textures = await TexturePackageFiles.LoadAsync(textureRoot, token);
        return Resolve(instance, textures, textureRoot);
    }

    public static ClientContentPackage Resolve(ScenarioContent instance, TexturePackage textures, string textureRoot)
    {
        if (instance.SchemaVersion != ScenarioContent.CurrentSchemaVersion) throw new InvalidDataException("Unsupported scenario content version.");
        if (textures.SourceBuild != instance.Map.SourceBuild) throw new InvalidDataException("Texture source build differs.");
        var roles = new Dictionary<string,string>(textures.Skin.Roles);
        foreach (var role in instance.IllustrationRoles) roles.Add(role.Key, role.Value);
        var skin = textures.Skin with { Roles = roles, Assets = textures.Skin.Assets.Concat(textures.Library.Assets).Concat(textures.Portraits.Select(p => p.Image)).ToArray() };
        var content = new ClientContentPackage(ScenarioContent.CurrentSchemaVersion, instance.Sources, instance.Map.Resolve(textures.Map), instance.Scenario,
            instance.Text, skin, textures.Library) { TextureRoot = textureRoot, Portraits = textures.Portraits };
        content.ValidateLinks();
        MapPackageFiles.Validate(content.Map);
        return content;
    }
}
