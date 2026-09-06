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
            if (person.StatusCode > 3) continue;
            if (person.LocationId is not int location || !locations.TryGetValue(location, out var settlement))
                throw new InvalidDataException($"Serving officer {id} has an unsupported initial location.");
            int ItemBonus(params int[] categories) => heldItems[id].Where(i => categories.Contains(i.CategoryId))
                .Select(i => (int)i.AbilityBonus).DefaultIfEmpty().Max();
            int OfficeBonus(int ability) => person.OfficeRankId is int office && offices[office].HasAbilityBonus(ability)
                ? offices[office].AbilityBonus : 0;
            var state = new OfficerState(id, settlement.OwnerId, location,
                new(person.Strength, person.Intelligence, person.Politics, person.Command), person.IsCommandParticipant)
            {
                Injury = person.Health,
                ItemBonuses = new(ItemBonus(1), ItemBonus(5, 9), ItemBonus(3, 4, 6), ItemBonus(2)),
                OfficeBonuses = new(OfficeBonus(1), OfficeBonus(2), OfficeBonus(3), OfficeBonus(0))
            };
            result.Add(new(text.Person($"officer.{id}", person.FamilyNameResourceCode, person.GivenNameResourceCode), state));
        }
        return result.ToArray();
    }
}
