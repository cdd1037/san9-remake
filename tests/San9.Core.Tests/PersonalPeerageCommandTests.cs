using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonalPeerageCommandTests
{
    private static SimulationEngine Engine(int year = 190, string? regime = "han", int rank = 1, int existing = 0,
        PersonalPeerage? initial = null, bool countyAuthority = true, int loyalty = 50)
    {
        var force = new ForceState(1, 100) { LeaderId = 1, RankId = rank, PersonalPeerageRegimeId = regime };
        var ruler = new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler };
        var person = new OfficerState(2, 1, 1, new(50, 50, 50, 50), false)
            { OfficeId = 7, PersonalPeerage = initial, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, loyalty) };
        var holders = Enumerable.Range(10, existing).Select(id => new OfficerState(id, 1, 1, new(50, 50, 50, 50), false)
        { PersonalPeerage = new(PersonalPeerageGrade.VillageMarquis, null) });
        var world = WorldState.Create(new(year, 1, 1), 1, [force],
            [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)], [ruler, person, .. holders],
            controls: [new("player", 1, ControllerKind.Player)],
            corps: [new CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }]);
        var period = new PersonalPeeragePeriod(180, 220,
            ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty
                .Add(PersonalPeerageGrade.VillageMarquis, [1])
                .Add(PersonalPeerageGrade.CountyMarquis, countyAuthority ? [1] : [])
                .Add(PersonalPeerageGrade.King, [1]), []);
        var rules = new SimulationRules { Governance = new([new GovernorOfficeRule(7, 100, 0)]),
            Economy = new EconomyRulebook([new OfficeSalary(7, 1)], []) { PersonalPeerageRegimes = ImmutableDictionary<string, PersonalPeerageRegime>.Empty.Add("han", new([period])) } };
        return new SimulationEngine(world, rules);
    }

    private static CommandExecutionResult Execute(SimulationEngine engine, PersonalPeerage? title) => engine.Execute(new("player"),
        new SetPersonalPeerageCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 2, title));

    [Theory]
    [InlineData(96, 100)]
    [InlineData(100, 104)]
    [InlineData(120, 124)]
    [InlineData(254, 255)]
    [InlineData(255, 255)]
    public void GrantPreservesHiddenLoyaltyAndSaturatesAtStorageLimit(int loyalty, int expected)
    {
        var engine = Engine(loyalty: loyalty);
        Assert.True(Execute(engine, new(PersonalPeerageGrade.VillageMarquis, null)).Submission.Accepted);
        Assert.Equal(expected, engine.Snapshot.Officers.Single(p => p.Id == 2).Loyalty);
    }

    [Fact]
    public void FormalCountryTitleChangesRegimeOnlyWhenExplicitlyMapped()
    {
        var mutation = WorldState.Restore(Engine().CaptureStableState()).BeginMutation();
        var politics = new PoliticalRulebook(null, [new PeerageRule(3, 1, 0, 0)],
            [new CountryTitleRule(8, true, true, false, [], []) { PersonalPeerageRegimeId = "wei" },
             new CountryTitleRule(9, true, true, false, [], [])], ImmutableDictionary<int, int>.Empty);
        var rules = new SimulationRules { Politics = politics };
        PoliticalEvents.GrantRank(mutation, rules, 1, 3, 8, PeerageChangeKind.Proclamation, 0, []);
        Assert.Equal("wei", mutation.Forces[1].PersonalPeerageRegimeId);
        PoliticalEvents.GrantRank(mutation, rules, 1, 3, 9, PeerageChangeKind.Promotion, 0, []);
        Assert.Equal("wei", mutation.Forces[1].PersonalPeerageRegimeId);
    }

    [Fact]
    public void GrantPromotionRenameAndRevocationPreserveOfficeAndDoNotRepeatLoyaltyReward()
    {
        var engine = Engine();
        int loyalty = engine.Snapshot.Officers.Single(p => p.Id == 2).Loyalty;
        Assert.True(Execute(engine, new(PersonalPeerageGrade.VillageMarquis, "汉寿亭侯")).Submission.Accepted);
        var person = engine.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Equal(7, person.OfficeId);
        Assert.Equal(Math.Min(255, loyalty + 4), person.Loyalty);
        Assert.True(Execute(engine, new(PersonalPeerageGrade.VillageMarquis, "新亭侯")).Submission.Accepted);
        Assert.Equal(person.Loyalty, engine.Snapshot.Officers.Single(p => p.Id == 2).Loyalty);
        Assert.True(Execute(engine, new(PersonalPeerageGrade.CountyMarquis, null)).Submission.Accepted);
        int promotedLoyalty = engine.Snapshot.Officers.Single(p => p.Id == 2).Loyalty;
        Assert.True(Execute(engine, null).Submission.Accepted);
        Assert.Null(engine.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage);
        Assert.Equal(Math.Max(0, promotedLoyalty - 10), engine.Snapshot.Officers.Single(p => p.Id == 2).Loyalty);
        Assert.Equal(7, engine.Snapshot.Officers.Single(p => p.Id == 2).OfficeId);
    }

    [Fact]
    public void MissingRegimeWrongPeriodOrRankAndUnverifiedKingAreRejected()
    {
        Assert.Equal(CommandRejection.PeerageRegimeUnavailable, Execute(Engine(regime: null), new(PersonalPeerageGrade.VillageMarquis, null)).Submission.Rejection);
        Assert.Equal(CommandRejection.PeerageRegimeUnavailable, Execute(Engine(year: 221), new(PersonalPeerageGrade.VillageMarquis, null)).Submission.Rejection);
        Assert.Equal(CommandRejection.PeerageUnauthorized, Execute(Engine(rank: 2), new(PersonalPeerageGrade.VillageMarquis, null)).Submission.Rejection);
        Assert.False(Execute(Engine(), new(PersonalPeerageGrade.King, null)).Submission.Accepted);
        var full = Engine(existing: 12);
        Assert.Equal(CommandRejection.PeerageQuotaFull, Execute(full, new(PersonalPeerageGrade.VillageMarquis, null)).Submission.Rejection);
        Assert.Equal(12, full.Snapshot.Officers.Count(p => p.PersonalPeerage?.Grade == PersonalPeerageGrade.VillageMarquis));
        var retained = Engine(initial: new(PersonalPeerageGrade.CountyMarquis, "旧县侯"), countyAuthority: false);
        Assert.Equal(CommandRejection.PeerageUnauthorized, Execute(retained, new(PersonalPeerageGrade.VillageMarquis, null)).Submission.Rejection);
        Assert.Equal("旧县侯", retained.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage!.Title);
    }
}
