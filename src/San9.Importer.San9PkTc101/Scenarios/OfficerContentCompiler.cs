using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class OfficerContentCompiler
{
    public static OfficerInitialState[] Compile(IReadOnlyList<San9ScenarioPersonRecord> people,
        IReadOnlyList<San9ScenarioItemRecord> items, IReadOnlyList<San9ScenarioOfficeRecord> offices,
        IReadOnlyList<SettlementInitialState> settlements, ScenarioTextReader text)
    {
        var locations = settlements.ToDictionary(s => s.Id);
        var heldItems = items.Where(i => !i.IsInvalid && i.HolderPersonId is not null).ToLookup(i => i.HolderPersonId!.Value);
        List<OfficerInitialState> result = [];
        for (int id = 0; id < people.Count; id++)
        {
            var person = people[id];
            if (person.StatusCode > 5) continue;
            var settlement = person.LocationId is int sourceLocation ? locations.GetValueOrDefault(sourceLocation) : null;
            int? location = settlement?.Id;
            if (person.StatusCode <= 3 && settlement is null)
                throw new InvalidDataException($"Serving officer {id} has an unsupported initial location.");
            int ItemBonus(params int[] categories) => heldItems[id].Where(i => categories.Contains(i.CategoryId))
                .Select(i => (int)i.AbilityBonus).DefaultIfEmpty().Max();
            int OfficeBonus(int ability) => person.OfficeRankId is int office && offices[office].HasAbilityBonus(ability)
                ? offices[office].AbilityBonus : 0;
            var state = new OfficerState(id, person.StatusCode <= 3 ? settlement?.OwnerId : null, location,
                new(person.Strength, person.Intelligence, person.Politics, person.Command), person.IsCommandParticipant)
            {
                LearnedTactics = Enumerable.Range(0, San9ScenarioPersonArchive.TacticCount).Where(person.HasLearnedTactic).ToImmutableArray(),
                TacticProficiencies = person.TacticProficiencies.Select(v => (int)v).ToImmutableArray(),
                ServiceStartYear = person.EncodedStatusStartYear == 0 ? null : person.StatusStartYear,
                ParentId = person.ParentPersonId, IsFemale = person.GenderFlag != 0, Personality = person.Personality,
                RegionId = settlement is null ? person.LocationId : null, Injury = person.Health, Status = (OfficerStatus)person.StatusCode, BirthYear = person.BirthYear, OfficeId = person.OfficeRankId,
                Loyalty = person.StatusCode <= 3 && settlement?.OwnerId is int owner ? person.GetForceLoyalty(owner) : 0,
                ItemBonuses = new(ItemBonus(1), ItemBonus(5, 9), ItemBonus(3, 4, 6), ItemBonus(2)),
                OfficeBonuses = new(OfficeBonus(1), OfficeBonus(2), OfficeBonus(3), OfficeBonus(0))
            };
            result.Add(new(text.Person($"officer.{id}", person.FamilyNameResourceCode, person.GivenNameResourceCode), state));
        }
        return result.ToArray();
    }
}
