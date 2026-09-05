using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class ScenarioContentCompiler
{
    public static (ScenarioSnapshot Scenario, TextCatalog Text) Compile(byte[] exe, byte[] scenario, byte[] messages)
    {
        T Read<T>(Func<Stream, T> parser) { using var stream = new MemoryStream(scenario); return parser(stream); }
        var header = Read(San9ScenarioArchive.ReadInitialScenario190Header);
        var cities = Read(San9ScenarioCityArchive.ReadKnownRecords);
        var zones = Read(San9ScenarioZoneArchive.ReadKnownRecords);
        var forces = Read(San9ScenarioForceArchive.ReadKnownRecords);
        var persons = Read(San9ScenarioPersonArchive.ReadKnownRecords);
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
        bool Available(int id) => persons.Any(p => p.LocationId == id && p.StatusCode <= 3 && !p.IsCommandParticipant);
        int Morale(int? owner, uint soldiers) => owner is int f && soldiers > 0 ? Math.Clamp((int)Math.Sqrt(forces[f].Trust) + 40, 0, 100) : 0;
        int? emperorCity = new ushort[] { 563, 564, 565 }.Select(code => persons.FirstOrDefault(p => p.BiographyResourceCode == code && p.StatusCode != 8))
            .FirstOrDefault(p => p is not null)?.LocationId;
        int[] trainingCities = [0, 3, 8, 14, 17, 20, 25, 36, 42];
        List<SettlementSnapshot> settlements = [];
        Dictionary<int, string> regions = [];
        Dictionary<int, int> regionCities = [];
        for (int i = 0; i < cities.Count; i++)
        {
            var c = cities[i]; string key = text.City($"region.{i}", c.AuxiliaryId1); regions.Add(i, key); regionCities.Add(i, i);
            if (c.Durability == ushort.MaxValue || c.MaxDurability == ushort.MaxValue) continue;
            settlements.Add(new(i, key, Root(c.ForceId), c.X, c.Y, c.Soldiers, c.Durability, c.MaxDurability,
                Morale(Root(c.ForceId), c.Soldiers), Available(i), Root(c.ForceId) is int owner ? forces[owner].ColorCode : 50,
                (c.HasTrader ? new[] { "merchant" } : trainingCities.Contains(i) ? ["training"] : Array.Empty<string>())
                    .Concat(emperorCity == i ? ["emperor"] : Array.Empty<string>()).ToArray()));
        }
        foreach (var z in zones)
        {
            int id = 50 + z.Id; if (z.CityId is int city) regionCities.Add(id, city); regions.Add(id, text.Region($"region.{id}", z.NameResourceCode));
            if (z.FixedBuildingId is not int type || z.Durability == ushort.MaxValue) continue;
            string name = text.Settlement($"settlement.{id}", z.NameResourceCode, type);
            settlements.Add(new(id, name, Root(z.ForceId), z.X, z.Y, z.Soldiers, z.Durability, fixedBuildings[type].MaxDurability, Morale(Root(z.ForceId), z.Soldiers), Available(id), null, []));
        }
        var summaries = settlements.Where(s => s.OwnerId is not null).Select(s => s.OwnerId!.Value).Append(5).Distinct().Order().Select(id =>
        {
            var f = forces[id];
            if (f.LeaderId is not int leader || leader >= persons.Count) throw new InvalidDataException("Missing force leader.");
            var p = persons[leader];
            string name = text.Person($"force.{id}", p.FamilyNameResourceCode, p.GivenNameResourceCode);
            return new ForceSnapshot(id, name, f.ColorCode, f.Money, f.Food, f.Trust);
        }).ToArray();
        var catalog = text.Catalog;
        if (catalog.Resolve(summaries.Single(f => f.Id == 5).NameKey) != "董卓") throw new InvalidDataException("Unexpected 190-year player force.");
        var snapshot = new ScenarioSnapshot(2, header.ScenarioId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            header.StartDate.Year, header.StartDate.Month, header.StartDate.Day, 5, summaries, settlements.OrderBy(s => s.Id).ToArray(), regions, regionCities);
        snapshot.Validate(); catalog.Validate(); return (snapshot, catalog);
    }
}
