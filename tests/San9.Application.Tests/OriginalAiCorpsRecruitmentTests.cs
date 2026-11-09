using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class OriginalAiCorpsRecruitmentTests
{
    private static ScenarioInitialState Fixture(bool domestic = true)
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with
        {
            Settlements = [.. basis.Settlements, basis.Settlements[0] with { Id = 3, CorpsId = 3,
                Soldiers = 100, DraftPopulation = 100000, Commerce = 0, Cultivation = 0, PublicOrder = 0 }],
            Officers = basis.Officers.Select(p => p.State.Id is 2 or 3 ? p with { State = p.State with
                { SettlementId = 3, BaseAbilities = new(100, 100, 100, 100) } } : p).ToArray(),
            Information = basis.Information with { Relations = [new(1, 2, 100)], Corps = [.. basis.Information.Corps,
                new(3, 1, 2, new(100000, 1000000)) {
                    Delegation = domestic ? new DomesticDelegation() : new GeneralDelegation() }] },
            CityConnections = [new(3, 2)],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 3 },
            RegionNameKeys = new() { [1] = "one", [2] = "two", [3] = "three" },
            OriginalAi = basis.OriginalAi with { WorkRules = basis.OriginalAi.WorkRules.SetItem(OriginalAiWork.Recruit, new(0, 0, 1, true, true)) }
        };
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void RecruitmentRequiresExplicitDomesticDelegationAndTheOwningForceOption(bool player, bool enablePlayer, bool domestic)
    {
        var session = GameSession.Start(Fixture(domestic), 190,
            playerForceController: player ? ControllerKind.Player : ControllerKind.Automatic,
            companionSettings: new()
            {
                ComputerCorpsOptions = new() { DomesticDelegationRecruitment = !enablePlayer },
                PlayerDelegatedCorpsOptions = new() { DomesticDelegationRecruitment = enablePlayer }
            });
        var result = session.Apply(session.Request(new EndControlOperation(), player ? "player" : "ai:1"));
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(OriginalAiMode.Domestic, session.OriginalAi.Corps.Single(c => c.CorpsId == 3).Mode);
        bool recruit = domestic && player == enablePlayer;
        var reports = session.LastOriginalAiWork.Where(r => r.CorpsId == 3).ToArray();
        Assert.Equal(recruit, result.Events.OfType<DevelopmentCompletedEvent>().Any(e =>
            e.Command.SettlementId == 3 && e.Command.Activity == DomesticActivity.Recruit));
        Assert.Equal(recruit, reports.Any(r => r.Work == OriginalAiWork.Recruit));
        if (recruit)
            Assert.True(Array.FindIndex(reports, r => r.Work == OriginalAiWork.Recruit) <
                Array.FindIndex(reports, r => r.Work == OriginalAiWork.Train));
    }

    [Fact]
    public void RestoredSessionUsesTheSameExternalRecruitmentOption()
    {
        var content = Fixture();
        var settings = new CompanionSettings { PlayerDelegatedCorpsOptions = new() { DomesticDelegationRecruitment = true } };
        var session = GameSession.Start(content, 190, companionSettings: settings);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(CompanionSettings.Default, restored.Companion);
        restored.ConfigureCompanion(settings);
        var request = session.Request(new EndControlOperation());
        var result = session.Apply(request);
        Assert.Equal(SessionDisposition.Accepted, result.Disposition);
        Assert.Equal(SessionDisposition.Accepted, restored.Apply(request).Disposition);
        Assert.Contains(result.Events.OfType<DevelopmentCompletedEvent>(), e =>
            e.Command.SettlementId == 3 && e.Command.Activity == DomesticActivity.Recruit);
        StateAssert.Equal(session.Capture(), restored.Capture());
    }
}
