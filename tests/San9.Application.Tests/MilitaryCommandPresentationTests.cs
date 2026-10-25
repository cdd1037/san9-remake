using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class MilitaryCommandPresentationTests
{
    private static ScenarioInitialState Definition()
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with
        {
            Settlements = basis.Settlements.Select(s => s with { Soldiers = 1000 }).ToArray(),
            Military = new(8, 8, Enumerable.Repeat(0, 64).ToImmutableArray(), [new(0, 1, null)],
                [new(7, 1, 50, 10000, 5, new(1, 1), TravelMode.Land)
                    { Combat = new(10, 10, 10, 10, 50), Attack = new(10, true),
                        CaptureRanks = Enumerable.Repeat(BattleRank.Middle, 5).ToImmutableArray() }],
                [new(1, new(7, 7), new(1, 1)), new(2, new(12, 12), new(1, 1))])
        };
    }

    private static (CommandPresentation Content, TextCatalog Texts) Presentation()
    {
        string[] roles = ["sortie", "reinforce", "build", "transport", "transport-redirect", "attack", "march", "retreat", "adjust"];
        var messages = roles.ToDictionary(r => r, r => new DecisionMessageDefinition(r,
            new() { ["person.0"] = "commander", ["person.1"] = "recipient" }, "commander", "order"));
        messages["attack"] = messages["attack"] with { Bindings = new(messages["attack"].Bindings) { ["unit.0"] = "target" } };
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            new() { ["city.2"] = "Enemy city" })
        {
            Messages = roles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)]))
        };
        texts.Messages["attack"] = new([new MessageValue(new("property", "military-target.location-name",
            Arguments: [new("binding", "unit.0")]))]);
        return (CommandPresentation.Empty with { Military = messages, Sounds = new() { ["order"] = "order.wav" } }, texts);
    }

    [Fact]
    public void AcceptedDeploymentUsesCommittedCommanderAndLeavesWorldUnchangedDuringPresentation()
    {
        var session = GameSession.Start(Definition(), 42);
        var before = session.View;
        var command = new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [3, 2], 100, 7,
            new(MilitaryOrderKind.Move, new(32, 7), null));
        var result = session.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted, result.Submission.Rejection.ToString());
        var (content, texts) = Presentation();
        var hash = session.Snapshot;
        var message = Assert.Single(MilitaryCommandPresentation.Create(before, session.View, session.Messages(texts),
            content, command, result.Events, _ => 0));
        Assert.Equal(2, message.SpeakerId);
        Assert.Equal("sortie", message.Message.Key);
        Assert.Equal("order", message.SoundRole);
        StateAssert.Equal(hash, session.Snapshot);
    }

    [Theory]
    [InlineData(false, false, "march")]
    [InlineData(false, true, "retreat")]
    [InlineData(true, true, "adjust")]
    public void MarchDistinguishesChangedDestinationFromPolicyOnlyChanges(bool sameTarget, bool engaged, string expected)
    {
        var definition = Definition();
        var session = GameSession.Start(definition, 42);
        var previous = new UnitState(10, 1, new(17, 7), 100)
        {
            FormationId = 7, CommanderId = 2, Morale = 100,
            // Stored combat flags can outlive their target; current incoming attacks determine engagement.
            ActiveEngagement = !engaged,
            EngagementTargetId = engaged ? null : 11,
            Order = new(MilitaryOrderKind.Return, null, sameTarget ? 1 : 2)
        };
        var enemy = new UnitState(11, 2, engaged ? new(19, 7) : new(32, 32), 100)
        {
            FormationId = 7, Morale = 100,
            Order = engaged ? new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = previous.Id } : null
        };
        var world = session.Snapshot with
        {
            Units = [previous, enemy],
            Simulation = session.Snapshot.Simulation with { NextUnitId = 12 },
            Officers = session.Snapshot.Officers.Select(p => p.Id == 2
                ? p with { SettlementId = null, UnitId = previous.Id } : p).ToImmutableArray()
        };
        var before = new StrategySnapshot(definition, world);
        Assert.Equal(engaged, before.IsUnitEngaged(previous.Id));
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, 0, 10, new(MilitaryOrderKind.Return, null, 1))
            { Policies = new(true, false, true, false), Waypoint = new(22, 7) };
        var afterWorld = world with { Units = [previous with { Order = command.Order, Policies = command.Policies!, Waypoint = command.Waypoint }, enemy] };
        var (content, texts) = Presentation();
        var message = Assert.Single(MilitaryCommandPresentation.Create(before, new(definition, afterWorld),
            new(definition, afterWorld, texts), content, command, [], _ => 0));
        Assert.Equal(expected, message.Message.Key);
    }

    [Fact]
    public void BuildingAttackResolvesItsGarrisonNameWithoutCreatingAnArtificialUnit()
    {
        var definition = Definition();
        var session = GameSession.Start(definition, 42);
        var unit = new UnitState(10, 1, new(17, 7), 100) { FormationId = 7, CommanderId = 2 };
        var world = session.Snapshot with { Units = [unit] };
        var view = new StrategySnapshot(definition, world);
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, 0, 10, new(MilitaryOrderKind.AttackSettlement, null, 2));
        var (content, texts) = Presentation();
        var context = new WorldMessageContext(definition, world, texts);
        var message = Assert.Single(MilitaryCommandPresentation.Create(view, view, context, content, command, [], _ => 0));
        Assert.Equal("Enemy city", message.Message.BbCode);
        Assert.Equal(1, context.Query("military-target.is-garrison", [MessageBinding.Reference(MessageEntityKind.Settlement, 2)]).Number);
        Assert.Throws<InvalidDataException>(() => context.Query("military-target.is-garrison",
            [MessageBinding.Reference(MessageEntityKind.Settlement, 999)]));
    }

    [Theory]
    [InlineData(false, "transport-redirect")]
    [InlineData(true, "adjust")]
    public void TransportChangeUsesItsOwnDialogueAndSameDestinationUsesAdjustment(bool sameTarget, string expected)
    {
        var definition = Definition();
        var session = GameSession.Start(definition, 42);
        var unit = new UnitState(10, 1, new(17, 7), 100) { FormationId = 7, CommanderId = 2,
            Order = new(MilitaryOrderKind.Transport, null, sameTarget ? 1 : 2) };
        var world = session.Snapshot with { Units = [unit] };
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, 0, 10, new(MilitaryOrderKind.Transport, null, 1));
        var (content, texts) = Presentation();
        var message = Assert.Single(MilitaryCommandPresentation.Create(new(definition, world), new(definition, world),
            new(definition, world, texts), content, command, [], _ => 0));
        Assert.Equal(expected, message.Message.Key);
        Assert.Equal(2, message.SpeakerId);
        Assert.Equal("order", message.SoundRole);
    }
}
