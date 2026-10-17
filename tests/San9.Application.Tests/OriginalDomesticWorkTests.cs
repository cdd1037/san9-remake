using System.Collections.Immutable;
using San9.Application.AI;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OriginalDomesticWorkTests
{
    [Fact]
    public void PatrolFiltersIntelligenceButRollsPoliticsAndStopsAfterHalfOfOriginalPool()
    {
        var input = Input(Site(1, [8, 3, 9, 2])) with
        {
            Abilities = Abilities((8, new(0, 100, 0, 0)), (3, new(0, 90, 100, 0)),
                (9, new(0, 34, 100, 0)), (2, new(0, 0, 100, 0))),
        };
        int draws = 0;
        var command = Assert.Single(Commands(input, DomesticActivity.Patrol, () => { draws++; return 0; }));
        Assert.Equal(new[] { 3 }, command.OfficerIds);
        Assert.Equal(2, draws);
    }

    [Fact]
    public void SpecializationAdjustmentAndStableTiesDetermineWhoIsChecked()
    {
        var input = Input(Site(1, [90, 4, 2, 1])) with
        {
            Abilities = Abilities((90, new(0, 0, 80, 0)), (4, new(100, 100, 85, 100)),
                (2, new(0, 0, 80, 0)), (1, new(0, 0, 80, 0))),
        };
        // Specialized 80 has key 79, generalist 85 has key 77. The equal-key
        // officers retain collector order, rather than being sorted by entity ID.
        Assert.Equal(new[] { 90, 2 }, Assert.Single(Commands(input, DomesticActivity.Commerce)).OfficerIds);
    }

    [Fact]
    public void FailedRollsDoNotExtendCheckBudgetOrSkipFailedPreparation()
    {
        var input = Input(Site(1, [1, 2, 3, 4]), Site(2, [5])) with
        {
            Abilities = Enumerable.Range(1, 5).ToImmutableDictionary(id => id, _ => new OfficerAbilities(50, 50, 50, 50)),
        };
        int draws = 0, submits = 0;
        var result = OriginalDomesticWork.Run(DomesticActivity.Commerce, 10, () => input,
            () => { draws++; return 99; }, _ => { submits++; return true; });
        Assert.Equal(OriginalDomesticEnd.PreparationFailed, result.End);
        Assert.Equal(1, Assert.Single(result.Steps).SettlementId);
        Assert.Equal(2, draws);
        Assert.Equal(0, submits);
    }

    [Fact]
    public void AbilityThresholdUsesUnfilteredPoolSizeAndIncludesThirtyFive()
    {
        var input = Input(Site(1, [1, 2, 3, 4, 5, 6])) with
        {
            Abilities = Enumerable.Range(1, 6).ToImmutableDictionary(id => id,
                id => new OfficerAbilities(0, 0, id <= 3 ? 35 : 34, 0)),
        };
        Assert.Equal(new[] { 1, 2, 3 }, Assert.Single(Commands(input, DomesticActivity.Cultivation)).OfficerIds);
    }

    [Fact]
    public void GuaranteedProbabilitiesStillDrawAndSelectionStopsAtFive()
    {
        var ids = Enumerable.Range(1, 20).ToImmutableArray();
        var input = Input(Site(1, ids)) with
        {
            Abilities = ids.ToImmutableDictionary(id => id, _ => new OfficerAbilities(200, 200, 200, 200)),
        };
        int draws = 0;
        Assert.Equal(5, Assert.Single(Commands(input, DomesticActivity.Repair, () => { draws++; return 99; })).OfficerIds.Length);
        Assert.Equal(5, draws);
    }

    [Fact]
    public void CandidateOrderingUsesWidePopulationScoreAndStableTies()
    {
        var input = Input(Site(90, [1], uint.MaxValue), Site(2, [2], uint.MaxValue), Site(1, [3], 1));
        Assert.Equal(new[] { 90, 2, 1 }, Commands(input, DomesticActivity.Commerce).Select(c => c.SettlementId));
    }

    [Theory]
    [InlineData(DomesticActivity.Repair)]
    [InlineData(DomesticActivity.Train)]
    public void RelationBonusPrecedesLargerUnprioritizedGap(DomesticActivity activity)
    {
        var first = Site(1, [1]);
        var second = Site(2, [2]) with { HasPriorityRelation = true };
        first = first with { Settlement = first.Settlement with { Morale = 1, Durability = 0 } };
        second = second with { Settlement = second.Settlement with { Morale = 99, Durability = 99 } };
        Assert.Equal(new[] { 2, 1 }, Commands(Input(first, second), activity).Select(c => c.SettlementId));
    }

    [Fact]
    public void RecruitmentPrioritizesPopulationWithRelationBonusAndSelectsFiveWithoutRolling()
    {
        var input = Input(Site(1, [1, 2, 3, 4, 5, 6], 499999), Site(2, [7], 1) with { HasPriorityRelation = true });
        input = input with { Sites = input.Sites.Select(s => s with
            { Settlement = s.Settlement with { Morale = 90 } }).ToImmutableArray() };
        var commands = Commands(input, DomesticActivity.Recruit, () => throw new InvalidOperationException());
        Assert.Equal(new[] { 2, 1 }, commands.Select(c => c.SettlementId));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, commands[1].OfficerIds);
    }

    [Fact]
    public void TrainingUsesWarWithoutMinimumAbilityOrMoneyReserve()
    {
        var input = Input(Site(1, [1, 2])) with { Money = 0,
            Abilities = Abilities((1, new(34, 100, 100, 100)), (2, new(0, 100, 100, 100))) };
        Assert.Equal(new[] { 1 }, Assert.Single(Commands(input, DomesticActivity.Train, () => 33)).OfficerIds);
        Assert.Empty(Commands(input, DomesticActivity.Train, () => 34));
    }

    [Theory]
    [InlineData(9999, 89, 2000, 2501, true, true)]
    [InlineData(10000, 89, 2000, 2501, true, false)]
    [InlineData(10000, 90, 2000, 2501, true, true)]
    [InlineData(499999, 90, 2000, 2501, true, true)]
    [InlineData(500000, 90, 2000, 2501, true, false)]
    [InlineData(10000, 90, 1999, 2501, true, false)]
    [InlineData(10000, 90, 2000, 2500, true, false)]
    [InlineData(10000, 90, 2000, 2501, false, false)]
    public void RecruitmentUsesStrictGatesAndIntegerReserve(uint soldiers, int morale, uint money,
        uint food, bool allowed, bool expected)
    {
        var site = Site(1, [1]) with { Settlement = Site(1, [1]).Settlement with { Soldiers = soldiers, Morale = morale } };
        var input = Input(site) with { Money = money, Food = food, SalaryDemand = 1000, RationDemand = 1001,
            RecruitmentAllowed = allowed };
        Assert.Equal(expected, Commands(input, DomesticActivity.Recruit,
            () => throw new InvalidOperationException("Recruitment must not roll.")).Count > 0);
    }

    [Fact]
    public void RejectionConsumesTargetAndAttemptWithoutDebitingPrivateFunds()
    {
        var input = Input(Site(1, [1]), Site(2, [2]));
        var visited = new List<int>();
        var result = OriginalDomesticWork.Run(DomesticActivity.Commerce, 2, () => input, () => 0,
            c => { visited.Add(c.SettlementId); return false; });
        Assert.Equal(new[] { 1, 2 }, visited);
        Assert.All(result.Steps, s => Assert.Equal(OriginalDomesticAttempt.Rejected, s.Outcome));
        Assert.Equal(OriginalDomesticEnd.NoCandidates, result.End);
        visited.Clear();
        result = OriginalDomesticWork.Run(DomesticActivity.Commerce, 1, () => input, () => 0,
            c => { visited.Add(c.SettlementId); return false; });
        Assert.Equal(new[] { 1 }, visited);
        Assert.Equal(OriginalDomesticEnd.AttemptsExhausted, result.End);
    }

    [Fact]
    public void NextCandidateSeesActualFundsAndPersonnelAfterSubmission()
    {
        var input = Input(Site(1, [1]), Site(2, [2])) with { Money = 300 };
        int submitted = 0;
        var result = OriginalDomesticWork.Run(DomesticActivity.Commerce, 10, () => input, () => 0, _ =>
        {
            submitted++;
            input = input with { Money = 249 };
            return true;
        });
        Assert.Equal(1, submitted);
        Assert.Equal(OriginalDomesticEnd.ReserveReached, result.End);

        input = Input(Site(1, [1]), Site(2, [2]));
        result = OriginalDomesticWork.Run(DomesticActivity.Commerce, 10, () => input, () => 0, _ =>
        {
            input = input with { Sites = [input.Sites[0], input.Sites[1] with
                { Settlement = input.Sites[1].Settlement with { OfficerIds = [] } }] };
            return true;
        });
        Assert.Equal(OriginalDomesticEnd.PreparationFailed, result.End);
        Assert.Equal(OriginalDomesticAttempt.Submitted, result.Steps[0].Outcome);
        Assert.Equal(OriginalDomesticAttempt.PreparationFailed, result.Steps[1].Outcome);
    }

    [Fact]
    public void NonCityAndUsedActivitiesAreExcludedWithoutDrawing()
    {
        var city = Site(1, [1]);
        var input = Input(city with { IsCity = false }, Site(2, [2]) with
            { Settlement = Site(2, [2]).Settlement with { UnavailableActivities = [DomesticActivity.Commerce] } });
        Assert.Empty(Commands(input, DomesticActivity.Commerce,
            () => throw new InvalidOperationException("Excluded targets must not draw.")));
        Assert.Equal(2, Commands(input, DomesticActivity.Repair).Count);
    }

    [Fact]
    public void RealPublicSubmissionChangesResourcesAndOfficersBeforeFollowingWork()
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 190);
        var context = new CommandContext("player");
        var seat = session.Snapshot.Simulation.Controls[0];
        OriginalDomesticInputs Capture()
        {
            var engine = new SimulationEngine(WorldState.Restore(session.Snapshot));
            var observed = DomesticObservation.Capture(session.Snapshot, seat, context, engine.GetAbilities(session.Snapshot));
            return new(seat.ForceId, observed.Settlements[0].Money, observed.Settlements[0].Food, 0, 0, true,
                observed.Settlements.Select(s => new OriginalDomesticSite(s, true, false)).ToImmutableArray(), observed.Abilities);
        }
        bool Submit(DevelopmentCommand development)
        {
            var command = new DevelopCommand(Guid.NewGuid(), seat.ForceId,
                session.Snapshot.Simulation.NextSequence, development);
            return session.Execute(context, command).Submission.Accepted;
        }
        uint before = Capture().Money;
        var commerce = OriginalDomesticWork.Run(DomesticActivity.Commerce, 10, Capture, () => 0, Submit);
        var cultivation = OriginalDomesticWork.Run(DomesticActivity.Cultivation, 10, Capture, () => 0, Submit);
        Assert.Equal(OriginalDomesticAttempt.Submitted, Assert.Single(commerce.Steps).Outcome);
        Assert.Equal(OriginalDomesticAttempt.Submitted, Assert.Single(cultivation.Steps).Outcome);
        var city = session.Snapshot.Settlements.Single(s => s.Id == 1);
        Assert.True(city.Commerce > 300 && city.Cultivation > 200);
        Assert.Equal(before - 100, Capture().Money);
        Assert.Equal(2, session.Snapshot.Officers.Count(o => o.OwnerId == 1 && o.Acted));
        Assert.Equal(2, session.Snapshot.Simulation.NextSequence);
        Assert.Empty(OriginalDomesticWork.Run(DomesticActivity.Commerce, 10, Capture,
            () => throw new InvalidOperationException(), Submit).Steps);
    }

    private static OriginalDomesticSite Site(int id, ImmutableArray<int> people, uint population = 1000) =>
        new(new(id, 1, 10000, 100000, 10000, 50, 500, 50, 100, people)
        { DraftPopulation = population, SoldierCapacity = 500000, Commerce = 300, MaxCommerce = 500,
            Cultivation = 200, MaxCultivation = 500 }, true, false);

    private static OriginalDomesticInputs Input(params OriginalDomesticSite[] sites) =>
        new(1, 10000, 100000, 0, 0, true, sites.ToImmutableArray(),
            sites.SelectMany(s => s.Settlement.OfficerIds).Distinct()
                .ToImmutableDictionary(id => id, _ => new OfficerAbilities(100, 100, 100, 100)));

    private static ImmutableDictionary<int, OfficerAbilities> Abilities(params (int Id, OfficerAbilities Value)[] values) =>
        values.ToImmutableDictionary(p => p.Id, p => p.Value);

    private static List<DevelopmentCommand> Commands(OriginalDomesticInputs input, DomesticActivity activity, Func<int>? roll = null)
    {
        var commands = new List<DevelopmentCommand>();
        OriginalDomesticWork.Run(activity, int.MaxValue, () => input, roll ?? (() => 0), c => { commands.Add(c); return true; });
        return commands;
    }
}
