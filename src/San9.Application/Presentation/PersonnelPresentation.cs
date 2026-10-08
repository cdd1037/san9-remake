using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class PersonnelPresentation
{
    private static Dictionary<string, MessageBinding> Values(StrategySnapshot view, int actorId, int placeId)
    {
        var actor = view.World.Officers.Single(p => p.Id == actorId);
        int force = actor.OwnerId!.Value;
        return new()
        {
            ["actor"] = MessageBinding.Reference(MessageEntityKind.Person, actorId),
            ["ruler"] = MessageBinding.Reference(MessageEntityKind.Person, view.World.Forces.Single(f => f.Id == force).LeaderId!.Value),
            ["home"] = Place(view, actor.SettlementId!.Value),
            ["place"] = Place(view, placeId),
            ["force"] = MessageBinding.Reference(MessageEntityKind.Force, force)
        };
    }
    private static MessageBinding Place(StrategySnapshot view, int id) => MessageBinding.Reference(
        view.Settlements.Any(s => s.Id == id) ? MessageEntityKind.Settlement : MessageEntityKind.Region, id);
    private static CommandMessage Message(PersonnelPresentationDefinition content, string role, WorldMessageContext context,
        TextCatalog texts, IReadOnlyDictionary<string, MessageBinding> values, Func<int, int> random)
    {
        var message = content.Messages[role];
        var bindings = MessageGraph.Requirements(message.Key, texts.Messages).Bindings.ToDictionary(slot => slot, slot => values[message.Bindings[slot]]);
        return new(context.Produce(message.Key, bindings, random), message.Speaker is string speaker ? values[speaker].Entity!.Id : null, message.Sound)
            { IllustrationRole = message.Illustration };
    }
    public static IReadOnlyList<CommandMessage> Advice(PersonnelDraft draft, PersonnelPresentationDefinition content,
        WorldMessageContext context, TextCatalog texts, Func<int, int> random)
    {
        if (draft.Advice is not { AdviserId: int adviser, Opinion: AdviserOpinion.Favorable or AdviserOpinion.Unfavorable } advice) return [];
        var values = Values(draft.View, draft.Members[0], draft.Kind == "employ" ? TargetPlace(draft.View, draft.TargetId!.Value) : draft.TargetId!.Value);
        values["adviser"] = MessageBinding.Reference(MessageEntityKind.Person, adviser);
        string role = draft.Kind + (advice.Opinion == AdviserOpinion.Favorable ? "-favorable" : "-unfavorable");
        if (draft.SearchAdvice is ThreeVisitsStep.FirstVisit) role = "search-first-visit";
        if (draft.SearchAdvice is ThreeVisitsStep.Recruitment) role = "search-return-visit";
        return [Message(content, role, context, texts, values, random)];
    }
    private static int TargetPlace(StrategySnapshot view, int targetId)
    {
        var target = view.World.Officers.Single(p => p.Id == targetId);
        return target.UnitId is int unit ? view.RegionAt(view.World.Units.Single(u => u.Id == unit).Position)!.Value :
            (target.SettlementId ?? target.RegionId)!.Value;
    }
    public static IReadOnlyList<CommandMessage> Departure(PersonnelDraft draft, PersonnelPresentationDefinition content,
        WorldMessageContext context, TextCatalog texts, Func<int, int> random)
    {
        int actor = draft.Members[0], target = draft.TargetId!.Value;
        if (draft.Kind is "move" or "summon")
        {
            var rules = new GovernanceRulebook(draft.View.Information.Offices.Select(o => new GovernorOfficeRule(o.Id,o.CommandCapacity,o.GovernorPriority)));
            actor = rules.SelectCommander(draft.View.World.Officers.Where(p => draft.Members.Contains(p.Id)),
                draft.View.Officers.ToDictionary(o => o.Id,o => o.Abilities))!.Id;
        }
        var values = Values(draft.View, actor, draft.Kind == "employ" ? TargetPlace(draft.View, target) : target);
        string role = draft.Kind is "move" or "summon" ? "move-start" : draft.Kind + "-start";
        if (draft.Kind == "employ")
        {
            values["target"] = MessageBinding.Reference(MessageEntityKind.Person, target);
            if (draft.View.World.Officers.Single(p => p.Id == target).Status == OfficerStatus.Captive) role = "captive-start";
        }
        return [Message(content, role, context, texts, values, random)];
    }
    /// <summary>Only direct player missions narrate; search person encounters already belong to DecisionDraft.</summary>
    public static IReadOnlyList<CommandMessage> Results(StrategySnapshot before, WorldMessageContext context, TextCatalog texts,
        PersonnelPresentationDefinition content, IReadOnlyList<DomainEvent> events, Func<int, int> random)
    {
        List<CommandMessage> result = [];
        bool Direct(int actor) => before.World.Simulation.Tasks.Any(t => t.OfficerId == actor && t.Kind is "personnel-search" or "personnel-recruit" &&
            before.World.Simulation.Controls.Any(c => c.ForceId == t.ForceId && c.Kind == ControllerKind.Player) &&
            !before.World.Corps.Any(c => c.Id == before.World.Settlements.Single(s => s.Id == t.OriginSettlementId).CorpsId && c.ControlMode == CorpsControlMode.Automatic));
        foreach (var outcome in events)
        {
            if (outcome is SearchResolvedEvent search && search.Result != SearchResultKind.Person && Direct(search.OfficerId))
            {
                var values = Values(before, search.OfficerId, search.PlaceId);
                values["money"] = new(search.Money);
                if (search.TreasureId is int item) values["treasure"] = MessageBinding.Reference(MessageEntityKind.Treasure, item);
                string role = search.Result switch { SearchResultKind.Treasure => "search-treasure", SearchResultKind.Money => "search-money", _ => "search-none" };
                var message = Message(content, role, context, texts, values, random);
                if (search.TreasureId is int treasure) message = message with { IllustrationRole = $"information-treasure-{treasure}" };
                result.Add(message);
                if (search.Result == SearchResultKind.None) result.Add(Message(content, "search-empty", context, texts, values, random));
            }
            if (outcome is RecruitmentResolvedEvent recruit && Direct(recruit.OfficerId))
            {
                var target = before.World.Officers.Single(p => p.Id == recruit.TargetOfficerId);
                var values = Values(before, recruit.OfficerId, TargetPlace(before, target.Id));
                values["target"] = MessageBinding.Reference(MessageEntityKind.Person, target.Id);
                bool captive = target.Status == OfficerStatus.Captive;
                if (target.OwnerId is int oldForce)
                {
                    values["old-force"] = MessageBinding.Reference(MessageEntityKind.Force, oldForce);
                    if (before.World.Forces.Single(f => f.Id == oldForce).LeaderId is int ruler)
                        values["old-ruler"] = MessageBinding.Reference(MessageEntityKind.Person, ruler);
                }
                result.Add(Message(content, captive || target.Status == OfficerStatus.Free ? "employ-meeting" : "employ-secret-meeting", context, texts, values, random));
                string reply = captive ? recruit.Success ? "captive-success" : target.OwnerId is null ? "captive-free-failure" : "captive-failure" :
                    recruit.Success ? "employ-success" : "employ-failure";
                result.Add(Message(content, reply, context, texts, values, random));
                string resultRole = !recruit.Success ? "employ-result-failure" : captive && target.OwnerId is not null ? "captive-result-success" : "employ-result-success";
                result.Add(Message(content, resultRole, context, texts, values, random));
                foreach (var transfer in events.OfType<PersonnelTerritoryTransferredEvent>().Where(e => e.OfficerId == target.Id))
                {
                    values["place"] = Place(before, transfer.PlaceId);
                    result.Add(Message(content, "employ-territory", context, texts, values, random));
                    if (before.World.Settlements.Single(s => s.Id == transfer.PlaceId).Kind == SettlementKind.City)
                        result.Add(Message(content, "employ-city", context, texts, values, random));
                }
                if (recruit.Success && target.UnitId is int unit && before.World.Units.Single(u => u.Id == unit).CommanderId == target.Id)
                    result.Add(Message(content, "employ-unit", context, texts, values, random));
            }
        }
        return result;
    }
}
