using System.Collections.Immutable;
using San9.Application.AI;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransportOptionRestrictsReceiversInTheOwningForceScope(bool player) =>
        CheckSupportOption(SplitTransportFixture(), OriginalAiWork.Transport, 1, player);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReliefOptionRestrictsTargetsInTheOwningForceScope(bool player) =>
        CheckSupportOption(SupportReliefFixture(2), OriginalAiWork.Deploy, 3, player, w => EnemyArmy(w, target: 3));

    private static void CheckSupportOption(ScenarioInitialState basis, OriginalAiWork work, int target, bool player,
        Func<WorldSnapshot, WorldSnapshot>? prepare = null)
    {
        int home = basis.Settlements.First(s => s.OwnerId == 1 && s.Id != target).Id;
        var governor = basis.Officers.Single(p => p.State.Id == 2);
        var content = basis with
        {
            Settlements = basis.Settlements.Select(s => s.Id == target ? s with { CorpsId = 3 } : s).ToArray(),
            Officers = basis.Officers.Select(p => p.State.Id == 1 ? p with { State = p.State with { SettlementId = home } } : p)
                .Append(governor with { NameKey = "governor", State = governor.State with
                    { Id = 20, SettlementId = target, Status = OfficerStatus.Governor } }).ToArray(),
            Information = basis.Information with { Corps = [.. basis.Information.Corps, new(3, 1, 20, new(1000, 1000))] }
        };
        WorldSnapshot Change(WorldSnapshot world)
        {
            world = prepare?.Invoke(world) ?? world;
            return world with { Simulation = world.Simulation with { Controls = world.Simulation.Controls.Select(c => c with
                { Kind = c.ForceId == 1 && !player ? ControllerKind.Automatic : ControllerKind.Player }).ToImmutableArray() } };
        }
        var restriction = new AutomaticCorpsOptions
            { CrossCorpsTransport = work != OriginalAiWork.Transport, CrossCorpsRelief = work != OriginalAiWork.Deploy };
        var settings = player ? new CompanionSettings { PlayerDelegatedCorpsOptions = restriction }
            : new CompanionSettings { ComputerCorpsOptions = restriction };
        var h = new Harness(content, Change, draw: _ => throw new InvalidOperationException("Support must not draw."));
        h.Engine.ConfigureCompanion(settings);
        var before = h.Engine.Snapshot;
        Assert.Equal(0, h.Run(work, 1).Submitted);
        Assert.Empty(h.Commands);
        StateAssert.Equal(before, h.Engine.Snapshot);

        // Restricting the other scope must leave this force's support available.
        h.Engine.ConfigureCompanion(player ? new() { ComputerCorpsOptions = restriction }
            : new() { PlayerDelegatedCorpsOptions = restriction });
        Assert.Equal(1, h.Run(work, 1).Submitted);
        var command = Assert.Single(h.Commands);
        var manual = GameSession.EngineFactory(content)(WorldState.Restore(before));
        manual.ConfigureCompanion(settings);
        Assert.True(manual.Execute(new("player"), command).Submission.Accepted);

        var own = new Harness(content, w => Change(w with { Settlements = w.Settlements.Select(s => s.Id == target
            ? s with { CorpsId = null } : s).ToArray() }));
        own.Engine.ConfigureCompanion(settings);
        Assert.Equal(1, own.Run(work, 1).Submitted);
    }
}
