using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class OriginalAiActionsTests
{
    private static ScenarioInitialState Fixture()
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with
        {
            Forces = [new(1, "one", 1, 1000) { LeaderId = 1 }, new(2, "two", 2, 0) { LeaderId = 2 },
                new(3, "three", 3, 100) { LeaderId = 3 }],
            Settlements = [basis.Settlements[0] with { Soldiers = 500000, Morale = 100, Durability = 500, MaxDurability = 500 },
                basis.Settlements[1] with { X = 7, Y = 1, Soldiers = 10000, Durability = 500, MaxDurability = 500 },
                basis.Settlements[1] with { Id = 3, OwnerId = 3, X = 13, Y = 1, Soldiers = 20000, NameKey = "three" }],
            Officers = [Person(1, 1, 1, 100, OfficerStatus.Ruler), Person(11, 1, 1, 100), Person(12, 1, 1, 90),
                Person(2, 2, 2, 30, OfficerStatus.Ruler), Person(4, 2, 2, 30), Person(3, 3, 3, 30, OfficerStatus.Ruler),
                Person(8, 1, null, 70) with { State = Person(8, 1, null, 70).State with { UnitId = 10 } },
                Person(9, 2, null, 30) with { State = Person(9, 2, null, 30).State with { UnitId = 20 } }],
            Units = [new UnitState(10, 1, new(17, 7), 5000) { FormationId = 400, ServicePlaceId = 1, CommanderId = 8,
                Morale = 60, Order = new(MilitaryOrderKind.Move, new(32, 7), null),
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(8, BattleRank.Middle) },
                new UnitState(20, 2, new(27, 7), 5000) { FormationId = 400, ServicePlaceId = 2, CommanderId = 9,
                Morale = 100, Order = new(MilitaryOrderKind.AttackSettlement, null, 1),
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(9, BattleRank.Middle) }],
            Information = basis.Information with { Corps = [new(1, 1, 1, new(100000, 100000)) { IsRoot = true },
                new(2, 2, 2, new(100000, 100000)) { IsRoot = true }, new(3, 3, 3, new(100000, 100000)) { IsRoot = true }],
                Relations = [new(1, 2, 0), new(1, 3, 80), new(2, 3, 20)] },
            CityConnections = [new(1, 2), new(2, 1), new(2, 3), new(3, 2)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3 },
            RegionNameKeys = new() { [1] = "one", [2] = "two", [3] = "three" },
            Military = new(20, 3, Enumerable.Repeat(0, 60).ToImmutableArray(), [new(0, 1, null)],
                [new(400, 10, 0, 30000, 5, new(1, 1), TravelMode.Land)
                    { CaptureRanks = Enumerable.Repeat(BattleRank.Middle, 5).ToImmutableArray() }],
                [new(1, new(7, 7), new(1, 1)), new(2, new(37, 7), new(1, 1)), new(3, new(67, 7), new(1, 1))])
                { RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 6 ? 1 : i % 20 < 12 ? 2 : 3)).ToImmutableArray(),
                    TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 60).ToImmutableArray() },
            OriginalAi = OriginalAiDefinition.Default with { Advanced = true,
                WorkRules = OriginalAiDefinition.Default.WorkRules.ToImmutableDictionary(p => p.Key,
                    p => p.Value with { OpeningWaitDays = 0, PeriodDays = 0 }) }
        };
    }
    private static OfficerInitialState Person(int id, int owner, int? home, int skill, OfficerStatus status = OfficerStatus.Serving) =>
        new($"person.{id}", new OfficerState(id, owner, home, new(skill, skill, skill, skill), false)
            { Status = status, BirthYear = 160, Righteousness = 0 }.WithLoyalty(owner, 80));

    private sealed class Harness
    {
        internal SimulationEngine Engine { get; }
        internal OriginalAiState State { get; set; }
        internal OriginalAiActions Actions { get; }
        internal List<GameCommand> Commands { get; } = [];
        internal Harness(ScenarioInitialState? content = null, Func<WorldSnapshot, WorldSnapshot>? change = null,
            Func<int, int>? draw = null, bool reject = false)
        {
            content ??= Fixture();
            var snapshot = new ScenarioSessionDefinition(content).CreateWorld(42).CreateSnapshot();
            snapshot = snapshot with { Simulation = snapshot.Simulation with { Boundary = SimulationBoundary.Stable, OtherForceOrdersVisible = true } };
            Engine = GameSession.EngineFactory(content)(WorldState.Restore(change?.Invoke(snapshot) ?? snapshot));
            State = OriginalAiState.Create(content) with { Corps = [new(1, OriginalAiMode.TargetForce, 2, null)], DiplomaticOrder = [2, 1, 3] };
            var context = new CommandContext("player");
            Actions = new(Engine, content, new(content.Military!), 1, context, new(draw ?? (maximum => maximum - 1)), command =>
            {
                Commands.Add(command);
                if (reject) return false;
                var result = Engine.Execute(context, command);
                Assert.True(result.Submission.Accepted, $"{command}: {result.Submission.Rejection}");
                return true;
            });
        }
        internal OriginalAiWorkReport Run(OriginalAiWork work, int limit = 1) => Actions.Run(work, limit, State, State.Corps[0]);
    }

    [Theory]
    [InlineData(OriginalAiWork.FalseReport, "unit-strategy")]
    [InlineData(OriginalAiWork.Confuse, "unit-strategy")]
    [InlineData(OriginalAiWork.Encourage, "unit-strategy")]
    [InlineData(OriginalAiWork.Reinforce, "unit-strategy")]
    [InlineData(OriginalAiWork.Alienate, "strategy-alienate")]
    [InlineData(OriginalAiWork.Burn, "strategy-burn")]
    [InlineData(OriginalAiWork.Seize, "strategy-seize")]
    [InlineData(OriginalAiWork.Gossip, "strategy-gossip")]
    [InlineData(OriginalAiWork.RequestAttack, "diplomacy-attack-request")]
    [InlineData(OriginalAiWork.Surrender, "diplomacy-surrender")]
    public void ProductionWorkStartsRealPublicMission(OriginalAiWork work, string kind)
    {
        var h = new Harness(change: w => work == OriginalAiWork.Confuse ? w with { Units = w.Units.Select(u => u.Id == 10
            ? u with { Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 20 } } : u).ToImmutableList() } : w);
        if (work == OriginalAiWork.Surrender) h.State = h.State with { DiplomaticOrder = [1, 2, 3] };
        Assert.Equal(1, h.Run(work).Submitted);
        var task = Assert.Single(h.Engine.Snapshot.Simulation.Tasks);
        Assert.Equal(kind, task.Kind);
        Assert.True(h.Engine.Snapshot.Officers.Single(p => p.Id == task.OfficerId).Acted);
        if (work == OriginalAiWork.Reinforce)
        {
            Assert.Equal(497000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
            Assert.Equal(3000u, task.Reinforcements!.Soldiers);
        }
    }

    [Theory]
    [InlineData(OriginalAiWork.Burn)]
    [InlineData(OriginalAiWork.Seize)]
    public void BuildingStrategiesUseTheAppointedCommanderAfterTheRulerLeaves(OriginalAiWork work)
    {
        var content = Fixture();
        content = content with { Officers = content.Officers.Select(p => p.State.Id == 2
            ? p with { State = p.State with { BaseAbilities = new(100, 100, 100, 100) } } : p).ToArray() };
        var h = new Harness(content, change: w =>
        {
            int active = w.Simulation.ActiveControl;
            int enemy = w.Simulation.Controls.IndexOf(w.Simulation.Controls.Single(c => c.ForceId == 2));
            var engine = GameSession.EngineFactory(content)(WorldState.Restore(w with
                { Simulation = w.Simulation with { ActiveControl = enemy } }));
            Assert.True(engine.Execute(new(w.Simulation.Controls[enemy].ControllerId),
                new SearchCommand(Guid.NewGuid(), 2, engine.Snapshot.Simulation.NextSequence, 2, 2)).Submission.Accepted);
            var after = engine.Snapshot;
            Assert.False(after.Officers.Single(p => p.Id == 2).IsCommander);
            Assert.True(after.Officers.Single(p => p.Id == 4).IsCommander);
            return after with { Simulation = after.Simulation with { ActiveControl = active } };
        });
        Assert.Equal(1, h.Run(work).Submitted);
    }

    [Fact]
    public void GiftForecastAndSubmissionUseSamePayloadAndRejectedTargetDoesNotSpendResources()
    {
        var content = Fixture() with { Information = Fixture().Information with { Relations = [new(1, 2, 40), new(1, 3, 80), new(2, 3, 20)] } };
        var h = new Harness(content);
        var before = h.Engine.Snapshot;
        Assert.Equal(1, h.Run(OriginalAiWork.Gift).Submitted);
        var command = Assert.IsType<GiftCommand>(Assert.Single(h.Commands));
        Assert.Equal(2, command.TargetForceId);
        Assert.Equal(new(GiftKind.Money, 10000, null), command.Offer);
        Assert.Equal(DiplomacyMath.GiftMoneyThreshold(100, 40, 10000),
            GameSession.EngineFactory(content)(WorldState.Restore(before)).GetMissionDecision(new("player"), command).Threshold);
        Assert.Equal(90000u, h.Engine.Snapshot.Corps.Single(c => c.Id == 1).Treasury.Money);
        var rejected = new Harness(content, reject: true);
        var original = rejected.Engine.Snapshot;
        Assert.Equal(1, rejected.Run(OriginalAiWork.Gift).Rejected);
        StateAssert.Equal(original, rejected.Engine.Snapshot);
    }

    [Fact]
    public void GiftAcceptsScoresAboveOneHundredAndKeepsTargetOuterOrder()
    {
        var content = Fixture();
        content = content with { Officers = content.Officers.Select(p => p.State.Id == 1 ? p with { State = p.State with { BaseAbilities = new(115, 115, 115, 115) } } : p).ToArray() };
        var h = new Harness(content, change: w => w with { Relations = [new(1, 2, 100), new(1, 3, 100), new(2, 3, 0)],
            Simulation = w.Simulation with { Alliance = new(3, 1, [1, 2], 10) } });
        Assert.Equal(1, h.Run(OriginalAiWork.Gift).Submitted);
        Assert.Equal(2, Assert.IsType<GiftCommand>(Assert.Single(h.Commands)).TargetForceId);
    }

    [Fact]
    public void ReturnPrisonerUsesSpecializedPoliticsAndRealRecoveryTask()
    {
        var content = Fixture();
        content = content with { Officers = [.. content.Officers, Person(30, 1, 2, 90, OfficerStatus.Captive)] };
        int draws = 0;
        var h = new Harness(content, draw: maximum => { draws++; return maximum - 1; });
        Assert.Equal(1, h.Run(OriginalAiWork.ReturnPrisoner).Submitted);
        Assert.Equal(0, draws); // No adviser uses actual public feasibility, not the unknown-opinion draw.
        Assert.Equal(30, Assert.IsType<ReturnPrisonerCommand>(Assert.Single(h.Commands)).PrisonerId);
    }

    [Fact]
    public void NoAdviserDoesNotTurnAnImpossibleMissionIntoCertainSuccess()
    {
        var content = Fixture();
        content = content with { Officers = content.Officers.Select(p => p.State.OwnerId == 1 && p.State.UnitId is null
            ? p with { State = p.State with { BaseAbilities = new(45, 45, 45, 45) } } : p).ToArray() };
        var h = new Harness(content, change: w => w with { Units = w.Units.Select(u => u.Id == 20 ? u with { Morale = 100 } : u).ToImmutableList(),
            Officers = w.Officers.Select(p => p.Id == 9 ? p with { BaseAbilities = new(115, 115, 115, 115) } : p).ToImmutableList() });
        var report = h.Run(OriginalAiWork.FalseReport, 3);
        Assert.Equal(3, report.Attempts);
        Assert.Equal(3, report.EmptyPlans);
        Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(12999u, false, true)]
    [InlineData(12999u, false, false)]
    [InlineData(13000u, false, true)]
    [InlineData(13000u, false, false)]
    [InlineData(14999u, false, true)]
    [InlineData(14999u, false, false)]
    [InlineData(15000u, true, true)]
    [InlineData(15000u, true, false)]
    public void ReliefCountsIncomingAttackBeforeKeepingGarrison(uint soldiers, bool succeeds, bool visible)
    {
        var h = new Harness(change: w => w with { Settlements = w.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = soldiers } : s).ToImmutableList(),
            Simulation = w.Simulation with { OtherForceOrdersVisible = visible } });
        Assert.Equal(succeeds ? 1 : 0, h.Run(OriginalAiWork.Reinforce).Submitted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReliefConsumesFailedTargetAndRetriesRemainingTargetWithinSameAttempt(bool secondTarget)
    {
        var content = Fixture();
        content = content with
        {
            Settlements = [content.Settlements[0] with { Soldiers = 14000 }, .. content.Settlements.Skip(1),
                content.Settlements[0] with { Id = 4, X = 0, NameKey = "relief-source" }],
            Officers = content.Officers.Select(p => p.State.Id == 12
                ? p with { State = p.State with { SettlementId = 4 } } : p).ToArray(),
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3, [4] = 1 },
            RegionNameKeys = new() { [1] = "one", [2] = "two", [3] = "three", [4] = "relief-source" },
            Military = content.Military! with { Places = content.Military.Places.Add(new(4, new(2, 7), new(1, 1))) }
        };
        var h = new Harness(content, change: w => secondTarget ? w with
        {
            Units = [.. w.Units, w.Units.Single(u => u.Id == 10) with { Id = 11, Soldiers = 6000,
                Position = new(22, 7), CommanderId = 18, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(18, BattleRank.Middle) }],
            Officers = [.. w.Officers, w.Officers.Single(p => p.Id == 8) with { Id = 18, UnitId = 11 }]
        } : w);
        var report = h.Run(OriginalAiWork.Reinforce);
        Assert.Equal(1, report.Attempts);
        Assert.Equal(secondTarget ? 1 : 0, report.Submitted);
        if (secondTarget)
        {
            var command = Assert.IsType<UnitStrategyCommand>(Assert.Single(h.Commands));
            Assert.Equal(11, command.TargetUnitId);
            Assert.Equal(12, command.OfficerId);
        }
        else Assert.Empty(h.Commands);
        Assert.Equal(14000u, h.Engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Theory]
    [InlineData(OriginalAiWork.FalseReport)]
    [InlineData(OriginalAiWork.Confuse)]
    public void HostileStrategyDecisionsIgnorePlayerOrderDisplay(OriginalAiWork work)
    {
        var visibleDraws = new List<int>();
        var hiddenDraws = new List<int>();
        Harness Create(bool visible, List<int> draws) => new(change: w => w with
        {
            Simulation = w.Simulation with { OtherForceOrdersVisible = visible },
            Units = w.Units.Select(u => u.Id == 10 && work == OriginalAiWork.Confuse ? u with
                { Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 20 } } : u).ToImmutableArray()
        }, draw: maximum => { draws.Add(maximum); return maximum - 1; });
        var visible = Create(true, visibleDraws);
        var hidden = Create(false, hiddenDraws);
        Assert.Equal(1, visible.Run(work).Submitted);
        Assert.Equal(1, hidden.Run(work).Submitted);
        Assert.Equal(visibleDraws, hiddenDraws);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(visible.Commands),
            System.Text.Json.JsonSerializer.Serialize(hidden.Commands));
        var world = hidden.Engine.Snapshot;
        StateAssert.Equal(visible.Engine.Snapshot, world with
            { Simulation = world.Simulation with { OtherForceOrdersVisible = true } });
    }

    [Fact]
    public void ReliefDecisionsAndRandomConsumptionIgnorePlayerOrderDisplay()
    {
        var visibleDraws = new List<int>();
        var hiddenDraws = new List<int>();
        Harness Create(bool visible, List<int> draws) => new(change: w => w with
        { Simulation = w.Simulation with { OtherForceOrdersVisible = visible } },
            draw: maximum => { draws.Add(maximum); return maximum - 1; });
        var visible = Create(true, visibleDraws);
        var hidden = Create(false, hiddenDraws);
        Assert.Equal(1, visible.Run(OriginalAiWork.Reinforce).Submitted);
        Assert.Equal(1, hidden.Run(OriginalAiWork.Reinforce).Submitted);
        Assert.Equal(visibleDraws, hiddenDraws);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(visible.Commands),
            System.Text.Json.JsonSerializer.Serialize(hidden.Commands));
        var world = hidden.Engine.Snapshot;
        StateAssert.Equal(visible.Engine.Snapshot, world with
            { Simulation = world.Simulation with { OtherForceOrdersVisible = true } });
    }

    [Fact]
    public void SelectedPairIsConsumedOnRejectionWithoutMutatingPublicState()
    {
        var h = new Harness(reject: true);
        var before = h.Engine.Snapshot;
        var report = h.Run(OriginalAiWork.Alienate, 5);
        Assert.Equal(2, report.Rejected); // Two non-ruler targets, each consumed even after refusal.
        Assert.Equal(2, h.Commands.Count);
        StateAssert.Equal(before, h.Engine.Snapshot);
    }

    [Fact]
    public void ReducedGroupSkipConsumesExactlyOnePlanningDraw()
    {
        var draws = new List<int>();
        var h = new Harness(draw: maximum => { draws.Add(maximum); return 0; });
        h.State = h.State with { Forces = h.State.Forces.Select(f => f.ForceId == 1 ? f with { Group = OriginalAiGroup.ReducedAttempts } : f).ToImmutableArray() };
        Assert.Equal(OriginalAiWorkEnd.ProbabilityFailed, h.Run(OriginalAiWork.Burn).End);
        Assert.Equal([100], draws);
        Assert.Empty(h.Commands);
    }
    [Fact]
    public void DelegatedProductionStrategySurvivesSaveAndRecordingWithoutRepeatingWork()
    {
        var content = Fixture();
        content = content with
        {
            Information = content.Information with { Corps = content.Information.Corps.Select(c => c.Id == 1
                ? c with { ControlMode = CorpsControlMode.Automatic } : c).ToArray() },
            OriginalAi = content.OriginalAi with
            {
                WorkRules = content.OriginalAi.WorkRules.ToImmutableDictionary(p => p.Key, p => p.Value with
                    { OpeningWaitDays = p.Key == OriginalAiWork.Encourage ? 0 : 10000 }),
                SecondaryBatches = content.OriginalAi.SecondaryBatches.ToImmutableDictionary(p => p.Key,
                    _ => ImmutableArray.Create(OriginalAiWork.Encourage))
            }
        };
        var session = GameSession.Start(content, 42);
        session.BeginRecording();
        var request = session.Request(new EndControlOperation());
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Contains(result.Events.OfType<TaskStartedEvent>(), e => e.Task.Journey?.UnitStrategy?.Strategy == UnitStrategyKind.Inspire);
        var after = session.Capture();
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        StateAssert.Equal(after, session.Capture());
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        for (int i = 0; i < 4; i++)
        {
            EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
        }
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), content,
            GameSession.EngineFactory(content));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Theory]
    [InlineData(OriginalAiWork.FalseReport, true, false, 20, 1)]
    [InlineData(OriginalAiWork.FalseReport, false, true, 20, 0)]
    [InlineData(OriginalAiWork.Confuse, true, false, 20, 0)]
    [InlineData(OriginalAiWork.Confuse, false, true, 20, 0)]
    [InlineData(OriginalAiWork.Confuse, false, false, 20, 1)]
    public void HostileStrategyUsesWorkSpecificFriendlyUnitScope(OriginalAiWork work, bool personnel,
        bool embedded, int target, int submitted)
    {
        var h = new Harness(change: w => w with { Units = w.Units.Select(u => u.Id == 10 ? u with
            { IsPersonnel = personnel, IsEmbedded = embedded,
                Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = target } } : u).ToImmutableList() });
        Assert.Equal(submitted, h.Run(work).Submitted);
    }

    [Fact]
    public void AttackRequestToPlayerDoesNotUseFreeAcceptanceThresholdAsCandidateGate()
    {
        var content = Fixture();
        content = content with { Information = content.Information with
            { Relations = [new(1, 2, 0), new(1, 3, 60), new(2, 3, 20)] } };
        var h = new Harness(content, w => w with { Simulation = w.Simulation with
            { Controls = w.Simulation.Controls.Select(c => c.ForceId == 3 ? c with { Kind = ControllerKind.Player } : c).ToImmutableArray() } });
        var preview = h.Engine.GetMissionDecision(new("player"), new RequestAttackCommand(Guid.NewGuid(), 1, 0, 11, 3, 2));
        Assert.Equal(CommandRejection.None, preview.Advice.Rejection);
        Assert.Equal(AdviserOpinion.Unavailable, preview.Advice.Opinion);
        Assert.Equal(0, preview.Threshold);
        Assert.Equal(1, h.Run(OriginalAiWork.RequestAttack).Submitted);
        Assert.Equal(3, Assert.IsType<RequestAttackCommand>(Assert.Single(h.Commands)).TargetForceId);
    }

    [Fact]
    public void AttackRequestUsesActualDistancePreferredEnvoyRatherThanPoliticalStandIn()
    {
        var content = Fixture();
        content = content with { Settlements = [content.Settlements[0] with { X = 0 }, .. content.Settlements.Skip(1),
            content.Settlements[0] with { Id = 4, X = 6, NameKey = "near" }],
            Officers = content.Officers.Select(p => p.State.Id == 12 ? p with { State = p.State with { SettlementId = 4 } } : p).ToArray(),
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3, [4] = 1 },
            RegionNameKeys = new() { [1] = "one", [2] = "two", [3] = "three", [4] = "near" },
            Military = content.Military! with { Places = content.Military.Places.SetItem(0, new(1, new(2, 7), new(1, 1))).Add(new(4, new(32, 7), new(1, 1))) } };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.RequestAttack).Submitted);
        var command = Assert.IsType<RequestAttackCommand>(Assert.Single(h.Commands));
        Assert.Equal(12, command.OfficerId);
        Assert.Equal(3, command.TargetForceId);
        Assert.Equal(2, command.TargetSettlementId);
    }

}
