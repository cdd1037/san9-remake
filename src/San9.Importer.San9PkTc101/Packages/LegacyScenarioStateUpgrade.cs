using System.Collections.Immutable;
using System.Text.Json;
using San9.Importer.San9PkTc101.Scenarios;
using System.Text.Json.Nodes;
using San9.Content.Packages;
using San9.Content.Scenarios;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Upgrades the compiled scenario-state format produced by TC101 content schema 243.</summary>
public static class LegacyScenarioStateUpgrade
{
    public static ScenarioInitialState Convert(JsonObject source, JsonObject text, bool showOfficerAges)
    {
        if (source["schemaVersion"]?.GetValue<int>() != 151)
            throw new InvalidDataException("Expected legacy scenario-state schema 151.");
        var document = (JsonObject)source.DeepClone();
        var entries = text["entries"]?.AsObject() ?? throw new InvalidDataException("Missing legacy text entries.");
        var information = document["information"]?.AsObject() ?? throw new InvalidDataException("Missing information catalog.");
        information["showOfficerAges"] = showOfficerAges;
        foreach (string category in new[] { "ranks", "offices", "tactics", "formations" })
        {
            var items = information[category]?.AsArray() ?? throw new InvalidDataException($"Missing catalog: {category}");
            foreach (var node in items)
            {
                var item = node?.AsObject() ?? throw new InvalidDataException("Missing catalog item.");
                foreach (var (oldKey, newKey) in new[] { ("nameKey", "name"), ("descriptionKey", "description") })
                {
                    string? key = item[oldKey]?.GetValue<string>();
                    if (key is null && oldKey == "descriptionKey") item[newKey] = "";
                    else if (key is null) throw new InvalidDataException($"Missing {oldKey}.");
                    else item[newKey] = entries[key]?.GetValue<string>() ?? throw new InvalidDataException($"Missing legacy text: {key}");
                    item.Remove(oldKey);
                }
                int id = item["id"]!.GetValue<int>();
                if (category == "tactics") item["battleAppearanceId"] = id is <= 11 or 23 or >= 25 and <= 38 ? id : null;
                if (category == "formations") item["mapAppearanceId"] = id;
            }
        }
        if (document["units"]?.AsArray().Count != 0)
            throw new InvalidDataException("Legacy initial field units require explicit position migration.");
        // The schema 151 runtime returned zero for absent force-loyalty entries.
        document["defaultLoyalty"] = 0;
        var growth = document["officerGrowth"]?.AsObject() ?? throw new InvalidDataException("Missing officer growth rules.");
        growth["tacticProficiencyCap"] = 1000;
        var formations = document["military"]?["formations"]?.AsArray() ?? throw new InvalidDataException("Missing formations.");
        var catalog = document["information"]?["formations"]?.AsArray() ?? throw new InvalidDataException("Missing formation catalog.");
        foreach (var entry in formations)
        {
            var formation = entry?.AsObject() ?? throw new InvalidDataException("Missing formation.");
            int id = formation["id"]!.GetValue<int>();
            var ranks = formation["captureRanks"]?.AsArray() ?? throw new InvalidDataException("Missing formation ranks.");
            var presentation = catalog.SingleOrDefault(item => item?["id"]?.GetValue<int>() == id)?["presentation"];
            var slots = presentation?["slots"]?.AsArray() ?? throw new InvalidDataException($"Missing formation positions: {id}");
            var positions = new JsonArray();
            if (slots.Count == 0 && id is >= 20 and <= 26 && ranks.Count == 5)
            {
                for (int i = 0; i < ranks.Count; i++) positions.Add(new JsonObject { ["column"] = i, ["rank"] = 1 });
            }
            else
            {
                if (slots.Count != ranks.Count) throw new InvalidDataException($"Formation position count differs: {id}");
                for (int i = 0; i < slots.Count; i++)
                {
                    int rank = ranks[i]!.GetValue<int>();
                    if (slots[i]?["row"]?.GetValue<int>() != rank) throw new InvalidDataException($"Formation rank differs: {id}");
                    positions.Add(new JsonObject { ["column"] = slots[i]!["column"]!.GetValue<int>(), ["rank"] = rank });
                }
            }
            formation.Remove("captureRanks");
            formation["positions"] = positions;
        }
        document["schemaVersion"] = ScenarioInitialState.CurrentSchemaVersion;
        var result = document.Deserialize<ScenarioInitialState>(PackageFiles.JsonOptions)
            ?? throw new InvalidDataException("Missing scenario state.");
        result = result with { Officers = result.Officers.Select(officer => officer with
            { State = officer.State.WithLoyaltyDefault(result.DefaultLoyalty) }).ToArray() };
        var military = result.Military ?? throw new InvalidDataException("Missing military rules.");
        military = military with
        {
            TroopTypes = MixedTroopPreset.TroopTypes([0, 1, 2], [3, 4, 5], [6, 7, 8], [9, 10, 11]),
            Armaments = ArmamentPreset.Basic(),
            NavalTraining = MixedTroopPreset.NavalTraining(31, 32, 33, 34),
            SiegeEquipment = MixedTroopPreset.SiegeEquipment(8, 9, 10, 11),
            MixedTroopCombat = MixedTroopPreset.Combat(),
            ResearchProjects = MixedTroopPreset.TroopResearchProjects(),
            BasicNavalFormationId = MilitaryContentCompiler.BasicNavalFormationId
        };
        military = MilitaryContentCompiler.AddMixedTroopFormations(military);
        var catalogItems = result.Information.Formations;
        var added = new[] { (Id: 28, Name: "偃月", Source: 7), (Id: 29, Name: "衡轭", Source: 4),
            (Id: 30, Name: "钩行", Source: 1), (Id: 31, Name: "车悬", Source: 2) };
        var extra = added.Select(item =>
        {
            var sourceFormation = catalogItems.Single(f => f.Id == item.Source);
            var presentation = sourceFormation.Presentation ?? throw new InvalidDataException("Missing formation presentation.");
            return new FormationCatalogDefinition(item.Id, item.Name)
            {
                Description = MixedTroopPreset.FormationDescription(item.Id - 28),
                MapAppearanceId = item.Source,
                Presentation = new(3, 3, MixedTroopPreset.FormationPositions(item.Id - 28)
                    .Select(p => new FormationSlot(p.Column, (int)p.Rank)).ToImmutableArray(),
                    presentation.VisionRadius, presentation.VisionAngleDegrees, null, null)
            };
        });
        result = result with { Military = military, Information = result.Information with
            { Formations = catalogItems.Concat(extra).ToArray(),
              Equipment = LegacyEquipmentMigration.Convert(result.Information.Treasures, result.Information.TreasureStates, EquipmentPreset.Basic()).Definitions } };
        result = PersonalPeeragePreset.UpgradeLegacy(result);
        result = FeaturedEquipmentPreset.Apply(result);
        result.Validate();
        return result;
    }
}
