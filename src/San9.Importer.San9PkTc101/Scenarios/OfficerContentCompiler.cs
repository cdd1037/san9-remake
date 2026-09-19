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
                AppearanceYear = person.AppearanceYear,
                LifespanTier = person.LifespanTier,
                SuccessionPriority = SuccessionPriority(person.BiographyResourceCode),
                BloodlineId = person.BloodlineRootPersonId,
                TacticLinkBonus = person.BiographyResourceCode == 9 ? 10 : 0,
                DuelAdvantages = DuelTraits(person.BiographyResourceCode),
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
                Ambition = person.Ambition,
                ForceFounderPriority = FounderPriority(person.BiographyResourceCode),
                PrefersConsolidationLeadership = ConsolidationLeadership(person.BiographyResourceCode),
                AutomaticSurrenderAllowed = person.BiographyResourceCode != 36 && person.BiographyResourceCode is not (>= 59 and <= 64),
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

    // TC101 biography identities are translated here; runtime ordering uses authored semantics.
    public static DuelAdvantages DuelTraits(int biographyId) => biographyId switch
    {
        3 => DuelAdvantages.DominantParry | DuelAdvantages.DominantAttack,
        9 => DuelAdvantages.DominantParry,
        4 => DuelAdvantages.DominantDoubleStrike,
        _ => DuelAdvantages.None
    };

    public static int FounderPriority(int biographyId) => biographyId switch
    {
        32 => 15, 5 => 14, 60 => 13, 38 => 12, 61 => 11, 62 => 10, 63 => 9, 64 => 8,
        520 => 7, 66 => 6, 68 => 5, 74 => 4, 36 => 3, 3 => 2, 27 => 1, _ => 0
    };

    public static bool ConsolidationLeadership(int biographyId) => biographyId is 36 or 60 or 61 or 59 or 62 or 63 or 64 or 68 or 3 or 5 or 520;

    internal static int SuccessionPriority(int biographyId) => biographyId switch
    {
        3 or 4 or 66 => 1,
        36 or 61 or 64 => 2,
        5 or 27 or 32 or 38 or 60 or 62 or 63 or 74 => 3,
        68 or 520 or 530 => 4,
        _ => 0
    };
}
