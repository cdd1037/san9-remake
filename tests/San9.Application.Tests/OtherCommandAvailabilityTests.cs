using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class OtherCommandAvailabilityTests
{
    private static string? Reason(ScenarioInitialState definition, WorldSnapshot world, string command) =>
        MapCommandMenu.Settlement(new(definition, world), 1).SelectMany(g => g.Children).Single(c => c.Id == command).UnavailableReasonKey;
    private static void Check(ScenarioInitialState definition, WorldSnapshot world, string command, string? reason) =>
        Assert.Equal(reason is null ? null : "ui.command-reason." + reason, Reason(definition, world, command));
    private static WorldSnapshot Home(WorldSnapshot world, Func<SettlementState, SettlementState> change) =>
        world with { Settlements = world.Settlements.Select(s => s.Id == 1 ? change(s) : s).ToArray() };

    [Theory]
    [InlineData("sortie")]
    [InlineData("build")]
    [InlineData("transport")]
    [InlineData("move")]
    [InlineData("search")]
    [InlineData("employ")]
    [InlineData("alienate")]
    [InlineData("burn")]
    [InlineData("seize")]
    [InlineData("rumor")]
    [InlineData("false-report")]
    [InlineData("disorder")]
    [InlineData("inspire")]
    [InlineData("rescue")]
    [InlineData("gift")]
    [InlineData("request")]
    [InlineData("return")]
    [InlineData("surrender")]
    public void LocalCommandsExposeConfusionBeforeOtherFailures(string command)
    {
        var definition = G01ScenarioTests.Fixture();
        var session = GameSession.Start(definition, 7);
        var world = Home(session.Snapshot, s => s with { ConfusionDaysRemaining = 1, Soldiers = 0 });
        world = world with { Officers = world.Officers.Select(p => p with { Acted = true }).ToArray() };
        Check(definition, world, command, "confused");
        Assert.Equal(DeploymentDraft.UnavailableReasonKey(new(definition, world), 1), Reason(definition, world, "sortie"));
        Assert.Same(session.Snapshot, session.View.World);
    }

    [Fact]
    public void AppointmentsUseForceWideCandidatesAndDoNotRequireAnUnusedLocalActor()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 7).Snapshot;
        world = Home(world, s => s with { ConfusionDaysRemaining = 1 });
        world = world with { Officers = world.Officers.Select(p => p with { Acted = true }).ToArray() };
        Check(definition, world, "strategist", null);
        Check(definition, world, "reward", null);
        Check(definition, world, "execute", "no-prisoner");
        Check(definition, world, "bestow", "no-treasure");
        Check(definition, world, "confiscate", "no-treasure-holder");
    }

    [Fact]
    public void RewardSeparatesFundsLoyaltyAndAlreadyRewardedCandidates()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 7).Snapshot;
        world = world with { Corps = world.Corps.Select(c => c with { Treasury = new(99, 0) }).ToArray(),
            Officers = world.Officers.Select(p => p.OwnerId == 1 ? p.WithLoyalty(1, 100) : p).ToArray() };
        Check(definition, world, "reward", "reward-funds");
        world = world with { Corps = world.Corps.Select(c => c with { Treasury = new(100, 0) }).ToArray() };
        Check(definition, world, "reward", "loyalty-max");
        world = world with { Officers = world.Officers.Select(p => p.Id == 2 ? p.WithLoyalty(1, 99) with { RewardedThisXun = true } : p).ToArray() };
        Check(definition, world, "reward", "no-reward-candidate");
        world = world with { Officers = world.Officers.Select(p => p with { RewardedThisXun = false }).ToArray() };
        Check(definition, world, "reward", null);
    }

    [Fact]
    public void RecruitmentAndSurrenderExcludeProtectedAndBarbarianServingTargets()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 7).Snapshot;
        Check(definition, world, "employ", null);
        Check(definition, world, "surrender", null);
        world = world with { Simulation = world.Simulation with { AttackRequests = [new(2, 1, 99, 3, 3)] } };
        Check(definition, world, "employ", "no-employ-target");
        Check(definition, world, "surrender", "no-target-force");
        world = world with { Simulation = world.Simulation with { AttackRequests = [] },
            Forces = world.Forces.Select(f => f.Id == 2 ? f with { IsBarbarian = true } : f).ToArray() };
        Check(definition, world, "employ", "no-employ-target");
        Check(definition, world, "surrender", "no-target-force");
    }

    [Fact]
    public void MovementAndRecallHaveDifferentSourceAndDestinationRequirements()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 7).Snapshot;
        Check(definition, world, "move", "no-move-destination");
        Check(definition, world, "summon", "no-other-facility");
        world = world with { Settlements = world.Settlements.Select(s => s.Id == 2 ? s with { OwnerId = 1, CorpsId = 1, ConfusionDaysRemaining = 1 } : s).ToArray() };
        Check(definition, world, "move", null);
        Check(definition, world, "summon", "no-normal-facility");
        world = world with { Settlements = world.Settlements.Select(s => s with { ConfusionDaysRemaining = 0 }).ToArray() };
        Check(definition, world, "summon", "no-recall-candidate");
        world = world with { Officers = world.Officers.Select(p => p.Id == 2 ? p with { SettlementId = 2 } : p).ToArray() };
        Check(definition, world, "summon", null);
        world = world with { Officers = world.Officers.Select(p => p.Id == 2 ? p with { Status = OfficerStatus.Governor } : p).ToArray() };
        Check(definition, world, "summon", "no-recall-candidate");
    }

    [Fact]
    public void DiplomacyAndUnitStrategiesRequireTheirOwnTargets()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 7).Snapshot;
        Check(definition, world, "rumor", "no-rumor-forces");
        Check(definition, world, "return", "no-captured-officer");
        Check(definition, world, "false-report", "no-false-report-target");
        Check(definition, world, "disorder", "no-disorder-target");
        Check(definition, world, "inspire", "no-inspire-target");
        Check(definition, world, "rescue", "no-rescue-target");
        world = world with { Corps = world.Corps.Select(c => c with { Treasury = new(1000, 0) }).ToArray() };
        Check(definition, world, "gift", null);
        world = world with { Corps = world.Corps.Select(c => c with { Treasury = new(999, 9999) }).ToArray() };
        Check(definition, world, "gift", "no-gift-resources");
    }

    [Fact]
    public void StrategyTargetsUseDirectedCityReachAndFormationAndMoraleFilters()
    {
        var basis = G01ScenarioTests.Fixture();
        var definition = basis with {
            RegionCityIds = new() { [1] = 1, [2] = 2 }, CityConnections = [new(1, 2)],
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, null)],
                [new(1, 1, 50, 10000, 5, new(1, 1), TravelMode.Land) { ImmuneToDisorder = true }],
                [new(1, new(7, 7), new(1, 1)), new(2, new(12, 12), new(1, 1))]) {
                    RegionIds = Enumerable.Repeat<int?>(2, 16).ToImmutableArray()
                }
        };
        var initial = GameSession.Start(basis, 7).Snapshot;
        var world = initial with {
            Simulation = initial.Simulation with { NextUnitId = 82 },
            Units = [new(81, 2, new(12, 12), 1000) { FormationId = 1 }]
        };
        Check(definition, world, "false-report", null);
        Check(definition, world, "disorder", "no-disorder-target");
        Check(definition with { CityConnections = [new(2, 1)] }, world, "false-report", "no-false-report-target");
        world = world with { Units = [world.Units[0] with { OwnerId = 1, Morale = 100 }] };
        Check(definition, world, "inspire", "no-inspire-target");
        world = Home(world, s => s with { Soldiers = 2999 }) with {
            Units = [world.Units[0] with { Morale = 99 }]
        };
        Check(definition, world, "inspire", null);
        Check(definition, world, "rescue", "rescue-soldiers");
        Check(definition, Home(world, s => s with { Soldiers = 3000 }), "rescue", null);
    }

    [Theory]
    [InlineData(UnitCondition.Routed, "unit-routed")]
    [InlineData(UnitCondition.Confused, "unit-confused")]
    [InlineData(UnitCondition.NoFormation, "unit-no-formation")]
    [InlineData(UnitCondition.Disguised, "unit-false-report")]
    [InlineData(UnitCondition.Normal, null)]
    [InlineData(UnitCondition.HighSpirits, null)]
    public void UnitStateControlsBothCommandsWhileRepeatedOrdersRemainSupported(UnitCondition condition, string? reason)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 7).Snapshot with { Units = [new(81, 1, new(12, 12), 1000)
            { FormationId = 1, Condition = condition, CommandedThisXun = true }] };
        var hash = world;
        Assert.All(MapCommandMenu.Unit(new(definition, world), 81), entry =>
            Assert.Equal(reason is null ? null : "ui.command-reason." + reason, entry.UnavailableReasonKey));
        StateAssert.Equal(hash, world);
    }
}
