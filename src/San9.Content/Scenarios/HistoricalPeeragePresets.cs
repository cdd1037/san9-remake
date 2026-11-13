using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

/// <summary>Game policies for explicitly identified regimes; the six supported grades are not the complete historical hierarchy.</summary>
public static class HistoricalPeeragePresets
{
    public const string Han = "han-peerage";
    public const string Wei = "wei-peerage";
    public const string Shu = "shu-peerage";
    public const string Wu = "wu-peerage";

    public static ImmutableArray<PersonalPeerageRegimeDefinition> Create(
        IReadOnlyCollection<RankDefinition> ranks, int weiBloodline, int shuBloodline, int wuBloodline)
    {
        var ordered = ranks.Where(r => !r.IsUnranked).OrderBy(r => r.Precedence).ToArray();
        if (ordered.Length == 0 || weiBloodline < 0 || shuBloodline < 0 || wuBloodline < 0 ||
            new[] { weiBloodline, shuBloodline, wuBloodline }.Distinct().Count() != 3)
            throw new ArgumentException("Invalid ranks or royal bloodline identities.");
        var formalRanks = ordered.Select(r => r.Id).ToImmutableArray();
        var imperialRank = ImmutableArray.Create(ordered[^1].Id);
        var authority = ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty
            .Add(PersonalPeerageGrade.ImperialMarquis, formalRanks)
            .Add(PersonalPeerageGrade.VillageMarquis, formalRanks)
            .Add(PersonalPeerageGrade.DistrictMarquis, formalRanks)
            .Add(PersonalPeerageGrade.CountyMarquis, formalRanks)
            .Add(PersonalPeerageGrade.Duke, imperialRank)
            .Add(PersonalPeerageGrade.King, imperialRank);
        PersonalPeeragePeriod Period(int start, int end, ImmutableArray<int> royal) => new(start, end, authority, royal);
        return [
            new(Han, [Period(1, int.MaxValue, [])]) { Name = "汉爵制（重制设定）" },
            new(Wei, [Period(1, 221, [weiBloodline]), Period(222, int.MaxValue, [weiBloodline])]) { Name = "魏爵制（重制设定）" },
            new(Shu, [Period(1, int.MaxValue, [shuBloodline])]) { Name = "蜀汉爵制（重制设定）" },
            new(Wu, [Period(1, int.MaxValue, [wuBloodline])]) { Name = "孙吴爵制（重制设定）" }
        ];
    }
}
