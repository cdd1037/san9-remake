using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Content.Interface;

namespace San9.Content.Packages;

public sealed record ClientContentPackage(int SchemaVersion, ContentSource[] Sources, MapPackage Map, ScenarioInitialState Scenario, TextCatalog Text, InterfaceSkin Skin)
{
    public void ValidateLinks()
    {
        if (SchemaVersion != 18) throw new InvalidDataException("Unsupported client content version.");
        Scenario.Validate(); Text.Validate(); Skin.Validate();
        var markup = Text.Entries.Values.SelectMany(e => new[] { e.Text, e.SourceText })
            .Concat(MessageGraph.Walk(Text.Messages.Values.SelectMany(m => m.Nodes)).SelectMany(n => n switch
            {
                MessageLiteral l => new[] { l.BbCode, l.SourceBbCode },
                MessageFormat f => new[] { f.Open, f.Close },
                MessageValue v => new[] { v.Prefix, v.Suffix }, _ => []
            }));
        foreach (string icon in markup.SelectMany(MessageMarkup.IconIds).Distinct())
            if (!Skin.Roles.ContainsKey($"text-icon-{icon}-16") && !Skin.Roles.ContainsKey($"text-icon-{icon}-24"))
                throw new InvalidDataException($"Missing inline image: {icon}");
        foreach (var treasure in Scenario.Information.Treasures) _ = Text.ResolveRich(TreasureText.DescriptionKey(treasure.Id));
        foreach (string key in Scenario.Officers.Select(o => o.BiographyKey).OfType<string>()) _ = Text.ResolveRich(key);
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
        foreach (string key in Scenario.Forces.Select(f => f.NameKey).Concat(Scenario.Settlements.Select(s => s.NameKey)).Concat(Scenario.RegionNameKeys.Values).Concat(Scenario.Officers.SelectMany(o => new[] { o.NameKey, o.CourtesyNameKey }.OfType<string>())).Concat(Scenario.Information.Ranks.Select(r => r.NameKey)).Concat(Scenario.Information.Offices.Select(o => o.NameKey)).Concat(Scenario.Information.Treasures.Select(t => t.NameKey)))
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
