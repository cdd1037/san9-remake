using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class ExecutionPresentation
{
    public static IReadOnlyList<CommandMessage> Create(WorldMessageContext context,
        ExecutionPresentationDefinition definition, PrisonerExecutedEvent result, Func<int,int> random)
    {
        int ruler = result.Occurrence.ExecutingRulerId;
        var announcement = new Dictionary<string,MessageBinding> {
            ["person.0"] = MessageBinding.Reference(MessageEntityKind.Person,ruler),
            ["person.1"] = MessageBinding.Reference(MessageEntityKind.Person,result.OfficerId) };
        var lastWords = new Dictionary<string,MessageBinding> {
            ["person.0"] = MessageBinding.Reference(MessageEntityKind.Person,result.OfficerId),
            ["person.1"] = MessageBinding.Reference(MessageEntityKind.Person,ruler) };
        var outcome = new Dictionary<string,MessageBinding> {
            ["person.0"] = MessageBinding.Reference(MessageEntityKind.Person,result.OfficerId),
            ["force.0"] = MessageBinding.Reference(MessageEntityKind.Force,result.ExecutingForceId) };
        // Preserve identity, allegiance and family conditions before death cleanup.
        return [new(context.Produce(definition.Announcement,announcement,random),definition.MessengerId,null) { Speaker=context.Speaker(definition.MessengerId) },
            new(context.Produce(definition.LastWords,lastWords,random),result.OfficerId,null) { Speaker=context.Speaker(result.OfficerId) },
            new(context.Produce(definition.Result,outcome,random),null,definition.ResultSound)];
    }
}
