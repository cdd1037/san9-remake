using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class IntelligenceMartialChainTests
{
    private static readonly CommandContext Player = new("player");

    private static SimulationEngine Engine(ulong seed, TacticEffect effect = TacticEffect.Confusion,
        UnitCondition condition = UnitCondition.Normal, bool settlement = false, int injury = 0, int cooldown = 0,
        bool contact = false, uint targetSoldiers = 100000, Action<MilitaryRulebook>? capture = null, int targetX = 12)
    {
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        // Contact boundary cases keep positions fixed throughout the xun despite continued approach.
        var rules = new MilitaryRulebook(new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, contact ? 100 : 1, contact ? 100 : 1)],
            [new(100, 5, 0, 200000, 3, new(1, 1), TravelMode.Land)
                { Combat = combat, Attack = new(10, false), CaptureRanks = [BattleRank.Front, BattleRank.Middle, BattleRank.Rear],
                    Tactics = new(100, 0, 4, [0], []) },
             new(200, 5, 0, 200000, 1, new(1, 1), TravelMode.Land)
                { Combat = combat, CaptureRanks = [BattleRank.Middle] }],
            [new(1, new(2, 2), new(1, 1)) { Combat = combat }, new(2, new(17, 2), new(1, 1)) { Combat = combat }])
        {
            Tactics = [new(9000, 7, effect, effect == TacticEffect.Taunt ? 0 : 10, 30, 0, true, true, [100]),
                new(9100, 0, TacticEffect.Martial, 1, 30, 0, true, true, [100]) { ContactRange = contact ? 5 : null },
                new(9101, 0, TacticEffect.Martial, 2, 30, 0, true, true, [100]) { ContactRange = contact ? 5 : null },
                new(9200, 7, TacticEffect.Trap, 10, 30, 0, false, true, [100])],
            Elevations = [0, 0, 0, 0, 0, 0], TrapTerrains = [new(0, 9200, false)]
        });
        var officers = new OfficerState[]
        {
            new(11, 1, null, new(70, 200, 70, 70), false)
                { UnitId = 0, LearnedTactics = [9000], EquippedTacticId = 9000 },
            new(12, 1, null, new(70, 70, 70, 70), false)
                { UnitId = 0, LearnedTactics = [9100], EquippedTacticId = 9100, Injury = injury, TacticCooldown = cooldown },
            new(13, 1, null, new(70, 70, 70, 70), false)
                { UnitId = 0, LearnedTactics = [9101], EquippedTacticId = 9101, Injury = injury, TacticCooldown = cooldown },
            new(21, 2, settlement ? 2 : null, new(70, 0, 70, 70), false) { UnitId = settlement ? null : 1 }
        };
        List<UnitState> units = [new(0, 1, new(6, 2), 100000) { FormationId = 100, Morale = 100, ServicePlaceId = 1,
            MemberRanks = new Dictionary<int, BattleRank> { [11] = BattleRank.Front, [12] = BattleRank.Middle,
                [13] = BattleRank.Rear }.ToImmutableDictionary() }];
        if (!settlement) units.Add(new(1, 2, new(targetX, 2), targetSoldiers)
            { FormationId = 200, Morale = 100, ServicePlaceId = 2, Condition = condition,
                ConditionDaysRemaining = condition == UnitCondition.Normal ? 0 : 30,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle) });
        var state = WorldState.Create(new(190, 1, 1), seed,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
            [new(1, 1, 1000, 100, 100, 100, 0, 0, 100, 0, 100),
             new(2, 2, targetSoldiers, 100, 100, 100, 0, 0, 100, 0, 100)
                { ConfusionDaysRemaining = condition == UnitCondition.Confused ? 30 : 0 }],
            officers, units: units, controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 1000000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 1000000)) { IsRoot = true }]);
        var engine = new SimulationEngine(state, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        var order = settlement ? new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2) :
            new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 };
        Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0, order)).Submission.Accepted);
        engine.EndControl(Player);
        capture?.Invoke(rules);
        return engine;
    }

    [Theory]
    [InlineData(TacticEffect.Confusion, UnitCondition.Normal, true)]
    [InlineData(TacticEffect.Confusion, UnitCondition.Confused, false)]
    [InlineData(TacticEffect.Defection, UnitCondition.NoFormation, true)]
    [InlineData(TacticEffect.Defection, UnitCondition.Normal, true)]
    [InlineData(TacticEffect.TrapSelection, UnitCondition.Confused, true)]
    [InlineData(TacticEffect.TrapSelection, UnitCondition.Normal, true)]
    [InlineData(TacticEffect.Illusion, UnitCondition.Confused, true)]
    [InlineData(TacticEffect.Taunt, UnitCondition.Confused, false)]
    [InlineData(TacticEffect.Sorcery, UnitCondition.Confused, false)]
    public void ResultStateControlsChainAndOnlyOneNewPrimaryCanActivate(TacticEffect effect, UnitCondition condition, bool expected)
    {
        int chains = 0, failed = 0;
        HashSet<int> primaries = [];
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var events = Engine(seed, effect, condition).AdvanceXun().Events.OfType<TacticActivatedEvent>().ToArray();
            var intelligence = events.FirstOrDefault(e => e.TacticId is 9000 or 9200);
            if (intelligence is null) continue;
            var followups = events.Where(e => e.Clock == intelligence.Clock && e.TacticId is 9100 or 9101).ToArray();
            Assert.InRange(followups.Length, 0, 1);
            if (followups.Length == 0) { failed++; continue; }
            chains++;
            var chain = followups[0];
            Assert.DoesNotContain(11, chain.Participants.Select(p => p.OfficerId));
            Assert.Equal(2, chain.Participants.Length); // Same-category, different-name responders still link.
            Assert.All(chain.Participants, p => Assert.Equal(p.OfficerId == 12 ? 9100 : 9101, p.TacticId));
            Assert.Equal(chain.TacticId, chain.Participants[0].TacticId);
            primaries.Add(chain.Participants[0].OfficerId);
        }
        Assert.Equal(expected, chains > 0);
        if (expected) { Assert.Equal(2, primaries.Count); Assert.True(failed > 0); }
    }

    [Theory]
    [InlineData(false, 0, 30, false)]
    [InlineData(false, 7, 0, false)]
    [InlineData(false, 0, 0, true)]
    [InlineData(true, 0, 0, true)]
    public void IneligibleMembersCannotFollowUp(bool settlement, int injury, int cooldown, bool contact)
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var events = Engine(seed, settlement: settlement, injury: injury, cooldown: cooldown, contact: contact)
                .AdvanceXun().Events.OfType<TacticActivatedEvent>().ToArray();
            Assert.DoesNotContain(events, e => e.TacticId is 9100 or 9101);
        }
    }

    [Fact]
    public void OriginalContactExpansionAllowsGapBelowFiveButExcludesBoundaryContact()
    {
        bool chainInside = false;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            foreach (int x in new[] { 11, 12 })
            {
                var events = Engine(seed, contact: true, targetX: x).AdvanceXun().Events.OfType<TacticActivatedEvent>().ToArray();
                var intelligence = events.FirstOrDefault(e => e.TacticId == 9000);
                bool chained = intelligence is not null && events.Any(e => e.Clock == intelligence.Clock && e.TacticId is 9100 or 9101);
                if (x == 11) chainInside |= chained;
                else Assert.False(chained);
            }
        }
        Assert.True(chainInside);
        var source = new MapOccupancy(new(10, 10), new(1, 1));
        Assert.True(source.IntersectsExpanded(new(new(15, 15), new(1, 1)), 5));
        Assert.False(source.IntersectsExpanded(new(new(16, 15), new(1, 1)), 5));
        Assert.True(source.IntersectsExpanded(new(new(17, 10), new(5, 1)), 5));
    }

    [Fact]
    public void TrapDefeatStopsChainBeforeAnyCandidateRoll()
    {
        bool observed = false;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var engine = Engine(seed, TacticEffect.TrapSelection, UnitCondition.Confused, targetSoldiers: 100);
            var events = engine.AdvanceXun().Events.OfType<TacticActivatedEvent>().ToArray();
            if (!events.Any(e => e.TacticId == 9200)) continue;
            Assert.DoesNotContain(engine.Snapshot.Units, u => u.Id == 1);
            Assert.DoesNotContain(engine.CaptureStableState().Simulation.RandomStreams, s => s.Name == "military-tactic-chain-member");
            observed = true;
        }
        Assert.True(observed);
    }

    [Fact]
    public void ConfusedFacilityAllowsRangedFollowup()
    {
        bool observed = false;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var events = Engine(seed, settlement: true).AdvanceXun().Events.OfType<TacticActivatedEvent>().ToArray();
            var intelligence = events.FirstOrDefault(e => e.TacticId == 9000);
            if (intelligence is null) continue;
            observed |= events.Any(e => e.Clock == intelligence.Clock && e.TargetIsSettlement && e.TacticId is 9100 or 9101);
        }
        Assert.True(observed);
    }

    [Fact]
    public void OneSelectionAndOneAllocationRollEvenWhenChosenActorFails()
    {
        int checkedAttempts = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var engine = Engine(seed);
            var events = engine.AdvanceXun().Events.OfType<TacticActivatedEvent>().ToArray();
            if (!events.Any(e => e.TacticId == 9000)) continue;
            var streams = engine.CaptureStableState().Simulation.RandomStreams;
            var selected = streams.SingleOrDefault(s => s.Name == "military-tactic-chain-member");
            if (selected is null) continue;
            foreach (string name in new[] { "military-tactic-chain-member", "military-tactic-chain-allocation" })
            {
                var random = new StableRandom(StableRandom.DeriveSeed(seed, name));
                random.NextUInt64();
                Assert.Equal(random.State, streams.Single(s => s.Name == name).State);
            }
            checkedAttempts++;
        }
        Assert.True(checkedAttempts > 0);
    }

    [Fact]
    public void ChainCooldownsAndRandomStateSurviveStableRestore()
    {
        bool checkedChain = false;
        for (ulong seed = 1; seed <= 40 && !checkedChain; seed++)
        {
            MilitaryRulebook rules = null!;
            var engine = Engine(seed, capture: value => rules = value);
            var events = engine.AdvanceXun().Events.OfType<TacticActivatedEvent>().ToArray();
            var first = events.FirstOrDefault(e => e.TacticId == 9000);
            var chain = first is null ? null : events.FirstOrDefault(e => e.Clock == first.Clock && e.TacticId is 9100 or 9101);
            if (chain is null) continue;
            int elapsed = engine.Snapshot.Clock.Day - chain.Clock.Day;
            Assert.Equal(30 - elapsed, engine.Snapshot.Officers.Single(p => p.Id == 11).TacticCooldown);
            Assert.Equal(30 - elapsed, engine.Snapshot.Officers.Single(p => p.Id == chain.Participants[0].OfficerId).TacticCooldown);
            Assert.Equal(10 - elapsed, engine.Snapshot.Officers.Single(p => p.Id == chain.Participants[1].OfficerId).TacticCooldown);
            var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty,
                PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
            engine.EndControl(Player); restored.EndControl(Player);
            EventAssert.Equal(engine.AdvanceXun().Events, restored.AdvanceXun().Events);
            StateAssert.Equal(engine.Snapshot, restored.Snapshot);
            checkedChain = true;
        }
        Assert.True(checkedChain);
    }
}
