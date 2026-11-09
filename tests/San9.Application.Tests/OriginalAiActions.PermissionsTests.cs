using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed partial class OriginalAiActionsTests
{
    private static readonly (OriginalAiWork Work, AutomaticStrategyKind Kind)[] PermissionKinds =
    [
        (OriginalAiWork.FalseReport, AutomaticStrategyKind.FalseReport),
        (OriginalAiWork.Confuse, AutomaticStrategyKind.Disorder),
        (OriginalAiWork.Encourage, AutomaticStrategyKind.Inspire),
        (OriginalAiWork.Reinforce, AutomaticStrategyKind.Reinforce),
        (OriginalAiWork.Alienate, AutomaticStrategyKind.Alienate),
        (OriginalAiWork.Burn, AutomaticStrategyKind.Burn),
        (OriginalAiWork.Seize, AutomaticStrategyKind.Seize),
        (OriginalAiWork.Gossip, AutomaticStrategyKind.Gossip),
    ];
    private static readonly AutomaticStrategyPermissions NoStrategies = new()
    {
        FalseReport = false, Disorder = false, Inspire = false, Reinforce = false,
        Alienate = false, Burn = false, Seize = false, Gossip = false,
    };
    private static readonly (OriginalAiWork Work, AutomaticDiplomacyKind Kind)[] DiplomacyPermissionKinds =
    [
        (OriginalAiWork.Gift, AutomaticDiplomacyKind.Gift),
        (OriginalAiWork.RequestAttack, AutomaticDiplomacyKind.RequestAttack),
        (OriginalAiWork.ReturnPrisoner, AutomaticDiplomacyKind.ReturnPrisoner),
        (OriginalAiWork.Surrender, AutomaticDiplomacyKind.Surrender),
    ];
    private static readonly AutomaticDiplomacyPermissions NoDiplomacy = new()
    {
        Gift = false, RequestAttack = false, ReturnPrisoner = false, Surrender = false,
    };
    private static readonly OriginalAiWork[] PermissionWorks =
        [.. PermissionKinds.Select(p => p.Work), .. DiplomacyPermissionKinds.Select(p => p.Work)];

    public static IEnumerable<object[]> StrategyPermissionCases() =>
        from pair in PermissionKinds
        from player in new[] { false, true }
        from restrictPlayer in new[] { false, true }
        select new object[] { pair.Work, pair.Kind, player, restrictPlayer };

    [Theory]
    [MemberData(nameof(StrategyPermissionCases))]
    public void StrategyPermissionsApplyOnlyToTheSelectedScopeBeforePlanning(OriginalAiWork work,
        AutomaticStrategyKind kind, bool player, bool restrictPlayer)
    {
        var restricted = AutomaticStrategyPermissions.All.WithAllowed(kind, false);
        CheckAutomaticPermission(work, player, restrictPlayer, restrictPlayer
            ? new() { PlayerDelegatedStrategyPermissions = restricted }
            : new() { ComputerStrategyPermissions = restricted });
    }

    public static IEnumerable<object[]> DiplomacyPermissionCases() =>
        from pair in DiplomacyPermissionKinds
        from player in new[] { false, true }
        from restrictPlayer in new[] { false, true }
        select new object[] { pair.Work, pair.Kind, player, restrictPlayer };

    [Theory]
    [MemberData(nameof(DiplomacyPermissionCases))]
    public void DiplomacyPermissionsApplyOnlyToTheSelectedScopeBeforePlanning(OriginalAiWork work,
        AutomaticDiplomacyKind kind, bool player, bool restrictPlayer)
    {
        var restricted = AutomaticDiplomacyPermissions.All.WithAllowed(kind, false);
        CheckAutomaticPermission(work, player, restrictPlayer, restrictPlayer
            ? new() { PlayerDelegatedDiplomacyPermissions = restricted }
            : new() { ComputerDiplomacyPermissions = restricted });
    }

    private static void CheckAutomaticPermission(OriginalAiWork work, bool player, bool restrictPlayer, CompanionSettings settings)
    {
        bool blocked = player == restrictPlayer;
        int draws = 0;
        var h = new Harness(change: w => w with
        {
            Simulation = w.Simulation with { Controls = [.. w.Simulation.Controls.Select(c => c.ForceId == 1
                ? c with { Kind = player ? ControllerKind.Player : ControllerKind.Automatic }
                : c.ForceId == 2 ? c with { Kind = ControllerKind.Player } : c)] },
            Units = w.Units.Select(u => work == OriginalAiWork.Confuse && u.Id == 10
                ? u with { Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 20 } } : u).ToArray(),
            Officers = w.Officers.Select(p => work == OriginalAiWork.ReturnPrisoner && p.Id == 12
                ? p with { Status = OfficerStatus.Captive, SettlementId = 2 } : p).ToArray(),
            Settlements = w.Settlements.Select(s => work == OriginalAiWork.Gift && s.Id == 1
                ? s with { Soldiers = 1000 } : s).ToArray(),
            Relations = w.Relations.Select(r => work == OriginalAiWork.Gift && r.FirstId == 1 && r.SecondId == 2
                ? r with { Value = 40 } : r).ToArray(),
        }, draw: maximum => { draws++; Assert.False(blocked, "A prohibited action consumed a planning draw."); return maximum - 1; });
        if (work == OriginalAiWork.Surrender) h.State = h.State with { DiplomaticOrder = [1, 2, 3] };
        h.Engine.ConfigureCompanion(settings);
        var before = h.Engine.Snapshot;
        var report = h.Run(work);
        if (!blocked)
        {
            Assert.Equal(1, report.Submitted);
            return;
        }
        Assert.Equal(OriginalAiWorkEnd.Permission, report.End);
        Assert.Equal(0, report.Attempts);
        Assert.Equal(0, report.PathQueries);
        Assert.Equal(0, draws);
        Assert.Empty(h.Commands);
        StateAssert.Equal(before, h.Engine.Snapshot);
    }

    private static ScenarioInitialState PermissionContent()
    {
        var content = Fixture();
        return content with
        {
            Information = content.Information with { Corps = [.. content.Information.Corps.Select(c => c.Id == 1
                ? c : c)] },
            OriginalAi = content.OriginalAi with
            {
                WorkRules = content.OriginalAi.WorkRules.ToImmutableDictionary(p => p.Key, p => p.Value with
                    { OpeningWaitDays = PermissionWorks.Contains(p.Key) ? 0 : 10000, PeriodDays = 0 }),
                SecondaryBatches = content.OriginalAi.SecondaryBatches.ToImmutableDictionary(p => p.Key,
                    _ => PermissionWorks.Where(w => !OriginalAiTurnRunner.RootFirst.Contains(w)).ToImmutableArray()),
            }
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionSchedulerReportsProhibitedActionsWithoutPlanning(bool player)
    {
        var session = GameSession.Start(PermissionContent(), 42, playerForceController: player ? ControllerKind.Player : ControllerKind.Automatic,
            companionSettings: player ? new() { PlayerCorpsAutomation = PlayerCorpsAutomationMode.Full, PlayerDelegatedStrategyPermissions = NoStrategies, PlayerDelegatedDiplomacyPermissions = NoDiplomacy }
                : new() { ComputerStrategyPermissions = NoStrategies, ComputerDiplomacyPermissions = NoDiplomacy });
        if (player) session.EndControl(new("player")); else session.AdvanceXun();
        var reports = session.LastOriginalAiWork.Where(r => r.CorpsId == 1 && PermissionWorks.Contains(r.Work)).ToArray();
        Assert.Equal(PermissionWorks.Length, reports.Length);
        Assert.All(reports, r =>
        {
            Assert.Equal(OriginalAiWorkEnd.Permission, r.End);
            Assert.Equal(0, r.Attempts);
            Assert.Equal(0, r.Submitted);
            Assert.Equal(0, r.PathQueries);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PermissionsDoNotRestrictManualCommandsOrRecallExistingMissions(bool diplomacy)
    {
        var settings = new CompanionSettings
        {
            ComputerStrategyPermissions = NoStrategies, PlayerDelegatedStrategyPermissions = NoStrategies,
            ComputerDiplomacyPermissions = NoDiplomacy, PlayerDelegatedDiplomacyPermissions = NoDiplomacy,
        };
        var h = new Harness();
        h.Engine.ConfigureCompanion(settings);
        long sequence = h.Engine.Snapshot.Simulation.NextSequence;
        GameCommand command = diplomacy ? new RequestAttackCommand(Guid.NewGuid(), 1, sequence, 11, 3, 2)
            : new BurnCommand(Guid.NewGuid(), 1, sequence, 11, 2);
        Assert.True(h.Engine.Execute(new("player"), command).Submission.Accepted);

        var basis = PermissionContent();
        var work = diplomacy ? OriginalAiWork.RequestAttack : OriginalAiWork.Encourage;
        var content = basis with
        {
            Information = basis.Information with { Corps = [.. basis.Information.Corps.Select(c => diplomacy && c.Id == 1
                ? c : c)] },
            OriginalAi = basis.OriginalAi with
            {
                WorkRules = basis.OriginalAi.WorkRules.ToImmutableDictionary(p => p.Key, p => p.Value with
                    { OpeningWaitDays = p.Key == work ? 0 : 10000 }),
                SecondaryBatches = basis.OriginalAi.SecondaryBatches.ToImmutableDictionary(p => p.Key,
                    _ => diplomacy ? ImmutableArray<OriginalAiWork>.Empty : [work]),
            },
        };
        var session = GameSession.Start(content, 42, companionSettings: new() { PlayerCorpsAutomation = PlayerCorpsAutomationMode.Full });
        var started = diplomacy
            ? session.Execute(new("player"), new RequestAttackCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 11, 3, 2)).Events
            : session.EndControl(new("player")).Events;
        var mission = Assert.Single(started.OfType<TaskStartedEvent>()).Task;
        if (diplomacy) session.EndControl(new("player"));
        session.ConfigureCompanion(settings);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        restored.ConfigureCompanion(settings);
        bool completed = false;
        for (int i = 0; i < 4; i++)
        {
            var expected = session.AdvanceXun();
            var actual = restored.AdvanceXun();
            completed |= expected.Events.OfType<TaskCompletedEvent>().Any(e => e.TaskId == mission.Id);
            EventAssert.Equal(expected.Events, actual.Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
        }
        Assert.True(completed);
    }
}
