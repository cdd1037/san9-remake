using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Projects accepted player orders; neither message choices nor repainting consume simulation RNG.</summary>
public static class MilitaryCommandPresentation
{
    public static IReadOnlyList<CommandMessage> Create(StrategySnapshot before, StrategySnapshot after,
        WorldMessageContext context, CommandPresentation content, GameCommand command,
        IReadOnlyList<DomainEvent> events, Func<int, int> random)
    {
        UnitState unit;
        MilitaryOrder order;
        string role;
        MessageBinding? target = null;
        if (command is OrderUnitCommand change)
        {
            var previous = before.World.Units.Single(u => u.Id == change.UnitId);
            unit = after.World.Units.Single(u => u.Id == change.UnitId);
            order = change.Order;
            bool sameTarget = previous.Order is { } current && current.Kind == order.Kind &&
                current.UnitId == order.UnitId && current.SettlementId == order.SettlementId && current.Position == order.Position;
            role = sameTarget ? "adjust" : order.Kind switch
            {
                MilitaryOrderKind.AttackUnit or MilitaryOrderKind.AttackSettlement => "attack",
                MilitaryOrderKind.Return => before.IsUnitEngaged(previous.Id) ? "retreat" : "march",
                MilitaryOrderKind.Transport => "transport-redirect",
                _ => throw new ArgumentException("Unsupported interactive unit order.", nameof(command))
            };
        }
        else if (command is DeployUnitCommand or ConstructFacilityCommand or TransportUnitCommand)
        {
            int unitId = events.OfType<UnitDeployedEvent>().Single().UnitId;
            unit = after.World.Units.Single(u => u.Id == unitId);
            order = unit.Order ?? throw new InvalidOperationException("Deployment did not create an order.");
            role = command switch
            {
                ConstructFacilityCommand => "build",
                TransportUnitCommand => "transport",
                DeployUnitCommand when order.Kind == MilitaryOrderKind.Return => "reinforce",
                _ => "sortie"
            };
            if (command is ConstructFacilityCommand construction)
                target = MessageBinding.Reference(MessageEntityKind.Region, construction.RegionId);
        }
        else throw new ArgumentException("Unsupported military command.", nameof(command));

        target ??= order.UnitId is int enemy ? MessageBinding.Reference(MessageEntityKind.Unit, enemy) :
            order.SettlementId is int place ? MessageBinding.Reference(MessageEntityKind.Settlement, place) : null;
        int commander = unit.CommanderId ?? throw new InvalidOperationException("Military unit has no commander.");
        int? sourceId = command switch
        {
            DeployUnitCommand deployment => deployment.SettlementId,
            ConstructFacilityCommand construction => construction.SettlementId,
            TransportUnitCommand transport => transport.SettlementId,
            _ => null
        };
        int recipient = sourceId is int source
            ? SettlementStaffing.Read(before, source).Present.FirstOrDefault(p => p.IsCommander && p.OwnerId == command.IssuerId)?.Id ?? commander
            : before.World.Forces.Single(f => f.Id == command.IssuerId).LeaderId ?? commander;
        Dictionary<string, MessageBinding> values = new()
        {
            ["commander"] = MessageBinding.Reference(MessageEntityKind.Person, commander),
            ["recipient"] = MessageBinding.Reference(MessageEntityKind.Person, recipient),
            ["unit"] = MessageBinding.Reference(MessageEntityKind.Unit, unit.Id)
        };
        if (target is { } selected) values["target"] = selected;
        var definition = content.Military[role];
        var bindings = definition.Bindings.ToDictionary(b => b.Key, b => values[b.Value]);
        return [new(context.Produce(definition.Key, bindings, random), commander, definition.Sound)];
    }
}
