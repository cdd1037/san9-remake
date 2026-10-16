using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class OriginalAiPersonnelTests
{
    private static ScenarioInitialState Fixture()
    {
        var content = G01ScenarioTests.Fixture();
        return content with
        {
            Officers = content.Officers.Select(o => o with { State = o.State.WithLoyalty(o.State.OwnerId!.Value, 100)
                .WithLoyalty(o.State.OwnerId == 1 ? 2 : 1, 100) with
                { Status = o.State.Id is 1 or 4 ? OfficerStatus.Ruler : OfficerStatus.Serving, BirthYear = 170 } }).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c with { Treasury = new(10000, 50000) }).ToArray(), Relations = [new(1, 2, 50)] },
            CityConnections = [new(1, 2), new(2, 1)],
            RegionCityIds = new() { [1] = 1, [2] = 2 },
            RegionNameKeys = new() { [1] = "region.1", [2] = "region.2" },
            OriginalAi = OriginalAiDefinition.Default with { Advanced = true,
                WorkRules = OriginalAiDefinition.Default.WorkRules.ToImmutableDictionary(p => p.Key, p => p.Value with { OpeningWaitDays = 0, PeriodDays = 0 }) }
        };
    }

    private sealed class Harness
    {
        internal SimulationEngine Engine { get; }
        internal OriginalAiPersonnel Personnel { get; }
        internal OriginalAiState State { get; }
        internal List<GameCommand> Commands { get; } = [];
        internal List<DomainEvent> Events { get; } = [];
        internal Harness(ScenarioInitialState content, Func<int, int>? draw = null, bool reject = false, int corpsId = 1)
        {
            var initial = new ScenarioSessionDefinition(content).CreateWorld(190).CreateSnapshot();
            Engine = GameSession.EngineFactory(content)(WorldState.Restore(initial with
                { Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable } }));
            if (corpsId == 2) Engine.EndControl(new("player"));
            var context = new CommandContext(corpsId == 1 ? "player" : "ai:2");
            Personnel = new(Engine, content, corpsId, new(content, null, Engine.Snapshot), context, new OriginalAiRandom(draw ?? (_ => 0)), command =>
            {
                Commands.Add(command);
                if (reject) return false;
                var result = Engine.Execute(context, command);
                Events.AddRange(result.Events);
                Assert.True(result.Submission.Accepted, $"{command}: {result.Submission.Rejection}");
                return result.Submission.Accepted;
            });
            State = OriginalAiState.Create(content) with { Corps = [new(corpsId, OriginalAiMode.TargetForce, corpsId == 1 ? 2 : 1, null)] };
        }
        internal OriginalAiWorkReport Run(OriginalAiWork work, int attempts = 3, IReadOnlyDictionary<int, long>? quotas = null,
            OriginalAiState? state = null) => Personnel.Run(work, attempts, state ?? State, quotas ?? ImmutableDictionary<int, long>.Empty);
    }

    [Fact]
    public void AdviserRequiresFiveOrdinaryPeopleAndUsesCurrentIntelligenceLoyaltyAndStableTies()
    {
        var content = Fixture();
        content = content with { Officers = content.Officers.Concat(Enumerable.Range(10, 3).Select(id => new OfficerInitialState($"officer.{id}",
            content.Officers[1].State with { Id = id, BaseAbilities = new(50, 90, 50, 50) }))).ToArray() };
        var h = new Harness(content);
        var report = h.Run(OriginalAiWork.Adviser);
        Assert.Equal(1, report.Submitted);
        Assert.Equal(10, h.Engine.Snapshot.Forces.Single(f => f.Id == 1).AdviserId);
        var insufficient = new Harness(content with { Officers = content.Officers.Where(o => o.State.Id != 12).ToArray() });
        Assert.Equal(OriginalAiWorkEnd.PreparationFailed, insufficient.Run(OriginalAiWork.Adviser).End);
        var disloyal = new Harness(content with { Officers = content.Officers.Select(o => o.State.Id >= 10 ?
            o with { State = o.State.WithLoyalty(1, 79) } : o).ToArray() });
        Assert.Empty(disloyal.Commands);
        Assert.Equal(1, disloyal.Run(OriginalAiWork.Adviser).Submitted);
        Assert.Equal(3, Assert.IsType<AppointAdviserCommand>(Assert.Single(disloyal.Commands)).OfficerId);
    }

    [Fact]
    public void IncumbentAdviserWinsTheIntelligenceCapTieWithoutReplacement()
    {
        var content = Fixture();
        content = content with
        {
            Forces = content.Forces.Select(f => f.Id == 1 ? f with { AdviserId = 3 } : f).ToArray(),
            Officers = content.Officers.Select(o => o.State.Id == 3 ? o with { State = o.State with { Status = OfficerStatus.Strategist,
                BaseAbilities = new(50, 115, 50, 50) } } : o).Concat(Enumerable.Range(10, 4).Select(id => new OfficerInitialState($"officer.{id}",
                    content.Officers[1].State with { Id = id, BirthYear = 188, BaseAbilities = new(50, 115, 50, 50) }))).ToArray()
        };
        var h = new Harness(content);
        Assert.Equal(0, h.Run(OriginalAiWork.Adviser).Submitted);
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void OfficesRetainOccupiedSlotsWhenFilteredRosterExcludesRulerAndDoNotRequireIdleRulerForOrdinarySlots()
    {
        var content = Fixture();
        content = content with
        {
            Information = content.Information with { Offices = [new(901, "office.a", 15000, 10) { GovernorPriority = 4 },
                new(902, "office.b", 15000, 10) { GovernorPriority = 3 }] },
            Officers = content.Officers.Select(o => o.State.Id == 1 ? o with { State = o.State with { Acted = true } } :
                o.State.Id == 3 ? o with { State = o.State with { OfficeId = 901 } } : o).ToArray()
        };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Offices).Submitted);
        var assignments = Assert.IsType<AssignOfficesCommand>(Assert.Single(h.Commands)).Assignments;
        Assert.Contains(assignments, a => a.OfficerId == 2 && a.OfficeId == 902);
        Assert.DoesNotContain(assignments, a => a.OfficeId == 901);
        Assert.Contains(assignments, a => a.OfficerId == 3 && a.OfficeId is null);
        Assert.Null(h.Engine.Snapshot.Officers.Single(p => p.Id == 3).OfficeId);
    }

    [Fact]
    public void RewardUsesHighLoyaltyFirstAndPreservesTwoThousandRootFunds()
    {
        var content = Fixture();
        content = content with
        {
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c.Id == 1 ? c with { Treasury = new(2100, 50000) } : c).ToArray() },
            Officers = content.Officers.Select(o => o.State.Id is 2 or 3 ? o with { State = o.State.WithLoyalty(1, o.State.Id == 2 ? 30 : 99) } : o).ToArray()
        };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Reward).Submitted);
        Assert.Equal(3, Assert.IsType<RewardOfficerCommand>(Assert.Single(h.Commands)).OfficerId);
        Assert.Equal(2000u, h.Engine.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money);
        var before = h.Engine.Snapshot;
        Assert.Equal(0, h.Run(OriginalAiWork.Reward).Submitted);
        StateAssert.Equal(before, h.Engine.Snapshot);
    }

    [Fact]
    public void GrantUsesSharedRetentionPoolAndConfiscationKeepsStrongestNonexclusiveItem()
    {
        var content = Fixture();
        var items = new[] { new TreasureDefinition(901, "item.a", 0, 0, 10, null), new(902, "item.b", 0, 0, 20, null),
            new(903, "item.c", 0, 0, 30, null) };
        content = content with
        {
            Officers = content.Officers.Select(o => o.State.Id == 2 ? o with { State = o.State.WithLoyalty(1, 70) } : o).ToArray(),
            Information = content.Information with { Treasures = items,
                TreasureStates = items.Select(i => new TreasureState(i.Id, 1, TreasureDiscovery.Discovered, null)).ToArray() }
        };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.GrantTreasure).Submitted);
        var grant = Assert.IsType<GrantTreasureCommand>(Assert.Single(h.Commands));
        Assert.Equal(901, grant.TreasureId);
        Assert.Equal(2, grant.OfficerId);
        content = content with
        {
            Officers = content.Officers.Select(o => o.State.Id == 2 ? o with { State = o.State.WithLoyalty(1, 50) } : o).ToArray(),
            Information = content.Information with { Treasures = items.Select(i => i.Id == 901 ? i with { FavoredOwnerIds = [2] } : i).ToArray(),
                TreasureStates = items.Select(i => new TreasureState(i.Id, 2, TreasureDiscovery.Discovered, null)).ToArray() }
        };
        h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.ConfiscateTreasure).Submitted);
        Assert.Equal(902, Assert.IsType<ConfiscateTreasureCommand>(Assert.Single(h.Commands)).TreasureId);
        Assert.All(h.Engine.Snapshot.Treasures.Where(t => t.Id != 902), t => Assert.Equal(2, t.HolderId));
    }

    [Fact]
    public void GrantStopsAtFirstOverloadedRecipientAndRulerPersonalityGate()
    {
        var content = Fixture();
        var items = Enumerable.Range(900, 8).Select(id => new TreasureDefinition(id, $"item.{id}", 0, 0, id - 899, null)).ToArray();
        content = content with
        {
            Officers = content.Officers.Select(o => o.State.Id is 2 or 3 ? o with { State = o.State.WithLoyalty(1, o.State.Id == 2 ? 70 : 80) } : o).ToArray(),
            Information = content.Information with { Treasures = items,
                TreasureStates = items.Select((i, index) => new TreasureState(i.Id, index < 5 ? 2 : 1, TreasureDiscovery.Discovered, null)).ToArray() }
        };
        var h = new Harness(content);
        Assert.Equal(OriginalAiWorkEnd.PreparationFailed, h.Run(OriginalAiWork.GrantTreasure).End);
        Assert.Empty(h.Commands);
        content = content with { Officers = content.Officers.Select(o => o.State.Id == 1 ? o with { State = o.State with { Righteousness = 5, Ambition = 11 } } : o).ToArray() };
        h = new Harness(content);
        Assert.Equal(OriginalAiWorkEnd.PreparationFailed, h.Run(OriginalAiWork.GrantTreasure).End);
    }

    [Theory]
    [InlineData(19, 60, false, true)]
    [InlineData(20, 60, false, false)]
    [InlineData(19, 61, false, false)]
    [InlineData(100, 60, true, true)]
    public void CaptiveConfiscationUsesRelationshipBandAndRecruitingLoyalty(int relation, int loyalty, bool unaffiliated, bool eligible)
    {
        var content = Fixture();
        content = content with
        {
            Officers = content.Officers.Select(o => o.State.Id == 5 ? o with { State = o.State.WithLoyalty(1, loyalty) with
                { OwnerId = unaffiliated ? null : 2, Status = OfficerStatus.Captive, SettlementId = 1 } } : o).ToArray(),
            Information = content.Information with { Relations = [new(1, 2, relation)],
                Treasures = [new(901, "item.a", 0, 0, 10, null), new(902, "item.b", 0, 0, 20, null)],
                TreasureStates = [new(901, 5, TreasureDiscovery.Discovered, null), new(902, 5, TreasureDiscovery.Discovered, null)] }
        };
        var h = new Harness(content);
        Assert.Equal(eligible ? 1 : 0, h.Run(OriginalAiWork.ConfiscateTreasure).Submitted);
        if (eligible) Assert.Equal(901, Assert.IsType<ConfiscateTreasureCommand>(Assert.Single(h.Commands)).TreasureId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalRecruitUsesTargetFirstMaximumAbilityOrderConsumesRejectedPairAndKeepsRawScoresAbove100(bool reject)
    {
        var content = Fixture();
        content = content with { Officers = content.Officers.Select(o => o.State.Id == 5 ? o with { State = o.State.WithLoyalty(2, 20) with
                { Status = OfficerStatus.Captive, SettlementId = 1, BaseAbilities = new(110, 40, 40, 40) } } :
            o.State.Id == 6 ? o with { State = o.State with { Status = OfficerStatus.Free, OwnerId = null } } : o).ToArray() };
        var h = new Harness(content, reject: reject);
        var freePreview = h.Engine.GetPersonnelJourney(1, 1, 6, PersonnelJourneyKind.Recruit);
        Assert.True(freePreview.Available, freePreview.ToString());
        Assert.True(h.Engine.GetRecruitmentThreshold(1, 6) >= 20, h.Engine.GetRecruitmentThreshold(1, 6).ToString());
        var before = h.Engine.Snapshot;
        var report = h.Run(OriginalAiWork.RecruitLocal, 2);
        var commands = h.Commands.Cast<RecruitOfficerCommand>().ToArray();
        Assert.Equal(2, commands.Length);
        Assert.Equal(5, commands[0].TargetOfficerId);
        Assert.Equal(2, commands[0].OfficerId);
        Assert.True(h.Engine.GetRecruitmentThreshold(2, 5) > 100);
        Assert.NotEqual(commands[0].OfficerId, commands[1].OfficerId);
        Assert.NotEqual(commands[0].TargetOfficerId, commands[1].TargetOfficerId);
        Assert.Equal(reject ? 2 : 0, report.Rejected);
        if (reject) StateAssert.Equal(before, h.Engine.Snapshot);
        else Assert.Equal(2, h.Engine.Snapshot.Simulation.Tasks.Count(t => t.Kind == "personnel-recruit"));
    }

    [Fact]
    public void EnemyRecruitUsesStrategicSettlementOwnerLoyaltyOrderAndSeparatePoliticsFloor()
    {
        var content = Fixture();
        content = content with { Officers = content.Officers.Select(o => o.State.Id is 5 or 6 ?
                o with { State = o.State.WithLoyalty(2, o.State.Id == 5 ? 90 : 20) } :
                o.State.Id == 2 ? o with { State = o.State with { BaseAbilities = new(50, 50, 44, 50) } } : o).ToArray() };
        var h = new Harness(content);
        var state = h.State with { Corps = [new(1, OriginalAiMode.TargetSettlement, null, 2)] };
        var preview = h.Engine.GetPersonnelJourney(1, 1, 6, PersonnelJourneyKind.Recruit);
        Assert.True(preview.Available, preview.ToString());
        Assert.Equal(1, h.Run(OriginalAiWork.RecruitEnemy, 1, state: state).Submitted);
        var recruit = Assert.IsType<RecruitOfficerCommand>(Assert.Single(h.Commands));
        Assert.Equal(6, recruit.TargetOfficerId);
        Assert.NotEqual(2, recruit.OfficerId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public void InfeasibleRecruitmentConsumesEmptyBudgetWithoutCommandsDrawsOrStateChanges(int attempts)
    {
        var content = Fixture();
        content = content with { Officers = content.Officers.Select(o => o.State.Id is 5 or 6
            ? o with { State = o.State.WithLoyalty(2, 20).WithLoyalty(1, 0) } : o).ToArray() };
        var h = new Harness(content, _ => throw new InvalidOperationException("Unassigned recruitment must not draw."));
        var before = h.Engine.Snapshot;
        var report = h.Run(OriginalAiWork.RecruitEnemy, attempts);
        Assert.Equal(OriginalAiWorkEnd.AttemptsExhausted, report.End);
        Assert.Equal(attempts, report.EmptyPlans);
        Assert.Equal(attempts, report.Attempts);
        Assert.Empty(h.Commands);
        StateAssert.Equal(before, h.Engine.Snapshot);
    }

    [Theory]
    [InlineData(49, 0, 1)]
    [InlineData(50, 74, 2)]
    [InlineData(50, 75, 2)]
    public void RecruitmentGroupGatesShortCircuitInOrder(int first, int second, int expectedDraws)
    {
        var content = Fixture();
        int calls = 0;
        var h = new Harness(content, maximum => { Assert.Equal(100, maximum); return calls++ == 0 ? first : second; });
        var state = h.State with { Forces = h.State.Forces.Select(f => f with { Group = f.ForceId == 1 ? OriginalAiGroup.ReducedAttempts : OriginalAiGroup.Favored }).ToImmutableArray() };
        var report = h.Run(OriginalAiWork.RecruitEnemy, 1, state: state);
        Assert.Equal(expectedDraws, calls);
        Assert.Equal(first < 50 || second < 75 ? OriginalAiWorkEnd.ProbabilityFailed : OriginalAiWorkEnd.NoCandidates, report.End);
    }

    [Fact]
    public void SearchUsesPublicAdviceWithoutReadingHiddenOfficerIdentityAndConsumesDifferentActors()
    {
        var content = Fixture();
        content = content with
        {
            Forces = content.Forces.Select(f => f.Id == 1 ? f with { AdviserId = 3 } : f).ToArray(),
            Settlements = content.Settlements.Append(content.Settlements[0] with { Id = 3, X = 3, Y = 3 }).ToArray(),
            Officers = content.Officers.Select(o => o.State.Id == 3 ? o with { State = o.State with { Status = OfficerStatus.Strategist,
                BaseAbilities = new(50, 115, 70, 50) } } : o).Concat(Enumerable.Range(100, 4).Select(id => new OfficerInitialState($"officer.{id}",
                    new(id, null, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered }))).ToArray()
        };
        var h = new Harness(content);
        var before = h.Engine.Snapshot;
        Assert.Equal(1, h.Run(OriginalAiWork.Search, 2).Submitted);
        Assert.Equal(3, Assert.IsType<SearchCommand>(Assert.Single(h.Commands)).DestinationId);
        Assert.All(h.Engine.Snapshot.Officers.Where(p => p.Status == OfficerStatus.Undiscovered), p =>
            Assert.Equal(before.Officers.Single(o => o.Id == p.Id), p));
        var unknown = new Harness(content with { Forces = content.Forces.Select(f => f with { AdviserId = null }).ToArray(),
            Officers = content.Officers.Select(o => o.State.Id == 3 ? o with { State = o.State with { Status = OfficerStatus.Serving } } : o).ToArray() });
        Assert.Equal(0, unknown.Run(OriginalAiWork.Search).Submitted);
    }

    [Theory]
    [InlineData(0, 60, 41, 89, 2)]
    [InlineData(0, 59, 41, 89, 0)]
    [InlineData(0, 39, 41, 89, 1)]
    [InlineData(0, 40, 41, 89, 0)]
    [InlineData(0, 39, 40, 89, 0)]
    [InlineData(0, 39, 41, 90, 0)]
    [InlineData(1, 9, 0, 115, 1)]
    [InlineData(1, 10, 41, 89, 0)]
    public void PrisonerClassificationPreservesStrictThresholdsAndCurrentAbilityProtection(int threshold, int relation, int affinity, int maximum, int expected)
    {
        Assert.Equal((PrisonerDisposition)expected, OriginalAiPersonnel.Disposition(threshold, relation, affinity, new(maximum, 1, 1, 1)));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void CpuPrisonerExecutionUsesReducedProbabilityAndExhaustedDrawsProduceOneEmptyAttempt(int roll, bool execute)
    {
        var content = Fixture();
        content = content with
        {
            Information = content.Information with { Relations = [new(1, 2, 0)] },
            Officers = content.Officers.Select(o => o.State.Id == 5 ? o with { State = o.State with { Status = OfficerStatus.Captive, SettlementId = 1 } } : o).ToArray()
        };
        var h = new Harness(content, _ => roll);
        var progress = h.Personnel.PreparePrisoners();
        Assert.Equal(new[] { 5 }, progress.Execute);
        progress = h.Personnel.Dispose(progress, 2);
        Assert.Equal(1, progress.Attempts);
        Assert.Equal(execute ? 0 : 1, progress.EmptyPlans);
        Assert.Equal(execute ? 1 : 0, progress.Submitted);
        Assert.Empty(progress.Execute);
        Assert.Equal(execute ? OfficerStatus.Dead : OfficerStatus.Captive, h.Engine.Snapshot.Officers.Single(p => p.Id == 5).Status);
    }

    [Theory]
    [InlineData(49, true)]
    [InlineData(50, false)]
    public void UnaffiliatedPrisonerUsesDefaultRelationAndPublicExileCommand(int roll, bool release)
    {
        var content = Fixture();
        content = content with { Officers = content.Officers.Append(new OfficerInitialState("prisoner", new(900, null, 1,
            new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive })).ToArray() };
        var h = new Harness(content, _ => roll);
        var progress = h.Personnel.PreparePrisoners();
        Assert.Equal(new[] { 900 }, progress.Release);
        progress = h.Personnel.Dispose(progress, 2);
        Assert.Equal(release ? 1 : 0, progress.Submitted);
        if (release) Assert.IsType<ExileOfficerCommand>(Assert.Single(h.Commands));
        Assert.Equal(release ? OfficerStatus.Free : OfficerStatus.Captive, h.Engine.Snapshot.Officers.Single(p => p.Id == 900).Status);
    }

    [Fact]
    public void QuotasCountServingOccupiedPersonnelExcludeCaptivesAndUseWideArithmetic()
    {
        var content = Fixture();
        content = content with
        {
            Settlements = [content.Settlements[0] with { Soldiers = 100, DraftPopulation = 100, Wounded = 50, MerchantPresenceChance = 100 }, content.Settlements[1]],
            Officers = content.Officers.Select(o => o.State.Id == 2 ? o with { State = o.State with { Acted = true } } : o)
                .Append(new("prisoner", new(900, 2, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Captive })).ToArray()
        };
        var h = new Harness(content);
        var geography = new OriginalAiGeography(content, null, h.Engine.Snapshot);
        Assert.Equal(3, OriginalAiStaffing.Quotas(h.Engine.Snapshot, 1, geography)[1]);
        Assert.Equal(3, OriginalAiStaffing.Counts(h.Engine.Snapshot, 1)[1]);
        Assert.Equal(1000, OriginalAiStaffing.Demand(1000, uint.MaxValue, uint.MaxValue, uint.MaxValue,
            (long)uint.MaxValue * 2 + uint.MaxValue / 2, 2));
        Assert.Equal(2, OriginalAiStaffing.Demand(0, 0, 0, 0, 0, 2));
    }

    [Fact]
    public void MovementConsumesOnlySelectedSourceAndCurrentDestinationThenPublishesJourneys()
    {
        var content = Fixture();
        var city = content.Settlements[0];
        content = content with
        {
            Settlements = content.Settlements.Concat(Enumerable.Range(3, 4).Select(id => city with { Id = id, X = id, Y = id })).ToArray(),
            Officers = content.Officers.Select(o => o.State.Id is 2 or 3 ? o with { State = o.State with { SettlementId = 3 } } : o)
                .Append(new("extra", content.Officers[1].State with { Id = 900, SettlementId = 4 })).ToArray()
        };
        var h = new Harness(content);
        var quotas = new Dictionary<int, long> { [1] = 1, [3] = 1, [4] = 0, [5] = 1, [6] = 1 };
        Assert.Equal(2, h.Run(OriginalAiWork.MoveOfficers, 5, quotas).Submitted);
        var moves = h.Commands.Cast<MoveOfficersCommand>().ToArray();
        Assert.Equal(new[] { 5, 6 }, moves.Select(m => m.DestinationId));
        Assert.Equal(2, moves.SelectMany(m => m.OfficerIds).Distinct().Count());
        Assert.Equal(1, OriginalAiStaffing.Counts(h.Engine.Snapshot, 1)[3]);
        Assert.DoesNotContain(4, OriginalAiStaffing.Counts(h.Engine.Snapshot, 1).Keys);
        Assert.All(h.Engine.Snapshot.Simulation.Tasks, t => Assert.Equal("personnel-move", t.Kind));
    }

    [Fact]
    public void EmptyCitiesBorrowWithoutSurplusButNeverEmptyTheSource()
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Concat(Enumerable.Range(3, 3)
                .Select(id => content.Settlements[0] with { Id = id, X = id, Y = id })).ToArray()
        };
        var h = new Harness(content);
        var quotas = new Dictionary<int, long> { [1] = 3, [3] = 1, [4] = 1, [5] = 1 };
        var report = h.Run(OriginalAiWork.MoveOfficers, 255, quotas);
        Assert.Equal(2, report.Submitted);
        Assert.Equal(1, report.EmptyPlans);
        var moves = h.Commands.Cast<MoveOfficersCommand>().ToArray();
        Assert.Equal(new[] { 3, 4 }, moves.Select(m => m.DestinationId));
        Assert.All(moves, m => Assert.Single(m.OfficerIds));
        Assert.Equal(2, moves.SelectMany(m => m.OfficerIds).Distinct().Count());
        Assert.Equal(1, OriginalAiStaffing.Counts(h.Engine.Snapshot, 1)[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmergencyMovementSkipsUnavailableSurplusAndDoesNotSkipNextDestination(bool reject)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Concat(Enumerable.Range(3, 3)
                .Select(id => content.Settlements[0] with { Id = id, X = id, Y = id })).ToArray(),
            Officers = content.Officers.Select(o => o.State.Id == 1 ? o with { State = o.State with { Acted = true } } :
                o.State.Id is 2 or 3 ? o with { State = o.State with { SettlementId = 3 } } : o).ToArray()
        };
        var h = new Harness(content, reject: reject);
        var quotas = new Dictionary<int, long> { [1] = 0, [3] = 2, [4] = 1, [5] = 1 };
        var report = h.Run(OriginalAiWork.MoveOfficers, 255, quotas);
        var moves = h.Commands.Cast<MoveOfficersCommand>().ToArray();
        Assert.Equal(reject ? 2 : 1, moves.Length);
        Assert.Equal(4, moves[0].DestinationId);
        Assert.All(moves, m => Assert.Single(m.OfficerIds));
        if (reject)
        {
            Assert.Equal(5, moves[1].DestinationId);
            Assert.Equal(2, report.Rejected);
            Assert.Equal(2, OriginalAiStaffing.Counts(h.Engine.Snapshot, 1)[3]);
        }
        else
        {
            Assert.Equal(1, report.EmptyPlans);
            Assert.Equal(1, OriginalAiStaffing.Counts(h.Engine.Snapshot, 1)[3]);
        }
    }

    [Fact]
    public void RecruitmentForecastIsPureAndUsesTheResolutionThreshold()
    {
        var content = Fixture();
        content = content with { Officers = content.Officers.Select(o => o.State.Id == 5 ?
            o with { State = o.State.WithLoyalty(2, 20) } : o).ToArray() };
        var h = new Harness(content);
        var before = h.Engine.Snapshot;
        int threshold = h.Engine.GetRecruitmentThreshold(2, 5);
        Assert.Equal(threshold, h.Engine.GetRecruitmentThreshold(2, 5));
        StateAssert.Equal(before, h.Engine.Snapshot);
        Assert.Equal(1, h.Run(OriginalAiWork.RecruitEnemy, 1).Submitted);
        h.Engine.EndControl(new("player"));
        h.Engine.EndControl(new("ai:2"));
        var result = h.Engine.AdvanceXun();
        var resolved = Assert.Single(result.Events.OfType<RecruitmentResolvedEvent>());
        Assert.Equal(threshold, resolved.Threshold);
        Assert.Equal(5, resolved.TargetOfficerId);
    }

    private static ScenarioInitialState OrganizationFixture()
    {
        var content = Fixture();
        var settlements = content.Settlements.Concat(Enumerable.Range(3, 7).Select(id => content.Settlements[0] with
            { Id = id, X = id, Y = id, Soldiers = 0, DraftPopulation = 0 })).ToArray();
        return content with
        {
            Settlements = settlements.Select(s => s with { Soldiers = 0, DraftPopulation = 0, Morale = 0 }).ToArray(),
            CityProvinceIds = settlements.ToDictionary(s => s.Id, s => s.Id is 6 or 7 ? 1 : s.Id is 8 or 9 ? 2 : 0),
            ProvinceNameKeys = new() { [0] = "province.0", [1] = "province.1", [2] = "province.2" },
            RegionCityIds = settlements.ToDictionary(s => s.Id, s => s.Id),
            RegionNameKeys = settlements.ToDictionary(s => s.Id, s => $"region.{s.Id}"),
            CityConnections = [new(6, 2), new(8, 2)],
            Officers = content.Officers.Concat(Enumerable.Range(3, 7).Select(id => new OfficerInitialState($"officer.{100 + id}",
                content.Officers[1].State with { Id = 100 + id, SettlementId = id, IsCommander = true }))).ToArray()
        };
    }

    [Fact]
    public void OrganizationCreatesMultipleRegionsWithCurrentResourceSharesAndChosenGovernors()
    {
        var content = OrganizationFixture();
        var h = new Harness(content);
        bool Submit(GameCommand command)
        {
            h.Commands.Add(command);
            var result = h.Engine.Execute(new("player"), command);
            Assert.True(result.Submission.Accepted, result.Submission.Rejection.ToString());
            return true;
        }
        var before = h.Engine.Snapshot;
        var report = OriginalAiStaffing.Organize(h.Engine, content, 1, new(content, null, before), Submit);
        Assert.Equal(2, report.Submitted);
        var children = h.Engine.Snapshot.Corps.Where(c => c.OwnerId == 1 && !c.IsRoot).ToArray();
        Assert.Equal(new int?[] { 106, 108 }, children.Select(c => c.LeaderId));
        Assert.Equal(new uint[] { 12500, 12500 }, children.Select(c => c.Treasury.Food));
        Assert.Equal(10000UL, h.Engine.Snapshot.Corps.Where(c => c.OwnerId == 1).Aggregate(0UL, (sum, c) => sum + c.Treasury.Money));
        Assert.Equal(50000UL, h.Engine.Snapshot.Corps.Where(c => c.OwnerId == 1).Aggregate(0UL, (sum, c) => sum + c.Treasury.Food));
        Assert.All(children, c => Assert.Equal(CorpsControlMode.Automatic, c.ControlMode));
        Assert.All(h.Commands, c => Assert.NotNull(Assert.IsType<CreateCorpsCommand>(c).GovernorId));
    }

    [Fact]
    public void ProductionAppendsNewCorpsAndRecomputesRootQuotasBeforeSameXunMovementAndReplays()
    {
        var content = OrganizationFixture();
        var initial = new Harness(content).Engine.Snapshot;
        var session = GameSession.Restore(content, new(initial, [], [], 0) { OriginalAi = OriginalAiState.Create(content) }, GameSession.EngineFactory(content));
        // Explicitly delegate the root: descendants created by its public command must get a turn too.
        Assert.True(session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1, 0, 1, CorpsControlMode.Automatic)).Submission.Accepted);
        session.BeginRecording();
        var result = session.Apply(session.Request(new EndControlOperation(), "player"));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(2, result.Events.OfType<CorpsCreatedEvent>().Count());
        var children = session.Snapshot.Corps.Where(c => c.OwnerId == 1 && !c.IsRoot).ToArray();
        Assert.Equal(2, children.Length);
        foreach (var child in children)
        {
            Assert.Contains(session.LastOriginalAiWork, r => r.CorpsId == child.Id && r.Work == OriginalAiWork.MoveOfficers);
            Assert.Contains(session.OriginalAi.Corps, c => c.CorpsId == child.Id);
        }
        Assert.Null(session.OriginalAi.Continuation);
        Assert.DoesNotContain(result.Events.OfType<TaskStartedEvent>(), e => e.Task.ForceId == 1 && e.Task.ControllerId == "corps:1" &&
            children.Any(c => session.Snapshot.Settlements.Any(s => s.Id == e.Task.Journey?.DestinationPlaceId && s.CorpsId == c.Id)));
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content), verifyEvents: true);
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void DissolvedQueuedCorpsAreRemovedAndRootSeesTheirTerritoryInTheSamePass()
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Append(content.Settlements[0] with { Id = 3, X = 3, Y = 3, CorpsId = 10 }).ToArray(),
            Officers = content.Officers.Select(o => o.State.Id == 2 ? o with { State = o.State with { Status = OfficerStatus.Governor, SettlementId = 3 } } : o).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Append(new(10, 1, 2, new(1000, 2000))
                { ControlMode = CorpsControlMode.Automatic }).ToArray() }
        };
        var initial = new Harness(content).Engine.Snapshot;
        var session = GameSession.Restore(content, new(initial, [], [], 0) { OriginalAi = OriginalAiState.Create(content),
            PlayerForceController = ControllerKind.Player }, GameSession.EngineFactory(content));
        session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1, 0, 1, CorpsControlMode.Automatic));
        var result = session.Apply(session.Request(new EndControlOperation(), "player"));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Single(result.Events.OfType<CorpsDissolvedEvent>());
        Assert.DoesNotContain(session.Snapshot.Corps, c => c.Id == 10);
        Assert.DoesNotContain(session.LastOriginalAiWork, r => r.CorpsId == 10);
        Assert.Equal(1, session.Snapshot.Settlements.Single(s => s.Id == 3).CorpsId);
        Assert.Contains(session.LastOriginalAiWork, r => r.CorpsId == 1 && r.Work == OriginalAiWork.MoveOfficers);
    }

    [Fact]
    public void AutomaticExecutionPausesForPlayerSuccessionAndResumesTheRemainingWorkExactlyOnce()
    {
        var content = Fixture();
        content = content with
        {
            Information = content.Information with { Relations = [new(1, 2, 0)] },
            Officers = content.Officers.Select(o => o.State.Id == 1 ? o with { State = o.State with { Status = OfficerStatus.Captive, SettlementId = 2 } } : o).ToArray(),
            OriginalAi = content.OriginalAi with { WorkRules = content.OriginalAi.WorkRules.ToImmutableDictionary(p => p.Key,
                p => p.Value with { OpeningWaitDays = p.Key == OriginalAiWork.DisposePrisoner ? 0 : 100000 }) }
        };
        var initial = new Harness(content).Engine.Snapshot;
        const ulong runSeed = 9210881921195698695UL;
        var stream = new StableRandom(StableRandom.DeriveSeed(runSeed, "original-ai:2:68040"));
        Assert.Equal(0UL, stream.NextUInt64() % 100);
        initial = initial with { Simulation = initial.Simulation with { RunSeed = runSeed } };
        var session = GameSession.Restore(content, new(initial, [], [], 0) { OriginalAi = OriginalAiState.Create(content) }, GameSession.EngineFactory(content));
        session.BeginRecording();
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new EndControlOperation(), "player")).Disposition);
        var result = session.Apply(session.Request(new EndControlOperation(), "ai:2"));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(SimulationBoundary.AwaitingDecision, session.Snapshot.Simulation.Boundary);
        var cursor = Assert.IsType<OriginalAiContinuation>(session.OriginalAi.Continuation);
        Assert.Equal(1, cursor.Prisoners!.Submitted);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        long sequence = session.Snapshot.Simulation.NextSequence;
        var command = new ChooseExecutionSuccessorCommand(Guid.NewGuid(), 1, sequence, 1, 2);
        var request = session.Request(new CommandOperation(command), "player", command.CommandId);
        result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Null(session.OriginalAi.Continuation);
        Assert.Equal(OfficerStatus.Dead, session.Snapshot.Officers.Single(p => p.Id == 1).Status);
        Assert.Equal(2, session.Snapshot.Forces.Single(f => f.Id == 1).LeaderId);
        Assert.Single(result.Events.OfType<ControlEndedEvent>());
        Assert.DoesNotContain(result.Events, e => e is StageCompletedEvent);
        Assert.DoesNotContain(session.LastOriginalAiWork, r => r.Work == OriginalAiWork.Offices);
        Assert.Contains(session.LastOriginalAiWork, r => r.Work == OriginalAiWork.GrantTreasure);
        Assert.Equal(1, session.LastOriginalAiWork.Single(r => r.Work == OriginalAiWork.DisposePrisoner).Submitted);
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content, GameSession.EngineFactory(content), verifyEvents: true);
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void DelegatedChildPersonnelCannotUseDirectRootPeopleOrTreasury()
    {
        var content = Fixture();
        content = content with
        {
            Settlements = content.Settlements.Concat(new[] { 3, 4 }.Select(id => content.Settlements[0] with { Id = id, X = id, Y = id, CorpsId = 10 })).ToArray(),
            Officers = content.Officers.Select(o => o.State.Id is 2 or 3 ? o with { State = o.State.WithLoyalty(1, 70) with
                { SettlementId = 3, Status = o.State.Id == 2 ? OfficerStatus.Governor : OfficerStatus.Serving } } : o).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Append(new(10, 1, 2, new(5000, 3000))
                { ControlMode = CorpsControlMode.Automatic }).ToArray() }
        };
        var initial = new Harness(content).Engine.Snapshot;
        var session = GameSession.Restore(content, new(initial, [], [], 0) { OriginalAi = OriginalAiState.Create(content) }, GameSession.EngineFactory(content));
        var root = session.Snapshot.Corps.Single(c => c.Id == 1);
        var result = session.Apply(session.Request(new EndControlOperation(), "player"));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(root.Treasury, session.Snapshot.Corps.Single(c => c.Id == 1).Treasury);
        Assert.False(session.Snapshot.Officers.Single(p => p.Id == 1).Acted);
        Assert.All(session.LastOriginalAiWork, r => Assert.Equal(10, r.CorpsId));
        Assert.DoesNotContain(result.Events.OfType<ResourceChangedEvent>(), e => e.Account == new ResourceAccount(ResourceOwnerKind.Corps, 1));
        Assert.All(result.Events.OfType<TaskStartedEvent>(), e => Assert.Contains(e.Task.OfficerId, new[] { 2, 3 }));
    }

    [Fact]
    public void FailedAdvanceRollsBackCreatedCorpsPersonnelStrategyAndCommandIdentity()
    {
        var content = OrganizationFixture();
        var rules = new FailAfterOrganization
        {
            Personnel = new(content.Settlements.Select(s => new PersonPlace(s.Id, s.Position)), content.RegionCityIds,
                content.CityConnections, content.CityProvinceIds)
        };
        var session = GameSession.Start(content, 190, world => new SimulationEngine(world, rules), playerForceController: ControllerKind.Automatic);
        var before = session.Capture();
        var request = session.Request(new AdvanceOperation());
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Rejected, result.Disposition);
        Assert.True(rules.SawCreatedCorps);
        StateAssert.Equal(before, session.Capture());
        Assert.Empty(session.LastOriginalAiWork);
        Assert.Equal(request.Sequence, session.Request(new AdvanceOperation()).Sequence);
    }

    private sealed class FailAfterOrganization : SimulationRules
    {
        internal bool SawCreatedCorps { get; private set; }
        internal override void ApplyStage(ResolutionStage stage, WorldMutation world, List<DomainEvent> events)
        {
            if (stage != ResolutionStage.BeforeDailyCommands) return;
            SawCreatedCorps = world.Corps.Count > 2;
            throw new InvalidOperationException("Injected failure after personnel and organization work.");
        }
    }
}
