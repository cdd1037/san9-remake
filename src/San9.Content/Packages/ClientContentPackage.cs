using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Content.Interface;

namespace San9.Content.Packages;

public sealed record ClientContentPackage(int SchemaVersion, ContentSource[] Sources, MapPackage Map, ScenarioInitialState Scenario, TextCatalog Text, InterfaceSkin Skin)
{
    public void ValidateLinks()
    {
        if (SchemaVersion != 10) throw new InvalidDataException("Unsupported client content version.");
        Scenario.Validate(); Text.Validate(); Skin.Validate();
        foreach (string role in StrategyHintText.Roles) _ = Text.Resolve(StrategyHintText.Key(role));
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
        foreach (string key in Scenario.Forces.Select(f => f.NameKey).Concat(Scenario.Settlements.Select(s => s.NameKey)).Concat(Scenario.RegionNameKeys.Values).Concat(Scenario.Officers.Select(o => o.NameKey)).Concat(Scenario.Information.Ranks.Select(r => r.NameKey)).Concat(Scenario.Information.Offices.Select(o => o.NameKey)).Concat(Scenario.Information.Treasures.Select(t => t.NameKey)))
            _ = Text.Resolve(key);
    }
}

public static class ClientContentFiles
{
    public static async Task<ClientContentPackage> LoadAsync(string root, CancellationToken token = default)
    {
        var content = await PackageFiles.ReadAsync<ClientContentPackage>(Path.Combine(root, "content.json"), token);
        content.ValidateLinks();
        MapPackageFiles.Validate(content.Map);
        return content;
    }
}
