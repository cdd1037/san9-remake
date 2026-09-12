using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class ScenarioContentCompiler
{
    public static (ScenarioInitialState Scenario, TextCatalog Text) Compile(byte[] exe, byte[] scenario, byte[] messages, byte[] biographies)
    {
        T Read<T>(Func<Stream, T> parser) { using var stream = new MemoryStream(scenario); return parser(stream); }
        var header = Read(San9ScenarioArchive.ReadInitialScenario190Header);
        var cities = Read(San9ScenarioCityArchive.ReadKnownRecords);
        var zones = Read(San9ScenarioZoneArchive.ReadKnownRecords);
        var personPlaces = cities.Select((c, id) => new { Id = id, X = c.X, Y = c.Y })
            .Concat(zones.Select(z => new { Id = 50 + z.Id, X = z.X, Y = z.Y }))
            .Where(p => p.X != ushort.MaxValue || p.Y != ushort.MaxValue).ToArray();
        if (personPlaces.Any(p => p.X >= 200 || p.Y >= 200))
            throw new InvalidDataException("Invalid configured region coordinates.");
        var configuredRegionIds = personPlaces.Select(p => p.Id).ToHashSet();
        var forces = Read(San9ScenarioForceArchive.ReadKnownRecords);
        var persons = Read(San9ScenarioPersonArchive.ReadKnownRecords);
        var sourceUnits = Read(San9ScenarioUnitArchive.ReadKnownRecords);
        var items = Read(San9ScenarioItemArchive.ReadKnownRecords);
        var ranks = Read(San9ScenarioRankArchive.ReadKnownRecords);
        var offices = Read(San9ScenarioOfficeArchive.ReadKnownRecords);
        var fixedBuildings = Read(San9ScenarioFixedBuildingArchive.ReadKnownRecords);
        using var text = new ScenarioTextReader(messages, exe);
        text.ReadStrategyHints();
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
            if (!configuredRegionIds.Contains(i)) continue;
            var c = cities[i]; string key = text.City($"region.{i}", c.AuxiliaryId1); regions.Add(i, key); regionCities.Add(i, i);
            if (c.Durability == ushort.MaxValue || c.MaxDurability == ushort.MaxValue) continue;
            settlements.Add(new(i, key, Root(c.ForceId), c.X, c.Y, c.Soldiers, c.Durability, c.MaxDurability,
                Morale(Root(c.ForceId), c.Soldiers),
                (c.HasTrader ? new[] { "merchant" } : trainingCities.Contains(i) ? ["training"] : Array.Empty<string>())
                    .Concat(emperorCity == i ? ["emperor"] : Array.Empty<string>()).ToArray())
            { SoldierCapacity = San9ScenarioZoneArchive.CitySoldierCapacity, Population = c.Population, DraftPopulation = c.DraftPopulation, CorpsId = c.ForceId, IsFacilityListEntry = true, Appearance = c.TerrainCode, PublicOrder = c.PublicOrder, Commerce = c.Income, MaxCommerce = c.MaxIncome, Cultivation = c.Harvest, MaxCultivation = c.MaxHarvest });
        }
        foreach (var z in zones)
        {
            int id = 50 + z.Id;
            if (!configuredRegionIds.Contains(id)) continue;
            if (z.CityId is int city) regionCities.Add(id, city); regions.Add(id, text.Region($"region.{id}", z.NameResourceCode));
            if (z.FixedBuildingId is not int type || z.Durability == ushort.MaxValue) continue;
            string name = text.Settlement($"settlement.{id}", z.NameResourceCode, type);
            settlements.Add(new(id, name, Root(z.ForceId), z.X, z.Y, z.Soldiers, z.Durability, fixedBuildings[type].MaxDurability, Morale(Root(z.ForceId), z.Soldiers), [])
            { SoldierCapacity = z.SoldierCapacity, ConsumesFood = type is not (>= 7 and <= 10), Kind = San9.Core.Domain.SettlementKind.Facility, CanHostPersons = type < 10, CorpsId = z.ForceId, IsFacilityListEntry = fixedBuildings[type].IsFacilityListEntry });
        }
        var summaries = settlements.Where(s => s.OwnerId is not null).Select(s => s.OwnerId!.Value).Append(5).Distinct().Order().Select(id =>
        {
            var f = forces[id];
            if (f.LeaderId is not int leader || leader >= persons.Count) throw new InvalidDataException("Missing force leader.");
            var p = persons[leader];
            string name = text.Person($"force.{id}", p.FamilyNameResourceCode, p.GivenNameResourceCode);
            return new ForceInitialState(id, name, f.ColorCode, f.Money, f.Food, f.Trust) { BaseTrust = f.BaseTrust, ProvidedTactics = Enumerable.Range(0, San9ScenarioForceArchive.TacticCount).Where(f.HasTactic).ToImmutableArray(), RankId = f.RankGroupId is < 8 ? f.RankGroupId : null, IsBarbarian = f.IsBarbarian || f.CountryTitleId is >= 4 and <= 8, LeaderId = leader, FlagGlyph = f.CountryTitleId is int title ? System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x253bb0 + title * 4, 4)) : p.FlagCode < 186 ? p.FlagCode : -1 };
        }).ToArray();
        var officers = OfficerContentCompiler.Compile(persons, items, offices, settlements, summaries, text, new San9MessageTable(biographies));
        summaries = summaries.Select(f => f with { AdviserId = officers.Where(o => o.State.OwnerId == f.Id && o.State.Status == San9.Core.Domain.OfficerStatus.Strategist)
            .Select(o => (int?)o.State.Id).SingleOrDefault() }).ToArray();
        var treasureStates = TreasureContentCompiler.InitialStates(items);
        var information = new InformationDefinitions(
            ranks.Where(r => r.Id < 8).Select(r => new RankDefinition(r.Id, text.Rank($"rank.{r.Id}", r.Id), r.CommandCapacity, r.PrestigeRequirement, r.PromotionLoyaltyBonus)).ToArray(),
            offices.Where(o => o.IsAssignable).Select(o => new OfficeDefinition(o.Id, text.Office($"office.{o.Id}", o.Id), o.CommandCapacity, o.Salary)
                { GovernorPriority = San9ScenarioOfficeArchive.RecordCount - o.Id,
                  AbilityBonuses = OfficerContentCompiler.OfficeBonuses(o),
                  RequiredRankId = o.RankGroupId is int rank && rank < 8 ? rank : null }).ToArray(),
            items.Where(i => i.SerializedId < TreasureContentCompiler.BuiltInCount).Select(i =>
            {
                text.TreasureDescription(i.SerializedId, i.ResourceId2);
                return new TreasureDefinition(i.SerializedId, text.Treasure($"treasure.{i.SerializedId}", i.ResourceId1), i.CategoryId, i.AbilityBonus, i.LoyaltyChange, i.TacticId)
                { AbilityBonuses = TreasureContentCompiler.AbilityBonuses(i) };
            }).ToArray(),
            settlements.Where(s => s.CorpsId is not null).Select(s => s.CorpsId!.Value).Distinct().Order().Select(id =>
                new San9.Core.Domain.CorpsState(id, Root(id)!.Value, forces[id].LeaderId ?? throw new InvalidDataException("Missing corps leader."),
                    id == Root(id) ? null : new San9.Core.Domain.CorpsTreasury(forces[id].Money, forces[id].Food))
                { ControlMode = id == Root(id) || forces[id].IsPlayerControlled
                    ? San9.Core.Domain.CorpsControlMode.Direct : San9.Core.Domain.CorpsControlMode.Automatic }).ToArray(),
            treasureStates)
        {
            Relations = summaries.SelectMany(a => summaries.Where(b => b.Id > a.Id).Select(b =>
                new San9.Core.Domain.ForceRelation(a.Id, b.Id, forces[a.Id].DiplomaticRelations[b.Id]))).ToArray()
        };
        var catalog = text.Catalog;
        if (catalog.Resolve(summaries.Single(f => f.Id == 5).NameKey) != "董卓") throw new InvalidDataException("Unexpected 190-year player force.");
        var units = UnitContentCompiler.Compile(sourceUnits, Root);
        var snapshot = new ScenarioInitialState(22, header.ScenarioId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            header.StartDate.Year, header.StartDate.Month, header.StartDate.Day, 5, summaries, settlements.OrderBy(s => s.Id).ToArray(), regions, regionCities)
        { UnbuiltPlaces = personPlaces.Where(p => settlements.All(s => s.Id != p.Id)).Select(p => new San9.Core.Domain.PersonPlace(p.Id, San9.Core.Domain.MapCoordinates.Center(new(p.X, p.Y)))).ToImmutableArray(),
            CityConnections = PersonnelGeographyCompiler.CityConnections(Read(San9ScenarioSquareArchive.ReadKnownRecords),
                San9ScenarioSquareArchive.MapWidth, San9ScenarioSquareArchive.MapHeight, regionCities,
                zones.Where(z => z.FixedBuildingId == 1).Select(z => 50 + z.Id).ToHashSet()),
            Officers = officers, Units = units, Information = information, DomesticPairs = DomesticContentCompiler.Compile(persons),
            ImperialSealId = 79, EmperorId = persons.Take(700).Select((p, id) => (p, id)).Where(v => v.p.BiographyResourceCode is 563 or 564 or 565 && v.p.StatusCode != 8).Select(v => (int?)v.id).FirstOrDefault() };
        snapshot.Validate(); catalog.Validate(); return (snapshot, catalog);
    }
}
