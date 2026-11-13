using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

/// <summary>Explicit remake fallback for legacy packages that do not describe political regimes.</summary>
public static class PersonalPeeragePreset
{
    public const string GenericId = "remake-generic-peerage";

    public static ScenarioInitialState UpgradeLegacy(ScenarioInitialState scenario)
    {
        if (scenario.PersonalPeerageRegimes is not null) return scenario;
        if (scenario.Forces.Any(f => f.PersonalPeerageRegimeId is not null))
            throw new InvalidDataException("Force references a missing personal peerage regime.");
        var ranks = scenario.Information.Ranks.Where(r => !r.IsUnranked).OrderBy(r => r.Precedence).ToArray();
        if (ranks.Length == 0) return scenario;
        var formalRanks = ranks.Select(r => r.Id).ToImmutableArray();
        var imperialRank = ImmutableArray.Create(ranks[^1].Id);
        var allowed = ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty
            .Add(PersonalPeerageGrade.ImperialMarquis, formalRanks)
            .Add(PersonalPeerageGrade.VillageMarquis, formalRanks)
            .Add(PersonalPeerageGrade.DistrictMarquis, formalRanks)
            .Add(PersonalPeerageGrade.CountyMarquis, formalRanks)
            .Add(PersonalPeerageGrade.Duke, imperialRank)
            .Add(PersonalPeerageGrade.King, imperialRank);
        var generic = new PersonalPeerageRegimeDefinition(GenericId, [new PersonalPeeragePeriod(1, int.MaxValue, allowed, [])])
        { Name = "通用爵制（重制设定）" };
        return scenario with
        {
            PersonalPeerageRegimes = [generic],
            Forces = scenario.Forces.Select(f => f with { PersonalPeerageRegimeId = GenericId }).ToArray()
        };
    }
}
