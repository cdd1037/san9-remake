using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class OfficerContentCompiler
{
    public static OfficerAbilities OfficeBonuses(San9ScenarioOfficeRecord office) => new(
        office.HasStrengthBonus ? office.AbilityBonus : 0, office.HasIntelligenceBonus ? office.AbilityBonus : 0,
        office.HasPoliticsBonus ? office.AbilityBonus : 0, office.HasLeadershipBonus ? office.AbilityBonus : 0);
    public static OfficerInitialState[] Compile(IReadOnlyList<San9ScenarioPersonRecord> people,
        IReadOnlyList<SettlementInitialState> settlements, IReadOnlyList<ForceInitialState> forces,
        ScenarioTextReader text, San9MessageTable biographies)
    {
        var locations = settlements.ToDictionary(s => s.Id);
        List<OfficerInitialState> result = [];
        for (int id = 0; id < people.Count; id++)
        {
            var person = people[id];
            // Unconfigured expansion person slots have no built-in text resources.
            if (id >= 700)
            {
                if (person.StatusCode != 8) throw new InvalidDataException("Unsupported configured expansion officer.");
                continue;
            }
            var settlement = person.LocationId is int sourceLocation ? locations.GetValueOrDefault(sourceLocation) : null;
            int? location = settlement?.Id;
            if (person.StatusCode <= 3 && settlement is null)
                throw new InvalidDataException($"Serving officer {id} has an unsupported initial location.");
            var state = new OfficerState(id, person.StatusCode <= 3 ? settlement?.OwnerId : null, location,
                new(person.Strength, person.Intelligence, person.Politics, person.Command), person.IsCommandParticipant)
            {
                Affinity = person.Affinity,
                TacticLinkBonus = person.BiographyResourceCode == 9 ? 10 : 0,
                EquippedTacticId = person.EquippedTacticId,
                TacticCooldown = 0,
                MasteredCategories = Enumerable.Range(0, 9).Where(person.HasTacticCategoryMastery).ToImmutableArray(),
                LearnedTactics = Enumerable.Range(0, San9ScenarioPersonArchive.TacticCount).Where(person.HasLearnedTactic).ToImmutableArray(),
                TacticProficiencies = person.TacticProficiencies.Select(v => (int)v).ToImmutableArray(),
                StatusStartYear = person.EncodedStatusStartYear == 0 ? null : person.StatusStartYear,
                ParentId = person.ParentPersonId, IsFemale = person.GenderFlag != 0, Personality = person.Personality,
                IsCommander = person.IsOwnerLeader,
                RegionId = settlement is null ? person.LocationId : null, Injury = person.Health, Status = (OfficerStatus)person.StatusCode, BirthYear = person.BirthYear, OfficeId = person.OfficeRankId,
                ForceLoyalties = forces.ToImmutableDictionary(f => f.Id, f => (int)person.GetForceLoyalty(f.Id)),
                Righteousness = person.Righteousness,
                HasRecruitmentPoliticsBonus = person.BiographyResourceCode == 36,
                RewardedThisXun = person.WasRewardedThisXun
            };
            result.Add(new(text.Person($"officer.{id}", person.FamilyNameResourceCode, person.GivenNameResourceCode), state)
            {
                CourtesyNameKey = text.Courtesy($"officer-courtesy.{id}", person.CourtesyNameResourceCode),
                BiographyKey = text.Biography($"officer-biography.{id}", person.BiographyResourceCode, biographies)
            });
        }
        return result.ToArray();
    }
}
