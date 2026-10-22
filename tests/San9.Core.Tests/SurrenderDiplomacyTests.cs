using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SurrenderDiplomacyTests
{
    [Fact]
    public void SurrenderReturnsForeignEnvoyBeforeCurrentDatesCommandsAcrossRestore()
    {
        var rules = new SimulationRules
        {
            Military = Rules().Military,
            Personnel = new([new(1, new(2, 2)), new(2, new(92, 2)),
                new(3, new(692, 2)), new(4, new(112, 2))])
        };
        var engine = new SimulationEngine(World(), rules);
        var surrenderId = Guid.NewGuid();
        Assert.True(engine.Execute(new("first"), new DemandSurrenderCommand(surrenderId, 1, 0, 12, 2)).Submission.Accepted);
        engine.EndControl(new("first"));
        engine.EndControl(new("second"));
        Assert.True(engine.Execute(new("third"), new GiftCommand(Guid.NewGuid(), 3, 1, 31, 2,
            new(GiftKind.Money, 1000, null))).Submission.Accepted);
        engine.EndControl(new("third"));
        engine.AdvanceXun();
        var restored = new SimulationEngine(WorldState.Restore(engine.Snapshot), rules);
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 2, 2, surrenderId, 0, true);
        var events = engine.Execute(new("second"), answer).Events;
        EventAssert.Equal(events, restored.Execute(new("second"), answer).Events);
        Assert.Single(events.OfType<ForceSurrenderedEvent>());
        var returning = Assert.Single(engine.Snapshot.Simulation.Tasks.Where(t => t.OfficerId == 31));
        Assert.False(returning.Journey!.ReturnLeg!.AfterDailyCommands);
        Assert.True(returning.Journey.ReturnLeg.DiplomaticEnvoy);
        Assert.Equal(10, returning.Journey.ReturnLeg.CountdownDays);
        Assert.Equal(new GameClock(190, 1, 20), returning.Due);
        restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
        List<DomainEvent> completion = [];
        for (int i = 0; i < 2; i++)
        {
            foreach (var current in new[] { engine, restored })
                foreach (var control in current.Snapshot.Simulation.Controls) current.EndControl(new(control.ControllerId));
            var next = engine.AdvanceXun().Events;
            EventAssert.Equal(next, restored.AdvanceXun().Events);
            completion.AddRange(next);
        }
        Assert.Equal(new GameClock(190, 1, 20), Assert.Single(completion.OfType<PersonnelReturnedEvent>(), e => e.OfficerId == 31).Clock);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }

    [Theory]
    [InlineData(true, 99)]
    [InlineData(false, 99)]
    [InlineData(true, 100)]
    [InlineData(true, 120)]
    public void SurrenderGrowsOnlySuccessfulEnvoyAndSkipsCappedAbility(bool accept, int politics)
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Officers[12] with
        { BaseAbilities = mutation.Officers[12].BaseAbilities with { Politics = politics } });
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("diplomacy-politics-growth", 52)] });
        world.Commit(mutation);
        var engine = new SimulationEngine(world, Rules());
        Assert.True(engine.Execute(new("first"), new DemandSurrenderCommand(Guid.NewGuid(), 1, 0, 12, 2)).Submission.Accepted);
        var before = engine.CaptureStableState();
        var restored = new SimulationEngine(WorldState.Restore(before), Rules());
        var responseId = Guid.NewGuid();

        IReadOnlyList<DomainEvent> Resolve(SimulationEngine current)
        {
            current.EndControl(new("first")); current.EndControl(new("second")); current.EndControl(new("third"));
            current.AdvanceXun();
            var task = Assert.Single(current.Snapshot.Simulation.Tasks);
            var result = current.Execute(new("second"), new RespondDailyTaskCommand(responseId, 2, 1, task.Id, 0, accept));
            Assert.True(result.Submission.Accepted);
            return result.Events;
        }

        var events = Resolve(engine);
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(Resolve(restored)));
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        bool grows = accept && politics < 100;
        var growth = events.OfType<OfficerAbilityGrownEvent>().ToArray();
        if (grows)
            Assert.Equal(new OfficerAbilityGrownEvent(growth.Single().Clock, 12, "politics", 99, 100) { Occurrence = growth.Single().Occurrence }, Assert.Single(growth));
        else Assert.Empty(growth);
        Assert.Equal(grows ? 100 : politics, engine.Snapshot.Officers.Single(p => p.Id == 12).BaseAbilities.Politics);
        var random = new StableRandom(52);
        random.NextUInt64();
        Assert.Equal(grows ? random.State : 52UL,
            engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "diplomacy-politics-growth").State);
    }

    private static SimulationRules Rules() => new()
    {
        Personnel = new([new(1, new(2, 2)), new(2, new(92, 2)), new(3, new(182, 2)), new(4, new(112, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 2 }, [new(1, 2), new(2, 1), new(2, 3), new(3, 2)]),
        Military = new(new(200, 3, Enumerable.Repeat(0, 600).ToImmutableArray(), [new(0, 1, null)],
            [new(9, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(92, 2), new(1, 1)), new(3, new(182, 2), new(1, 1)), new(4, new(112, 2), new(1, 1))])
            { RegionIds = Enumerable.Range(0, 600).Select(i => (int?)(i % 200 < 18 ? 1 : i % 200 < 36 ? 2 : 3)).ToImmutableArray(),
              TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 600).ToImmutableArray() })
    };
    private static OfficerState Person(int id, int force, int? home, OfficerStatus status = OfficerStatus.Serving, int loyalty = 255) =>
        new(id, force, home, new(50, 50, 100, 50), false) { Status = status, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(force, loyalty) };
    private static WorldState World(bool units = false) => WorldState.Create(new(190, 1, 1), 42,
        [new ForceState(1, 900) { LeaderId = 11, BaseTrust = 900 },
         new ForceState(2, 100) { LeaderId = 21, BaseTrust = 100 },
         new ForceState(3, 100) { LeaderId = 31 }],
        [new(1, 1, 200000, 100, 100, 70, 0, 0, 0, 0, 0) { CorpsId = 10 },
         new(2, 2, 10000, 100, 100, 70, 0, 0, 0, 0, 0) { CorpsId = 20 },
         new(3, 3, 10000, 100, 100, 70, 0, 0, 0, 0, 0),
         new(4, 2, 0, 100, 100, 70, 0, 0, 0, 0, 0) { CorpsId = 21 }],
        [Person(11, 1, 1, OfficerStatus.Ruler), Person(12, 1, 1), Person(13, 1, 2, OfficerStatus.Captive),
         Person(21, 2, 2, OfficerStatus.Ruler), Person(22, 2, units ? null : 2) with { UnitId = units ? 50 : null },
         Person(23, 2, units ? null : 2, loyalty: 0) with { UnitId = units ? 50 : null },
         Person(24, 2, 4, OfficerStatus.Governor), Person(25, 2, 3, OfficerStatus.Captive), Person(31, 3, 3, OfficerStatus.Ruler),
         Person(33, 3, units ? null : 2, OfficerStatus.Captive) with { UnitId = units ? 50 : null }],
        units: units ? [new UnitState(50, 2, new(102, 2), 1000) { FormationId = 9, Morale = 70, CorpsId = 20,
            ServicePlaceId = 2, CommanderId = 22, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Front).Add(23, BattleRank.Rear) }] : [],
        corps: [new(10, 1, 11, new(10000, 10000)) { IsRoot = true }, new(20, 2, 21, new(2000, 4000)) { IsRoot = true }, new(21, 2, 24, new(1000, 2000)), new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }],
        relations: [new(1, 2, 80), new(1, 3, 0), new(2, 3, 0)],
        controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player), new("third", 3, ControllerKind.Automatic)]);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AcceptanceAtomicallyMergesCorpsAssetsPeopleAndDetentionAndRestoresExactly(bool units, bool emptyUnit)
    {
        var world = World(units);
        if (emptyUnit)
        {
            var mutation = world.BeginMutation();
            mutation.Replace(mutation.Officers[22].WithLoyalty(2, 0));
            world.Commit(mutation);
        }
        var engine = new SimulationEngine(world, Rules());
        Assert.True(engine.Execute(new("first"), new DemandSurrenderCommand(Guid.NewGuid(), 1, 0, 12, 2)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("second")); engine.EndControl(new("third"));
        engine.AdvanceXun();
        var task = Assert.Single(engine.Snapshot.Simulation.Tasks);
        var saved = engine.Snapshot;
        var restored = new SimulationEngine(WorldState.Restore(saved), Rules());
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, task.Id, 0, true);
        var result = engine.Execute(new("second"), response);
        Assert.True(result.Submission.Accepted);
        EventAssert.Equal(result.Events, restored.Execute(new("second"), response).Events);
        var surrendered = Assert.Single(result.Events.OfType<ForceSurrenderedEvent>());
        long funds = saved.Corps.Where(c => c.OwnerId == 2).Sum(c => (long)(c.Treasury?.Money ?? 0));
        Assert.Equal(funds, surrendered.MoneyRemoved);
        Assert.Equal((uint)(funds / 2), surrendered.MoneyReceived);
        Assert.Contains(21, surrendered.JoinedOfficerIds);
        Assert.Contains(23, surrendered.DepartedOfficerIds);
        Assert.True(engine.Snapshot.Forces.Single(f => f.Id == 2).IsEliminated);
        Assert.All(engine.Snapshot.Officers, p => Assert.False(p.ForceLoyalties.ContainsKey(2)));
        Assert.DoesNotContain(engine.Snapshot.Corps, c => c.OwnerId == 2);
        Assert.DoesNotContain(engine.Snapshot.Officers, p => p.OwnerId == 2);
        Assert.All(engine.Snapshot.Settlements.Where(s => s.Id is 2 or 4), s => Assert.Equal(1, s.OwnerId));
        Assert.Equal(OfficerStatus.Serving, engine.Snapshot.Officers.Single(p => p.Id == 13).Status);
        Assert.Null(engine.Snapshot.Officers.Single(p => p.Id == 25).OwnerId);
        Assert.Equal(OfficerStatus.Captive, engine.Snapshot.Officers.Single(p => p.Id == 25).Status);
        if (emptyUnit)
        {
            Assert.Empty(engine.Snapshot.Units);
            Assert.Contains(result.Events, e => e is UnitTerminatedEvent terminated && terminated.UnitId == 50);
            Assert.Equal(OfficerStatus.Serving, engine.Snapshot.Officers.Single(p => p.Id == 33).Status);
        }
        else if (units)
        {
            var unit = Assert.Single(engine.Snapshot.Units);
            Assert.Equal(1, unit.OwnerId);
            Assert.Equal(22, unit.CommanderId);
            Assert.Equal(50, engine.Snapshot.Officers.Single(p => p.Id == 33).UnitId);
        }
        // The first player force survives; surrender of the second player does not end the whole session.
        Assert.Null(engine.Snapshot.Simulation.TerminalReason);
        Assert.Contains(engine.Snapshot.Simulation.Controls, c => c.ForceId == 1 && c.Kind == ControllerKind.Player);
        Assert.Equal(SimulationBoundary.Stable, engine.Snapshot.Simulation.Boundary);
        _ = new SimulationEngine(WorldState.Restore(engine.Snapshot), Rules());
    }

    [Fact]
    public void RefusalLowersRelationWithoutMergingOrConsumingPersonnelDraws()
    {
        var engine = new SimulationEngine(World(), Rules());
        Assert.True(engine.Execute(new("first"), new DemandSurrenderCommand(Guid.NewGuid(), 1, 0, 12, 2)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("second")); engine.EndControl(new("third")); engine.AdvanceXun();
        var task = Assert.Single(engine.Snapshot.Simulation.Tasks);
        var result = engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, task.Id, 0, false));
        Assert.False(Assert.Single(result.Events.OfType<SurrenderResolvedEvent>()).Accepted);
        Assert.Equal(39, engine.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 2).Value);
        Assert.DoesNotContain(result.Events, e => e is ForceSurrenderedEvent or ForceEliminatedEvent);
    }

    [Fact]
    public void AutomaticAcceptanceMergesAndContinuesTheCurrentXunWithOtherForcesAlive()
    {
        var world = World();
        var state = world.BeginMutation();
        state.Replace(state.Officers[12] with { BaseAbilities = new(50, 50, 500, 50) });
        state.SetSimulation(state.Simulation with { Controls = state.Simulation.Controls.Select(c => c.ForceId == 2
            ? c with { Kind = ControllerKind.Automatic } : c).ToImmutableArray() });
        world.Commit(state);
        var engine = new SimulationEngine(world, Rules());
        Assert.True(engine.Execute(new("first"), new DemandSurrenderCommand(Guid.NewGuid(), 1, 0, 12, 2)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("second")); engine.EndControl(new("third"));
        var result = engine.AdvanceXun();
        Assert.True(Assert.Single(result.Events.OfType<SurrenderResolvedEvent>()).Accepted);
        Assert.Null(engine.Snapshot.Simulation.TerminalReason);
        Assert.Null(engine.Snapshot.Simulation.PendingDailyTask);
        Assert.Single(result.Events.OfType<XunAdvancedEvent>());
        Assert.Equal(new GameClock(190, 1, 11), engine.Snapshot.Clock);
        Assert.DoesNotContain(engine.Snapshot.Simulation.Controls, c => c.ForceId == 2);
        _ = new SimulationEngine(WorldState.Restore(engine.Snapshot), Rules());
    }

    [Fact]
    public void MergeSettlesOutgoingGiftBeforeSplittingTreasuryAndClearsAllianceAndRequests()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { Alliance = new(1, 2, [2, 3], 20), AttackRequests = [new(2, 3, 1, 1, 19)] });
        world.Commit(mutation);
        var engine = new SimulationEngine(world, Rules());
        Assert.True(engine.Execute(new("first"), new DemandSurrenderCommand(Guid.NewGuid(), 1, 0, 12, 2)).Submission.Accepted);
        engine.EndControl(new("first"));
        var giftId = Guid.NewGuid();
        Assert.True(engine.Execute(new("second"), new GiftCommand(giftId, 2, 1, 22, 3, new(GiftKind.Money, 1000, null))).Submission.Accepted);
        engine.EndControl(new("second")); engine.EndControl(new("third")); engine.AdvanceXun();
        var task = engine.Snapshot.Simulation.Tasks.Single(t => t.Surrender is not null);
        long funds = engine.Snapshot.Corps.Where(c => c.OwnerId == 2)
            .Sum(c => (long)(c.Treasury?.Money ?? 0));
        var result = engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 2, task.Id, 0, true));
        Assert.Contains(result.Events, e => e is TaskCancelledEvent cancelled && cancelled.TaskId == giftId);
        Assert.DoesNotContain(result.Events, e => e is GiftResolvedEvent);
        Assert.Equal(funds + 1000, Assert.Single(result.Events.OfType<ForceSurrenderedEvent>()).MoneyRemoved);
        Assert.Empty(engine.Snapshot.Simulation.AttackRequests);
        Assert.Null(engine.Snapshot.Simulation.Alliance);
        Assert.DoesNotContain(result.Events.OfType<AttackRequestEndedEvent>(), e => e.Reason == AttackRequestEndReason.Broken);
    }

    [Fact]
    public void NewAttackAgreementInvalidatesWaitingSurrenderWithoutRefusalOrMerge()
    {
        var engine = new SimulationEngine(World(), Rules());
        Assert.True(engine.Execute(new("first"), new DemandSurrenderCommand(Guid.NewGuid(), 1, 0, 12, 2)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("second")); engine.EndControl(new("third")); engine.AdvanceXun();
        var task = Assert.Single(engine.Snapshot.Simulation.Tasks);
        var snapshot = engine.Snapshot;
        engine = new(WorldState.Restore(snapshot with { Simulation = snapshot.Simulation with
            { AttackRequests = [new(2, 1, 3, 3, 19)] } }), Rules());
        var result = engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, task.Id, 0, true));
        Assert.Contains(result.Events, e => e is TaskCancelledEvent cancelled && cancelled.TaskId == task.Id);
        Assert.DoesNotContain(result.Events, e => e is SurrenderResolvedEvent or ForceSurrenderedEvent);
        Assert.Equal(80, engine.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 2).Value);
    }

    [Fact]
    public void ThresholdUsesWoundedCurrentTrustCityCountAmbitionAndSemanticRulerExclusion()
    {
        var state = World().BeginMutation();
        var rules = Rules();
        Assert.True(SurrenderDiplomacy.Neighbors(state, 1, 2, rules));
        Assert.True(SurrenderDiplomacy.Threshold(state, 1, 12, 2, rules) > 0);
        state.Replace(state.Officers[21] with { AutomaticSurrenderAllowed = false });
        Assert.Equal(0, SurrenderDiplomacy.Threshold(state, 1, 12, 2, rules));
        state.Replace(state.Officers[21] with { AutomaticSurrenderAllowed = true, Ambition = 15 });
        Assert.True(SurrenderDiplomacy.Threshold(state, 1, 12, 2, rules) < 0);
        state.Replace(state.Settlements[2] with { Wounded = 50000 });
        Assert.Equal(0, SurrenderDiplomacy.Threshold(state, 1, 12, 2, rules));
    }
}
