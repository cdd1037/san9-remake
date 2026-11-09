using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class DeploymentDraftTests
{
    [Fact]
    public void UnitAttackAndMarchSplitCandidatesAndPreserveExistingIntentions()
    {
        var policies = new UnitPolicies(true, false, true, false);
        var view = View(world => world with
        {
            Simulation = world.Simulation with { NextUnitId = 8 },
            Settlements = world.Settlements.Select(s => s.Id == 2 ? s with { OwnerId = 1,
                CorpsId = world.Corps.Single(c => c.OwnerId == 1 && c.IsRoot).Id } : s).ToArray(),
            Officers = world.Officers.Select(o => o.OwnerId == 2 ? o with { SettlementId = null, UnitId = 2 } : o).ToArray(),
            Units = [new(7, 1, MapCoordinates.Center(new(0, 0)), 1200)
                { FormationId = 900, NavalFormationId = 700, Policies = policies,
                    Order = new(San9.Core.Domain.MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 },
                    Waypoint = new(8, 2) },
                new(2, 2, MapCoordinates.Center(new(3, 0)), 100) { FormationId = 900 },
                new(3, 1, MapCoordinates.Center(new(0, 3)), 100) { FormationId = 900 }]
        });
        var hash = view.World;
        var attack = new UnitCommandTargets(view, 7, UnitCommandKind.Attack);
        Assert.Equal(new MilitaryTarget(2, true), attack.Selected);
        Assert.Equal(new MapCell(1, 0), attack.Waypoint);
        Assert.Equal(policies, attack.Policies);
        Assert.Single(attack.Candidates);
        Assert.DoesNotContain(new MilitaryTarget(2, false), attack.Candidates.Keys);
        Assert.Equal(new MapCell(0, 0), attack.Route[0]);
        Assert.True(attack.BeginPolicies()); Assert.False(attack.SetDepartureDelay(9));
        attack.SetPolicies(policies with { AutonomousRetreat = true });
        var command = attack.CreateCommand(Guid.NewGuid(), 0, true);
        Assert.Equal(7, command.UnitId); Assert.Equal(2, command.Order.UnitId);
        Assert.Equal(new WorldPosition(8, 2), command.Waypoint);
        Assert.True(command.Policies!.AutonomousRetreat); Assert.True(command.ConfirmAttack);
        Assert.Equal(San9.Core.Simulation.ArrivalEstimateStatus.Known, attack.EstimateArrival().Status);
        var march = new UnitCommandTargets(view, 7, UnitCommandKind.March);
        Assert.Equal(2, march.Candidates.Count);
        Assert.All(march.Candidates, entry => { Assert.False(entry.Key.IsUnit); Assert.Equal(MilitaryMarker.Enter, entry.Value); });
        Assert.True(march.Select(new(2, false))); Assert.True(march.BeginPolicies());
        Assert.Null(march.Waypoint);
        Assert.NotEqual(attack.Route.ToArray(), march.Route.ToArray());
        var marchCommand = march.CreateCommand(Guid.NewGuid(), 0);
        Assert.Equal(MilitaryOrderKind.Return, marchCommand.Order.Kind);
        Assert.Null(marchCommand.Waypoint);
        StateAssert.Equal(hash, view.World);
    }

    [Theory]
    [InlineData(UnitCommandKind.Attack, UnitCommandKind.March)]
    [InlineData(UnitCommandKind.March, UnitCommandKind.Attack)]
    public void SwitchingCommandKindPreviewsExistingRouteWithoutAuthorizingOldTarget(UnitCommandKind originalKind, UnitCommandKind nextKind)
    {
        var originalTarget = originalKind == UnitCommandKind.March ? new MilitaryTarget(1, false) : new MilitaryTarget(2, true);
        var nextTarget = nextKind == UnitCommandKind.March ? new MilitaryTarget(1, false) : new MilitaryTarget(2, true);
        var originalOrder = originalKind == UnitCommandKind.March ? new MilitaryOrder(MilitaryOrderKind.Return, null, 1)
            : new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 };
        var view = View(world => world with
        {
            Simulation = world.Simulation with { NextUnitId = 8 },
            Units = [new(7, 1, MapCoordinates.Center(new(0, 0)), 1200)
                { FormationId = 900, NavalFormationId = 700,
                    Order = originalOrder, Waypoint = new(8, 2) },
                new(2, 2, MapCoordinates.Center(new(3, 0)), 100) { FormationId = 900 }]
        });
        var hash = view.World;
        var original = new UnitCommandTargets(view, 7, originalKind);
        Assert.Equal(originalTarget, original.Selected);
        Assert.NotEmpty(original.Route);
        var next = new UnitCommandTargets(view, 7, nextKind);
        Assert.Null(next.Selected);
        Assert.Equal(original.Route.ToArray(), next.Route.ToArray());
        Assert.Equal(original.Waypoint, next.Waypoint);
        Assert.False(next.Reachable);
        Assert.False(next.Select(originalTarget));
        Assert.False(next.SetWaypoint(new(2, 0)));
        Assert.False(next.BeginPolicies());
        Assert.Throws<InvalidOperationException>(() => next.CreateCommand(Guid.NewGuid(), 0));
        Assert.True(next.Select(nextTarget));
        Assert.Null(next.Waypoint);
        Assert.NotEqual(original.Route.ToArray(), next.Route.ToArray());
        Assert.True(next.BeginPolicies());
        var command = next.CreateCommand(Guid.NewGuid(), 0);
        Assert.Equal(nextKind == UnitCommandKind.March ? MilitaryOrderKind.Return : MilitaryOrderKind.AttackUnit, command.Order.Kind);
        Assert.Equal(nextTarget.Id, nextTarget.IsUnit ? command.Order.UnitId : command.Order.SettlementId);
        Assert.Null(command.Waypoint);
        StateAssert.Equal(hash, view.World);
    }

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 2)]
    public void UnitDraftRejectsUnavailableSources(bool embedded, bool personnel, int owner)
    {
        var view = View(world => world with { Units = [new(7, owner, MapCoordinates.Center(new(0, 0)), 100)
            { FormationId = 900, IsEmbedded = embedded, IsPersonnel = personnel }] });
        Assert.Throws<ArgumentException>(() => new UnitCommandTargets(view, 7, UnitCommandKind.Attack));
    }

    internal static StrategySnapshot View(Func<San9.Core.Simulation.WorldSnapshot, San9.Core.Simulation.WorldSnapshot>? change = null, bool work = false,
        Func<ScenarioInitialState, ScenarioInitialState>? configure = null, CompanionSettings? companion = null)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var slots = new FormationPresentation(3, 3, [new(1, 0), new(0, 1), new(1, 1), new(2, 1), new(1, 2)], 3, 180, null, null);
        var line = slots with { Slots = [new(0, 0), new(0, 1), new(1, 1), new(1, 2), new(2, 2)] };
        var formation = new FormationRule(100, 50, 0, 100000, 5, new(1, 1), TravelMode.Land)
            { Combat = new(10, 10, 10, 10, 50), Attack = new(1, false),
                Positions = slots.Slots.Select(s => new FormationPosition(s.Column, (BattleRank)s.Row)).ToImmutableArray() };
        definition = definition with
        {
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, 1)],
                [formation,formation with { Id=900,MovementBudget=100, Positions = line.Slots.Select(s => new FormationPosition(s.Column, (BattleRank)s.Row)).ToImmutableArray() },formation with { Id=200,RequiredTacticId=5000,DeploymentCost=100 },
                 formation with { Id=700,TravelMode=TravelMode.Water }],
                [new(1, MapCoordinates.Center(new(1, 1)), new(1, 1)) { FacilityTypeId = work ? 9001 : null },
                 new(2, MapCoordinates.Center(new(2, 2)), new(1, 1))
                    { FacilityTypeId = work ? 9000 : null }])
            {
                Facilities = work ? [
                    new(9001, 0, 100, new(1, 1), 0, false, false, true, 100000, null, null, null, []),
                    new(9000, 0, 100, new(1, 1), 0, false, false, false, 100000, null, null, null, [])
                        { IsWork = true, Combat = new(10, 10, 10, 10, 50) }] : [],
                DefaultDeploymentFormationId = 900,
                BasicNavalFormationId = 700,
                Tactics = [new(5000, 5, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [200]), new(6000, 0, TacticEffect.Martial, 10, 1, 0, true, false, [100, 900])]
            },
            Information = definition.Information with
            {
                Formations = [new(100,"fish") { Presentation=slots },new(900,"snake") { Presentation=line },
                new(200,"siege") { Presentation=slots },new(700,"ship") { Presentation=slots }],
                Tactics = [new(5000, "technology"), new(6000, "strike")],
                Ranks = [new(60, "rank", 45000, 0, 0, 0)],
                Offices = [new(77, "office", 35000, 0)]
            }
        };
        world = world with
        {
            Settlements = world.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 32000 } : s).ToArray(),
            Forces = world.Forces.Select(f => f.Id == 1 ? f with { RankId = 60 } : f).ToArray(),
            Officers = world.Officers.Select(o => o.Id switch
            {
                1 => o with { Status = OfficerStatus.Ruler, LearnedTactics = [6000], EquippedTacticId = 6000 },
                2 => o with { LearnedTactics = [5000], OfficeId = 77 },
                _ => o
            }).ToArray()
        };
        definition = configure is null ? definition : configure(definition);
        return new(new StrategySnapshotSource(definition, new ScenarioRules(definition)), change is null ? world : change(world), companion);
    }
    [Fact]
    public void PreparationKeepsWorldUntouchedAndUsesCommanderAndCurrentMembers()
    {
        var view = View(); var hash = view.World; var draft = new DeploymentDraft(view, 1);
        Assert.Empty(draft.Members); Assert.False(draft.CanProceed);
        Assert.True(draft.SetMembers([3, 2, 1])); Assert.Equal(1, draft.CommanderId);
        Assert.Equal(900, draft.FormationId); Assert.Equal(700, draft.NavalFormationId); Assert.Equal(32000u, draft.Soldiers);
        Assert.True(draft.SetSoldiers(123)); Assert.True(draft.SetFormation(200, false)); Assert.Equal(100, draft.Cost);
        Assert.True(draft.SetTactic(1, null)); Assert.False(draft.SetTactic(1, 5000));
        // Removing the technology holder must invalidate the siege formation; confirmation resets troops.
        Assert.True(draft.SetMembers([1, 3])); Assert.Equal(900, draft.FormationId); Assert.Equal(32000u, draft.Soldiers);
        Assert.DoesNotContain(draft.Formations(false), f => f.Id == 200);
        Assert.True(draft.SetMembers([3])); Assert.Equal(10000u, draft.MaximumSoldiers);
        Assert.False(draft.SetSoldiers(0)); Assert.False(draft.SetSoldiers(10001)); Assert.Equal(10000u, draft.Soldiers);
        StateAssert.Equal(hash, view.World); Assert.Empty(view.World.Units);
    }
    [Fact]
    public void FormationChangeResetsPositionsAndRosterConfirmationRetainsValidPositions()
    {
        var draft = new DeploymentDraft(View(), 1); Assert.True(draft.SetMembers([1, 2]));
        var first = draft.Positions[1]; var second = draft.Positions[2];
        Assert.True(draft.Move(1, second)); Assert.Equal(first, draft.Positions[2]);
        Assert.True(draft.SetMembers([1, 2, 3])); Assert.Equal(second, draft.Positions[1]);
        Assert.False(draft.Move(1, new(100, 100)));
        Assert.True(draft.SetFormation(100, false)); Assert.Equal(new FormationSlot(1, 0), draft.Positions[1]);
        Assert.False(draft.SetMembers([1, 1])); Assert.False(draft.SetMembers([4])); Assert.False(draft.SetMembers([]));
        Assert.Equal(3, draft.Members.Length);
    }
    [Fact]
    public void MultiSelectionHonorsLimitAcrossSortedPagesAndCancellationIsIsolated()
    {
        var draft = new DeploymentDraft(View(), 1); draft.SetMembers([1]);
        var selection = new CatalogSelection(Enumerable.Range(1, 9), draft.Members, 5);
        selection.SelectAll(Enumerable.Range(2, 8).Reverse());
        Assert.Equal(new[] { 1, 6, 7, 8, 9 }, selection.Selected.Order()); Assert.False(selection.Toggle(2));
        selection.Toggle(9); Assert.True(selection.Toggle(2)); selection.Clear();
        Assert.Empty(selection.Selected); Assert.Equal(new[] { 1 }, draft.Members);
    }
    [Fact]
    public void NumberEditingRejectsEmptyAndClampsWithoutOverflow()
    {
        var number = new BoundedNumberInput(1, uint.MaxValue, 32000);
        number.Clear(); Assert.False(number.CanConfirm);
        number.Append("99999999999999999999"); Assert.Equal(uint.MaxValue, number.Value);
        number.Backspace(); Assert.Equal(429496729u, number.Value);
        number.Set(1); number.Step(-1); Assert.Equal(1u, number.Value);
        number.Clear(); number.Append("1200"); Assert.True(number.CanConfirm); Assert.Equal(1200u, number.Value);
    }

    [Fact]
    public void TargetsSeparateObjectIdentitiesAndLeavePreparationAndWorldUntouched()
    {
        var view = View(world => world with { Units = [
            new(2, 2, MapCoordinates.Center(new(3, 0)), 100) { FormationId = 900 },
            new(3, 1, MapCoordinates.Center(new(0, 0)), 100) { FormationId = 900, CorpsId = world.Corps.Single(c => c.OwnerId == 1 && c.IsRoot).Id },
            new(4, 2, MapCoordinates.Center(new(3, 1)), 100) { FormationId = 900, IsEmbedded = true },
            new(5, 2, MapCoordinates.Center(new(3, 2)), 100) { IsPersonnel = true }] });
        var draft = new DeploymentDraft(view, 1); draft.SetMembers([1]); draft.SetSoldiers(123);
        var hash = view.World;
        var targets = new DeploymentTargets(draft);
        Assert.Equal(2, targets.Candidates.Count);
        Assert.Equal(MilitaryMarker.Attack, targets.Candidates[new(2, false)]);
        Assert.Equal(MilitaryMarker.Attack, targets.Candidates[new(2, true)]);
        Assert.False(targets.Select(new(1, false)));
        Assert.True(targets.Select(new(2, true)));
        Assert.Equal(new MilitaryTarget(2, true), targets.Selected);
        Assert.Contains(new MapCell(3, 0), targets.ClearCells);
        Assert.DoesNotContain(new MapCell(0, 0), targets.ClearCells);
        StateAssert.Equal(hash, view.World); Assert.Equal(123u, draft.Soldiers);
        Assert.Equal(new[] { 1 }, draft.Members);
    }

    [Fact]
    public void TargetCatalogProjectsCurrentValuesAndKeepsMixedIdentitiesThroughSorting()
    {
        var view = View(world => world with
        {
            Settlements = world.Settlements.Select(s => s.Id == 2 ? s with
                { OwnerId = null, Soldiers = 9, Wounded = 3, Morale = 0, ConfusionDaysRemaining = 2 } : s).ToArray(),
            Units = [new(2, 2, MapCoordinates.Center(new(3, 0)), 100)
                { FormationId = 900, CommanderId = 3, Wounded = 6, Morale = 73, Condition = UnitCondition.Confused }]
        });
        var keys = view.Settlements.Select(s => s.NameKey).Concat(view.Forces.Select(f => f.NameKey))
            .Concat(view.Officers.Select(o => o.NameKey)).Distinct();
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion,
            keys.ToDictionary(k => k, k => k));
        var targets = new DeploymentTargets(new(view, 1));
        var hash = view.World;
        var catalog = new MilitaryTargetCatalog(targets, texts);
        var place = Assert.Single(catalog.Rows, r => !catalog.Target(r.Id).IsUnit);
        var unit = Assert.Single(catalog.Rows, r => catalog.Target(r.Id).IsUnit);
        Assert.Equal(new MilitaryTarget(2, false), catalog.Target(place.Id));
        Assert.Equal(new MilitaryTarget(2, true), catalog.Target(unit.Id));
        Assert.Equal([view.Settlements.Single(s => s.Id == 2).ResolveName(texts), "", "9", "3", "0", "混乱"],
            place.Cells.Select(c => c.Text));
        Assert.Equal([texts.Resolve(view.Officers.Single(o => o.Id == 3).NameKey) + "队",
            texts.Resolve(view.Forces.Single(f => f.Id == 2).NameKey), "100", "6", "73", "混乱"],
            unit.Cells.Select(c => c.Text));
        var table = new CatalogTableState(); table.SetRows(catalog.Rows); table.Sort(2);
        Assert.Equal(new MilitaryTarget(2, true), catalog.Target(table.Rows[0].Id));
        table.Sort(2);
        Assert.Equal(new MilitaryTarget(2, false), catalog.Target(table.Rows[0].Id));
        Assert.Null(targets.Selected); StateAssert.Equal(hash, view.World);
    }

    [Fact]
    public void FriendlyOrdinaryFacilityUsesEntryMarkerAndHostileFacilityUsesAttack()
    {
        var view = View(world => world with { Settlements = world.Settlements.Select(s => s.Id == 2 ? s with { OwnerId = 1, CorpsId = null } : s).ToArray() });
        Assert.Equal(MilitaryMarker.Enter, new DeploymentTargets(new(view, 1)).Candidates[new(2, false)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FriendlyWorkAttackMarkerUsesTheWorkRuleRatherThanHostingCapability(bool canHost)
    {
        var view = View(world => world with { Settlements = world.Settlements.Select(s => s.Id == 2
            ? s with { OwnerId = 1, CorpsId = null, CanHostPersons = canHost } : s).ToArray() }, work: true);
        var draft = new DeploymentDraft(view, 1); draft.SetMembers([1]); draft.SetSoldiers(123);
        var targets = new DeploymentTargets(draft);
        Assert.Equal(MilitaryMarker.Attack, targets.Candidates[new(2, false)]);
        Assert.True(targets.Select(new(2, false))); Assert.True(targets.BeginPolicies());
        Assert.True(targets.Reachable);
        Assert.Equal(MilitaryOrderKind.AttackSettlement, targets.CreateCommand(Guid.NewGuid(), 0).Order.Kind);
    }

    [Theory]
    [InlineData(null, 99)]
    [InlineData(99, null)]
    public void DeploymentEntersFriendlyFacilitiesAcrossCorpsAndNeverTargetsFriendlyFieldUnits(int? sourceCorps, int? targetCorps)
    {
        var view = View(world => world with
        {
            Corps = world.Corps.Append(new(99, 1, 2, new(10000, 10000))).ToArray(),
            Settlements = world.Settlements.Select(s => s with { OwnerId = 1, CorpsId = s.Id == 1 ? sourceCorps : targetCorps }).ToArray(),
            Units = [new(8, 1, MapCoordinates.Center(new(3, 0)), 100) { FormationId = 900, CorpsId = targetCorps }]
        }, companion: new() { AllowDelegatedCorpsOrders = true });
        var draft = new DeploymentDraft(view, 1); Assert.True(draft.SetMembers([1])); draft.SetSoldiers(123);
        var targets = new DeploymentTargets(draft);
        Assert.Equal(MilitaryMarker.Enter, targets.Candidates[new(2, false)]);
        Assert.DoesNotContain(new MilitaryTarget(8, true), targets.Candidates.Keys);
        Assert.True(targets.Select(new(2, false))); Assert.True(targets.BeginPolicies());
        Assert.Equal(San9.Core.Simulation.ArrivalEstimateStatus.Known, targets.EstimateArrival().Status);
        Assert.Equal(MilitaryOrderKind.Return, targets.CreateCommand(Guid.NewGuid(), 0).Order.Kind);
    }

    [Fact]
    public void PolicyCommandCapturesDelayRelayAndPreparationWithoutMutatingWorld()
    {
        var view = View(); var draft = new DeploymentDraft(view, 1); draft.SetMembers([1]);
        var targets = new DeploymentTargets(draft);
        Assert.Equal(0, targets.DepartureDelayDays); Assert.False(targets.SetDepartureDelay(3));
        targets.Select(new(2, false)); Assert.True(targets.SetWaypoint(new(0, 3)));
        Assert.True(targets.BeginPolicies()); Assert.True(targets.SetDepartureDelay(9));
        Assert.False(targets.SetDepartureDelay(10)); Assert.False(targets.SetDepartureDelay(-1));
        targets.SetPolicies(new(true, false, true, false));
        var command = targets.CreateCommand(Guid.NewGuid(), view.World.Simulation.NextSequence);
        Assert.Equal(9, command.DepartureDelayDays);
        Assert.Equal(MapCoordinates.Center(new(0, 3)), command.Waypoint);
        Assert.Equal(targets.Policies, command.Policies); Assert.Equal(draft.Tactics, command.Tactics);
        Assert.Equal(draft.Members, command.OfficerIds); Assert.Empty(view.World.Units);
        targets.ReturnToTargets(); Assert.True(targets.BeginPolicies()); Assert.Equal(9, targets.DepartureDelayDays);
    }

    [Fact]
    public void RouteWaypointTargetSwitchAndPoliciesRemainLocalAndReversible()
    {
        var view = View(world => world with { Units = [new(2, 2, MapCoordinates.Center(new(3, 3)), 100) { FormationId = 900 }] });
        var hash = view.World;
        var draft = new DeploymentDraft(view, 1); draft.SetMembers([1]);
        var targets = new DeploymentTargets(draft);
        Assert.False(targets.SetWaypoint(new(0, 0))); Assert.False(targets.BeginPolicies());
        Assert.True(targets.Select(new(2, false))); Assert.True(targets.Reachable); Assert.NotEmpty(targets.Route);
        Assert.False(targets.EditingPolicies);
        Assert.True(targets.SetWaypoint(new(0, 3))); Assert.Contains(new MapCell(0, 3), targets.Route);
        var route = targets.Route;
        Assert.False(targets.SetWaypoint(new(1, 1))); Assert.Equal(route, targets.Route);
        Assert.False(targets.SetWaypoint(new(-1, 0))); Assert.Equal(new MapCell(0, 3), targets.Waypoint);
        Assert.Null(targets.Error);
        Assert.True(targets.Select(new(2, true))); Assert.Null(targets.Waypoint); Assert.True(targets.Reachable);
        Assert.True(targets.BeginPolicies()); targets.SetPolicies(new(true, true, false, true));
        Assert.False(targets.Select(new(2, false))); Assert.False(targets.SetWaypoint(new(0, 0)));
        targets.ReturnToTargets(); Assert.Equal(new UnitPolicies(true, true, false, true), targets.Policies);
        Assert.Equal(new MilitaryTarget(2, true), targets.Selected); Assert.NotEmpty(targets.Route);
        StateAssert.Equal(hash, view.World);
    }
}
