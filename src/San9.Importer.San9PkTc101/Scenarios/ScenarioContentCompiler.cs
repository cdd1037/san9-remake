using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class ScenarioContentCompiler
{
    public static (ScenarioInitialState Scenario, TextCatalog Text) Compile(byte[] exe, byte[] scenario, byte[] messages)
    {
        T Read<T>(Func<Stream, T> parser) { using var stream = new MemoryStream(scenario); return parser(stream); }
        var header = Read(San9ScenarioArchive.ReadInitialScenario190Header);
        var cities = Read(San9ScenarioCityArchive.ReadKnownRecords);
        var zones = Read(San9ScenarioZoneArchive.ReadKnownRecords);
        var forces = Read(San9ScenarioForceArchive.ReadKnownRecords);
        var persons = Read(San9ScenarioPersonArchive.ReadKnownRecords);
        var items = Read(San9ScenarioItemArchive.ReadKnownRecords);
        var offices = Read(San9ScenarioOfficeArchive.ReadKnownRecords);
        var fixedBuildings = Read(San9ScenarioFixedBuildingArchive.ReadKnownRecords);
        using var text = new ScenarioTextReader(messages, exe);
        int? Root(int? id)
        {
            if (id is null) return null;
            HashSet<int> visited = [];
            int current = id.Value;
            while (true)
            {
                if (current < 0 || current >= forces.Count || !visited.Add(current)) throw new InvalidDataException("Invalid force ownership chain.");
                int next = forces[current].RuntimeOwningForceId;
                if (next == current) return current;
                current = next;
            }
        }
        int Morale(int? owner, uint soldiers) => owner is int f && soldiers > 0 ? Math.Clamp((int)Math.Sqrt(forces[f].Trust) + 40, 0, 100) : 0;
        int? emperorCity = new ushort[] { 563, 564, 565 }.Select(code => persons.FirstOrDefault(p => p.BiographyResourceCode == code && p.StatusCode != 8))
            .FirstOrDefault(p => p is not null)?.LocationId;
        int[] trainingCities = [0, 3, 8, 14, 17, 20, 25, 36, 42];
        List<SettlementInitialState> settlements = [];
        Dictionary<int, string> regions = [];
        Dictionary<int, int> regionCities = [];
        for (int i = 0; i < cities.Count; i++)
        {
            var c = cities[i]; string key = text.City($"region.{i}", c.AuxiliaryId1); regions.Add(i, key); regionCities.Add(i, i);
            if (c.Durability == ushort.MaxValue || c.MaxDurability == ushort.MaxValue) continue;
            settlements.Add(new(i, key, Root(c.ForceId), c.X, c.Y, c.Soldiers, c.Durability, c.MaxDurability,
                Morale(Root(c.ForceId), c.Soldiers),
                (c.HasTrader ? new[] { "merchant" } : trainingCities.Contains(i) ? ["training"] : Array.Empty<string>())
                    .Concat(emperorCity == i ? ["emperor"] : Array.Empty<string>()).ToArray())
            { Appearance = c.TerrainCode, PublicOrder = c.PublicOrder, Commerce = c.Income, MaxCommerce = c.MaxIncome, Cultivation = c.Harvest, MaxCultivation = c.MaxHarvest });
        }
        foreach (var z in zones)
        {
            int id = 50 + z.Id; if (z.CityId is int city) regionCities.Add(id, city); regions.Add(id, text.Region($"region.{id}", z.NameResourceCode));
            if (z.FixedBuildingId is not int type || z.Durability == ushort.MaxValue) continue;
            string name = text.Settlement($"settlement.{id}", z.NameResourceCode, type);
            settlements.Add(new(id, name, Root(z.ForceId), z.X, z.Y, z.Soldiers, z.Durability, fixedBuildings[type].MaxDurability, Morale(Root(z.ForceId), z.Soldiers), []));
        }
        var summaries = settlements.Where(s => s.OwnerId is not null).Select(s => s.OwnerId!.Value).Append(5).Distinct().Order().Select(id =>
        {
            var f = forces[id];
            if (f.LeaderId is not int leader || leader >= persons.Count) throw new InvalidDataException("Missing force leader.");
            var p = persons[leader];
            string name = text.Person($"force.{id}", p.FamilyNameResourceCode, p.GivenNameResourceCode);
            return new ForceInitialState(id, name, f.ColorCode, f.Money, f.Food, f.Trust) { FlagGlyph = f.CountryTitleId is int title ? System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x253bb0 + title * 4, 4)) : p.FlagCode < 186 ? p.FlagCode : -1 };
        }).ToArray();
        var catalog = text.Catalog;
        if (catalog.Resolve(summaries.Single(f => f.Id == 5).NameKey) != "董卓") throw new InvalidDataException("Unexpected 190-year player force.");
        var snapshot = new ScenarioInitialState(3, header.ScenarioId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            header.StartDate.Year, header.StartDate.Month, header.StartDate.Day, 5, summaries, settlements.OrderBy(s => s.Id).ToArray(), regions, regionCities)
        {
            Officers = persons.Select((p, id) => (p, id)).Where(x => x.p.StatusCode <= 3 && x.p.LocationId is int location && settlements.Any(s => s.Id == location))
                .Select(x =>
                {
                    var p = x.p; int location = p.LocationId!.Value;
                    int itemBonus = items.Where(i => !i.IsInvalid && i.HolderPersonId == x.id && i.CategoryId is 3 or 4 or 6).Select(i => (int)i.AbilityBonus).DefaultIfEmpty().Max();
                    int officeBonus = p.OfficeRankId is int office && offices[office].HasAbilityBonus(3) ? offices[office].AbilityBonus : 0;
                    int politics = Math.Max(1, p.Politics * (10 - p.Health) / 10) + itemBonus + officeBonus;
                    return new San9.Core.Domain.OfficerState(x.id, settlements.Single(s => s.Id == location).OwnerId, location, politics, p.IsCommandParticipant);
                }).ToArray()
        };
        snapshot.Validate(); catalog.Validate(); return (snapshot, catalog);
    }
}
