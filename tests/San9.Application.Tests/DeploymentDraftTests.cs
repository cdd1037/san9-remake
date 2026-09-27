using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class DeploymentDraftTests
{
    private static StrategySnapshot View(Func<San9.Core.Simulation.WorldSnapshot, San9.Core.Simulation.WorldSnapshot>? change = null)
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var slots = new FormationPresentation(3, 3, [new(1, 0), new(0, 1), new(1, 1), new(2, 1), new(1, 2)], 3, 180, null, null);
        var line = slots with { Slots = [new(0, 0), new(0, 1), new(1, 1), new(1, 2), new(2, 2)] };
        var formation = new FormationRule(100, 50, 0, 100000, 5, new(1, 1), TravelMode.Land) { Combat = new(10, 10, 10, 10, 50) };
        definition = definition with
        {
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, 1)],
                [formation,formation with { Id=900,MovementBudget=100 },formation with { Id=200,RequiredTacticId=5000,DeploymentCost=100 },
                 formation with { Id=700,TravelMode=TravelMode.Water }],
                [new(1, MapCoordinates.Center(new(1, 1)), new(1, 1)), new(2, MapCoordinates.Center(new(2, 2)), new(1, 1))])
            {
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
        return new(definition, change is null ? world : change(world));
    }
    [Fact]
    public void PreparationKeepsWorldUntouchedAndUsesCommanderAndCurrentMembers()
    {
        var view = View(); string hash = view.World.StateHash; var draft = new DeploymentDraft(view, 1);
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
        Assert.Equal(hash, view.World.StateHash); Assert.Empty(view.World.Units);
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
        string hash = view.World.StateHash;
        var targets = new DeploymentTargets(draft);
        Assert.Equal(2, targets.Candidates.Count);
        Assert.Equal(DeploymentMarker.Attack, targets.Candidates[new(2, false)]);
        Assert.Equal(DeploymentMarker.Attack, targets.Candidates[new(2, true)]);
        Assert.False(targets.Select(new(1, false)));
        Assert.True(targets.Select(new(2, true)));
        Assert.Equal(new DeploymentTarget(2, true), targets.Selected);
        Assert.Contains(new MapCell(3, 0), targets.ClearCells);
        Assert.DoesNotContain(new MapCell(0, 0), targets.ClearCells);
        Assert.Equal(hash, view.World.StateHash); Assert.Equal(123u, draft.Soldiers);
        Assert.Equal(new[] { 1 }, draft.Members);
    }

    [Fact]
    public void FriendlyOrdinaryFacilityUsesEntryMarkerAndHostileFacilityUsesAttack()
    {
        var view = View(world => world with { Settlements = world.Settlements.Select(s => s.Id == 2 ? s with { OwnerId = 1, CorpsId = null } : s).ToArray() });
        Assert.Equal(DeploymentMarker.Enter, new DeploymentTargets(new(view, 1)).Candidates[new(2, false)]);
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
        string hash = view.World.StateHash;
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
        Assert.Equal(new DeploymentTarget(2, true), targets.Selected); Assert.NotEmpty(targets.Route);
        Assert.Equal(hash, view.World.StateHash);
    }
}
