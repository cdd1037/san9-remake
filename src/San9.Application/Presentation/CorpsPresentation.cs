using San9.Content.Text;
using San9.Content.Interface;
using San9.Core.Simulation;
using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>Projects committed corps operations using their result and frozen pre-command names.</summary>
public static class CorpsPresentation
{
    public static string ControlName(TextCatalog texts, CorpsControlMode mode) => CommandText.Resolve(texts, mode switch
    {
        CorpsControlMode.Direct => "corps.direct",
        CorpsControlMode.Automatic => "corps.automatic",
        CorpsControlMode.Cooperative => "corps.cooperative",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    });

    public static IReadOnlyList<CommandMessage> Results(StrategySnapshot before, IReadOnlyList<DomainEvent> events, TextCatalog texts,
        IReadOnlyDictionary<string,DecisionMessageDefinition> content, WorldMessageContext prior, WorldMessageContext committed)
    {
        string Person(int id) => MessageStyles.Open("blue-outline") + texts.Resolve(before.Officers.Single(p => p.Id == id).NameKey) + MessageStyles.Close;
        string Corps(int id) => Person(before.World.Corps.Single(c => c.Id == id).LeaderId!.Value);
        var changed = events.OfType<CorpsChangedEvent>().Select(e => e.CorpsId).ToHashSet();
        List<CommandMessage> messages = [];
        foreach (var result in events)
        {
            CommandMessage? message = result switch
            {
                CorpsCreatedEvent e when e.ForceId == before.PlayerForceId => Produce("corps-created",e.CorpsId,committed),
                CorpsChangedEvent e when e.ForceId == before.PlayerForceId => Produce("corps-changed",e.CorpsId,prior),
                CorpsControlChangedEvent e when e.ForceId == before.PlayerForceId &&
                    !changed.Contains(e.CorpsId) => new(new("corps-result", e.Clock,
                    CommandText.Resolve(texts, "result.corps-control", Corps(e.CorpsId), ControlName(texts, e.Mode))),null,null),
                CorpsDelegationChangedEvent e when e.ForceId == before.PlayerForceId && e.Reason == "ordered" &&
                    !changed.Contains(e.CorpsId) => new(new("corps-result", e.Clock,
                    CommandText.Resolve(texts, "result.corps-policy", Corps(e.CorpsId))),null,null),
                _ => null
            };
            if (message is not null) messages.Add(message);
        }
        return messages;
        CommandMessage Produce(string role, int corps, WorldMessageContext context)
        {
            var definition = content[role];
            var bindings = definition.Bindings.ToDictionary(p => p.Key, p => MessageBinding.Reference(MessageEntityKind.Corps,corps));
            return new(context.Produce(definition.Key,bindings,Random.Shared.Next),null,definition.Sound);
        }
    }
}
