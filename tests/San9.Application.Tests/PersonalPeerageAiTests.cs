using System.Collections.Immutable;
using San9.Application.AI;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PersonalPeerageAiTests
{
    private static (SimulationEngine Engine, ControlSeat Seat) Create(uint money, bool authorized, int salary = 1, bool competing = false, bool service = false)
    {
        var seat = new ControlSeat("ai", 1, ControllerKind.Automatic);
        var world = WorldState.Create(new(190, 1, 1), 1,
            [new ForceState(1, 100) { LeaderId = 1, RankId = 3, PersonalPeerageRegimeId = "test" }],
            [new SettlementState(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            competing ? [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
                new OfficerState(2, 1, 1, new(50, 50, 50, 50), false) { TacticProficiencies = [1000, 0, 0, 0, 0, 0, 0, 0, 0] },
                new OfficerState(3, 1, 1, new(50, 50, 50, 50), false) {
                    BattleMerit = service ? null : ImmutableSortedDictionary<int, int>.Empty.Add(1, 1),
                    ServiceMerit = service ? ImmutableSortedDictionary<int, int>.Empty.Add(1, 1) : null }] :
            [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new OfficerState(2, 1, 1, new(50, 50, 50, 50), false)],
            controls: [seat], corps: [new CorpsState(1, 1, 1, new(money, 1000)) { IsRoot = true }]);
        var ranks = ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty
            .Add(PersonalPeerageGrade.ImperialMarquis, authorized ? [3] : []);
        var period = new PersonalPeeragePeriod(180, 220, ranks, [])
        { GradeRules = PersonalPeeragePolicy.Default.Grades.Values.OrderBy(r => r.Grade)
            .Select(r => r.Grade == PersonalPeerageGrade.ImperialMarquis ? r with { Salary = salary } : r).ToImmutableArray() };
        var rules = new SimulationRules { Economy = new EconomyRulebook([], []) { PersonalPeerageRegimes = ImmutableDictionary<string, PersonalPeerageRegime>.Empty
            .Add("test", new([period])) } };
        return (new SimulationEngine(world, rules), seat);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AiPrioritizesRecordedMeritInTheCurrentForce(bool service)
    {
        var (engine, seat) = Create(1000, true, competing: true, service: service);
        OriginalAiPersonalPeerage.Manage(engine, seat, []);
        Assert.NotNull(engine.Snapshot.Officers.Single(p => p.Id == 3).PersonalPeerage);
        Assert.Null(engine.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage);
    }

    [Fact]
    public void AiUsesFormalCommandAndRespectsAuthorityAndSalaryReserve()
    {
        var (engine, seat) = Create(1000, true);
        List<DomainEvent> events = [];
        OriginalAiPersonalPeerage.Manage(engine, seat, events);
        Assert.Contains(events, e => e is PersonalPeerageChangedEvent { OfficerId: 2 });
        Assert.Equal(PersonalPeerageGrade.ImperialMarquis, engine.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage!.Grade);
        var (unfunded, poorSeat) = Create(1, true);
        OriginalAiPersonalPeerage.Manage(unfunded, poorSeat, []);
        Assert.Null(unfunded.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage);
        var (forbidden, forbiddenSeat) = Create(1000, false);
        OriginalAiPersonalPeerage.Manage(forbidden, forbiddenSeat, []);
        Assert.Null(forbidden.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage);
        var (expensive, expensiveSeat) = Create(1000, true, salary: 500);
        OriginalAiPersonalPeerage.Manage(expensive, expensiveSeat, []);
        Assert.Null(expensive.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage);
    }
}
