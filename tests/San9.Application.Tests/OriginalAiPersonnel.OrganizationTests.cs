using System.Collections.Immutable;
using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class OriginalAiPersonnelTests
{
    private static OriginalAiWorkReport RunOrganization(Harness harness, ScenarioInitialState content)
    {
        bool Submit(GameCommand command)
        {
            harness.Commands.Add(command);
            var result = harness.Engine.Execute(new("player"), command);
            Assert.True(result.Submission.Accepted, result.Submission.Rejection.ToString());
            return true;
        }
        return OriginalAiStaffing.Organize(harness.Engine, content, 1, new(content, null, harness.Engine.Snapshot), Submit);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void OrganizationPermissionUsesTheActingForceAndPreservesExistingCorpsWhenDisabled(bool player, bool allowed)
    {
        var content = OrganizationFixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id == 3 ? s with { CorpsId = 10 } : s).ToArray(),
            Officers = content.Officers.Select(o => o.State.Id == 2 ? o with
                { State = o.State with { SettlementId = 3, Status = OfficerStatus.Governor } } : o).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Append(new(10, 1, 2, new(1000, 5000))).ToArray() },
            OriginalAi = content.OriginalAi with { WorkRules = content.OriginalAi.WorkRules
                .ToImmutableDictionary(p => p.Key, p => p.Key == OriginalAiWork.CreateCorps ? p.Value : p.Value with { OpeningWaitDays = 10000 }) }
        };
        var initial = new Harness(content).Engine.Snapshot;
        initial = initial with { Simulation = initial.Simulation with { Controls = initial.Simulation.Controls.Select(seat =>
            seat with { Kind = (seat.ForceId == 1 ? player : !player) ? ControllerKind.Player : ControllerKind.Automatic,
                ControllerId = (seat.ForceId == 1 ? player : !player) ? "player" : $"ai:{seat.ForceId}" }).ToImmutableArray() } };
        var settings = new CompanionSettings
        {
            PlayerCorpsAutomation = PlayerCorpsAutomationMode.Full,
            ComputerCorpsOptions = new() { AutomaticOrganization = player ? !allowed : allowed },
            PlayerDelegatedCorpsOptions = new() { AutomaticOrganization = player ? allowed : !allowed }
        };
        var session = GameSession.Restore(content, new(initial, [], [], 0) { OriginalAi = OriginalAiState.Create(content) },
            GameSession.EngineFactory(content));
        session.ConfigureCompanion(settings);
        var result = session.Apply(session.Request(new EndControlOperation(), initial.Simulation.Controls[0].ControllerId));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        var report = session.LastOriginalAiWork.Single(r => r.CorpsId == 1 && r.Work == OriginalAiWork.CreateCorps);
        if (allowed)
        {
            Assert.True(report.Submitted > 0);
            Assert.DoesNotContain(session.Snapshot.Corps, c => c.Id == 10);
            Assert.NotEmpty(result.Events.OfType<CorpsCreatedEvent>());
            return;
        }
        Assert.Equal(OriginalAiWorkEnd.Permission, report.End);
        Assert.Equal(0, report.Attempts);
        Assert.Equal(0, report.Submitted);
        Assert.Equal(initial.Corps, session.Snapshot.Corps);
        Assert.Equal(initial.Settlements, session.Snapshot.Settlements);
        Assert.Empty(result.Events.OfType<CorpsCreatedEvent>());
        Assert.Contains(session.LastOriginalAiWork, r => r.CorpsId == 10);

        var manual = GameSession.Restore(content, new(initial, [], [], 0) { OriginalAi = OriginalAiState.Create(content) },
            GameSession.EngineFactory(content));
        manual.ConfigureCompanion(settings);
        var command = new CreateCorpsCommand(Guid.NewGuid(), 1, initial.Simulation.NextSequence, [6, 7], 0, 0, new GeneralDelegation())
            { GovernorId = 106 };
        Assert.True(manual.Execute(new(initial.Simulation.Controls[0].ControllerId), command).Submission.Accepted);
    }

    [Fact]
    public void OrganizationCanRegroupAnExistingChildCityWithTheRootCityInItsRegion()
    {
        var content = OrganizationFixture();
        content = content with
        {
            Settlements = content.Settlements.Select(s => s.Id is 3 or 6 ? s with { CorpsId = 10 } : s)
                .Append(content.Settlements[0] with { Id = 10, X = 10, Y = 10 }).ToArray(),
            CityProvinceIds = new(content.CityProvinceIds) { [10] = 0 },
            RegionCityIds = new(content.RegionCityIds) { [10] = 10 },
            RegionNameKeys = new(content.RegionNameKeys) { [10] = "region.10" },
            Officers = content.Officers.Select(o => o.State.Id == 2 ? o with
                { State = o.State with { SettlementId = 3, Status = OfficerStatus.Governor } } : o).Append(
                new("officer.110", content.Officers.Single(o => o.State.Id == 109).State with { Id = 110, SettlementId = 10 })).ToArray(),
            Information = content.Information with { Corps = content.Information.Corps.Append(
                new CorpsState(10, 1, 2, new(1000, 5000))).ToArray() }
        };
        var h = new Harness(content);
        Assert.Equal(7, h.Engine.Snapshot.Settlements.Count(s => s.OwnerId == 1 && s.Kind == SettlementKind.City &&
            s.CorpsId is null or 1));

        Assert.Equal(2, RunOrganization(h, content).Submitted);

        Assert.Equal(10, Assert.IsType<DissolveCorpsCommand>(h.Commands[0]).CorpsId);
        var create = Assert.IsType<CreateCorpsCommand>(h.Commands[1]);
        Assert.Equal(new[] { 6, 7 }, create.CityIds);
        Assert.Equal(106, create.GovernorId);
        Assert.DoesNotContain(h.Engine.Snapshot.Corps, c => c.Id == 10);
        Assert.All(h.Engine.Snapshot.Settlements.Where(s => s.Id is 6 or 7), s =>
            Assert.Equal(h.Engine.Snapshot.Corps.Single(c => c.LeaderId == 106).Id, s.CorpsId));
    }

    [Fact]
    public void OrganizationKeepsItsOwnValidPartitionOnSubsequentPasses()
    {
        var content = OrganizationFixture();
        var h = new Harness(content);
        Assert.Equal(2, RunOrganization(h, content).Submitted);
        var established = h.Engine.Snapshot;
        Assert.Equal(0, RunOrganization(h, content).Submitted);
        Assert.Equal(0, RunOrganization(h, content).Submitted);
        Assert.Equal(established, h.Engine.Snapshot);
        Assert.Equal(2, h.Commands.Count);
    }

    [Fact]
    public void OrganizationKeepsASingleEligiblePartitionBelowTheCorpsLimit()
    {
        var content = OrganizationFixture();
        content = content with { CityConnections = [new(6, 2)] };
        var h = new Harness(content);
        Assert.Equal(1, RunOrganization(h, content).Submitted);
        var established = h.Engine.Snapshot;
        Assert.Single(established.Corps, c => c.OwnerId == 1 && !c.IsRoot);
        Assert.Equal(0, RunOrganization(h, content).Submitted);
        Assert.Equal(0, RunOrganization(h, content).Submitted);
        Assert.Equal(established, h.Engine.Snapshot);
        Assert.Single(h.Commands);
    }

    [Fact]
    public void OrganizationFillsAnotherRegionWithoutDissolvingItsExistingPartition()
    {
        var content = OrganizationFixture();
        content = content with { CityConnections = [new(6, 2)] };
        var h = new Harness(content);
        Assert.Equal(1, RunOrganization(h, content).Submitted);
        int established = Assert.Single(h.Engine.Snapshot.Corps, c => c.OwnerId == 1 && !c.IsRoot).Id;
        var expanded = content with { CityConnections = [new(6, 2), new(8, 2)] };
        Assert.Equal(1, RunOrganization(h, expanded).Submitted);
        Assert.Contains(h.Engine.Snapshot.Corps, c => c.Id == established);
        Assert.All(h.Commands, c => Assert.IsType<CreateCorpsCommand>(c));
        Assert.Equal(0, RunOrganization(h, expanded).Submitted);
    }

    [Theory]
    [InlineData(false, false, 25000u, 12500u)]
    [InlineData(true, false, 33333u, 5555u)]
    [InlineData(false, true, 33333u, 5555u)]
    [InlineData(true, true, 37500u, 3125u)]
    public void OrganizationFoodSharesIncludeRegionalGarrisonsAndServiceUnitsAndKeepTheSameRootDenominator(
        bool regionalGarrison, bool serviceUnit, uint firstFood, uint secondFood)
    {
        var content = OrganizationFixture();
        content = content with { Settlements = content.Settlements.Select(s => s with
            { Soldiers = s.Id is 6 or 7 or 8 or 9 ? 100u : 0u }).ToArray() };
        if (regionalGarrison)
            content = content with
            {
                Settlements = content.Settlements.Append(content.Settlements[0] with
                    { Id = 60, X = 6, Y = 5, Kind = SettlementKind.Facility, Soldiers = 100, Wounded = 100 }).ToArray(),
                RegionCityIds = new(content.RegionCityIds) { [60] = 6 },
                RegionNameKeys = new(content.RegionNameKeys) { [60] = "region.60" }
            };
        var places = content.Settlements.Select(s => new BattlefieldPlace(s.Id, s.Position, new(1, 1))
            { FacilityTypeId = s.Kind == SettlementKind.Facility ? 600 : 601 }).ToImmutableArray();
        content = content with
        {
            Military = new(20, 12, Enumerable.Repeat(0, 240).ToImmutableArray(), [new(0, 1, null)],
                [new(400, 100, 0, 30000, 5, new(1, 1), TravelMode.Land)], places)
            { Facilities = [new(600, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, []),
                new(601, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, [])] }
        };
        if (serviceUnit)
            content = content with
            {
                Officers = content.Officers.Append(new("officer.910", content.Officers.Single(o => o.State.Id == 109).State with
                    { Id = 910, SettlementId = null, UnitId = 900, IsCommander = false })).ToArray(),
                Units = [new(900, 1, new(32, 32), 100) { Wounded = 100, FormationId = 400, CommanderId = 910,
                    ServicePlaceId = 6, CorpsId = 1, OrderClass = UnitOrderClass.Military,
                    Order = new(MilitaryOrderKind.Move, new(37, 32), null) }]
            };
        var h = new Harness(content);

        Assert.Equal(2, RunOrganization(h, content).Submitted);

        var created = h.Commands.Cast<CreateCorpsCommand>().ToArray();
        Assert.Equal(new[] { firstFood, secondFood }, created.Select(c => c.Food));
        var children = h.Engine.Snapshot.Corps.Where(c => c.OwnerId == 1 && !c.IsRoot).ToArray();
        Assert.Equal(new[] { firstFood, secondFood }, children.Select(c => c.Treasury.Food));
        if (regionalGarrison) Assert.Equal(children[0].Id, h.Engine.Snapshot.Settlements.Single(s => s.Id == 60).CorpsId);
        if (serviceUnit) Assert.Equal(children[0].Id, h.Engine.Snapshot.Units.Single(u => u.Id == 900).CorpsId);
        Assert.Equal(50000UL, h.Engine.Snapshot.Corps.Where(c => c.OwnerId == 1)
            .Aggregate(0UL, (total, c) => total + c.Treasury.Food));
    }
}
