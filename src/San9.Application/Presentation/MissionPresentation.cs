using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class MissionPresentation
{
    private static CommandMessage Produce(string role,IReadOnlyDictionary<string,DecisionMessageDefinition> content,
        IReadOnlyDictionary<string,MessageBinding> values,WorldMessageContext context,TextCatalog texts)
    {
        var message = content[role];
        var bindings = MessageGraph.Requirements(message.Key,texts.Messages).Bindings.ToDictionary(slot => slot,slot => values[message.Bindings[slot]]);
        return new(context.Produce(message.Key,bindings,Random.Shared.Next),message.Speaker is string speaker ? values[speaker].Entity!.Id : null,message.Sound)
            { IllustrationRole = message.Illustration };
    }
    public static IReadOnlyList<CommandMessage> Advice(MissionDraft draft,IReadOnlyDictionary<string,DecisionMessageDefinition> content,WorldMessageContext context,TextCatalog texts)
    {
        if (draft.Advice is not { AdviserId: int adviser, Opinion: AdviserOpinion.Favorable or AdviserOpinion.Unfavorable } advice || content.Count == 0) return [];
        Dictionary<string,MessageBinding> values = new()
        {
            ["adviser"] = MessageBinding.Reference(MessageEntityKind.Person,adviser),
            ["ruler"] = MessageBinding.Reference(MessageEntityKind.Person,draft.View.World.Forces.Single(f => f.Id == draft.View.PlayerForceId).LeaderId!.Value)
        };
        if (draft.TargetId is int force && draft.TargetKind == MissionTargetKind.Force)
            values["target-force"] = MessageBinding.Reference(MessageEntityKind.Force, force);
        return [Produce((draft.IsDiplomacy ? draft.Kind : "strategy")+(advice.Opinion == AdviserOpinion.Favorable ? "-favorable" : "-unfavorable"),content,values,context,texts)];
    }
    public static IReadOnlyList<CommandMessage> Departure(MissionDraft draft,IReadOnlyDictionary<string,DecisionMessageDefinition> content,WorldMessageContext context,TextCatalog texts)
    {
        if (content.Count == 0) return [];
        var view = draft.View;
        int target = draft.TargetId!.Value;
        int? person = draft.TargetKind == MissionTargetKind.Officer ? target : draft.Kind == "return" ? draft.SecondaryId :
            draft.TargetKind == MissionTargetKind.Unit ? view.World.Units.Single(u => u.Id == target).CommanderId : null;
        int? force = draft.TargetKind == MissionTargetKind.Force ? target : draft.TargetKind == MissionTargetKind.Settlement ? view.World.Settlements.Single(s => s.Id == target).OwnerId :
            draft.TargetKind == MissionTargetKind.Unit ? view.World.Units.Single(u => u.Id == target).OwnerId : view.World.Officers.Single(p => p.Id == target).OwnerId;
        int? place = draft.TargetKind == MissionTargetKind.Settlement ? target : draft.Kind == "request" ? draft.SecondaryId : person is int id ?
            view.World.Officers.Single(p => p.Id == id).SettlementId : null;
        var placeKind = MessageEntityKind.Settlement;
        if (place is null && person is int located && view.World.Officers.Single(p => p.Id == located).UnitId is int unit)
        {
            place = view.RegionAt(view.World.Units.Single(u => u.Id == unit).Position);
            placeKind = MessageEntityKind.Region;
        }
        Dictionary<string,MessageBinding> values = new()
        {
            ["actor"] = MessageBinding.Reference(MessageEntityKind.Person,draft.ActorId!.Value),
            ["ruler"] = MessageBinding.Reference(MessageEntityKind.Person,view.World.Forces.Single(f => f.Id == view.PlayerForceId).LeaderId!.Value)
        };
        if (person is int targetPerson) values["target-person"] = MessageBinding.Reference(MessageEntityKind.Person,targetPerson);
        if (force is int targetForce)
        {
            values["target-force"] = MessageBinding.Reference(MessageEntityKind.Force,targetForce);
            if (view.World.Forces.Single(f => f.Id == targetForce).LeaderId is int ruler) values["target-ruler"] = MessageBinding.Reference(MessageEntityKind.Person,ruler);
        }
        if (place is int location) values["place"] = MessageBinding.Reference(placeKind,location);
        if (draft.Kind == "rumor") values["second-force"] = MessageBinding.Reference(MessageEntityKind.Force,draft.SecondaryId!.Value);
        return [Produce(draft.Kind+"-start",content,values,context,texts)];
    }

    /// <summary>Result facts come from this publication and its prior tasks, including invalidation; never inferred from a later world.</summary>
    public static IReadOnlyList<CommandMessage> Results(StrategySnapshot before,TextCatalog texts,IReadOnlyList<DomainEvent> events,
        IReadOnlySet<Guid>? alreadyPresented = null, IReadOnlyDictionary<string,DecisionMessageDefinition>? content = null,
        WorldMessageContext? context = null, IReadOnlyDictionary<string,int>? speakers = null)
    {
        List<CommandMessage> messages = [];
        string Person(int id) => texts.Resolve(before.Officers.Single(p => p.Id == id).NameKey);
        string Place(int id) => before.Settlements.Single(s => s.Id == id).ResolveName(texts);
        string Force(int id) => texts.Resolve(before.Forces.Single(f => f.Id == id).NameKey);
        bool Player(PersistentTask task) => task.ForceId == before.PlayerForceId && task.ControllerId == "player";
        foreach (var e in events)
        {
            int? actor = e switch { AlienateResolvedEvent a => a.OfficerId, BurnResolvedEvent b => b.OfficerId, SeizeResolvedEvent s => s.OfficerId,
                GossipResolvedEvent g => g.OfficerId, UnitStrategyResolvedEvent u => u.OfficerId, GiftResolvedEvent g => g.OfficerId, _ => null };
            Guid? taskId = e switch { GiftResolvedEvent g => g.TaskId, AttackRequestResolvedEvent a => a.TaskId, PrisonerReturnResolvedEvent p => p.TaskId,
                SurrenderResolvedEvent s => s.TaskId, TaskCancelledEvent c => c.TaskId, _ => null };
            if (taskId is Guid presented && alreadyPresented?.Contains(presented) == true && e is not TaskCancelledEvent) continue;
            var clock = e switch { AlienateResolvedEvent a => a.Clock, BurnResolvedEvent b => b.Clock, SeizeResolvedEvent s => s.Clock,
                GossipResolvedEvent g => g.Clock, UnitStrategyResolvedEvent u => u.Clock, GiftResolvedEvent g => g.Clock,
                AttackRequestResolvedEvent a => a.Clock, PrisonerReturnResolvedEvent p => p.Clock, SurrenderResolvedEvent s => s.Clock,
                TaskCancelledEvent c => c.Clock, _ => before.World.Clock };
            var task = before.World.Simulation.Tasks.FirstOrDefault(t => taskId is Guid id ? t.Id == id : actor is int p && t.OfficerId == p && IsMission(t));
            if (task is null || !Player(task) || !IsMission(task)) continue;
            if (content is { Count: > 0 } && context is not null)
                messages.AddRange(OutcomeDialogue(e, task, before, content, context, texts, speakers)
                    .Select(message => message with { Message = message.Message with { Clock = clock },
                        MapSpeech = message.MapSpeech is { } speech ? speech with { Message = speech.Message with { Clock = clock } } : null }));
            var original = content is not null && context is not null ? OriginalResult(e,task,before,content,context,texts) : null;
            void Add(string value, string? detail = null)
            {
                var message = original is null ? new ProducedMessage("mission-result",clock,value) : original.Message with { Clock = clock,
                    BbCode = original.Message.BbCode+(detail is null ? "" : "\n"+detail) };
                var location = ResultLocation(e,before);
                messages.Add(new(message,original?.SpeakerId,original?.SoundRole)
                {
                    IllustrationRole = original?.IllustrationRole,
                    MapRecord = new(Guid.NewGuid(),message,location,before.Forces.Single(f => f.Id == task.ForceId).ColorCode)
                });
            }
            string who = Person(task.OfficerId);
            switch (e)
            {
                case AlienateResolvedEvent a: Add(CommandText.Resolve(texts, "result.alienate", who, Person(a.TargetOfficerId), (a.Success ? CommandText.Resolve(texts, "result.alienate-gain", a.LoyaltyLoss) : CommandText.Resolve(texts, "outcome.failure"))),a.Success ? CommandText.Resolve(texts, "result.loyalty-loss", a.LoyaltyLoss) : null); break;
                case BurnResolvedEvent b: Add(CommandText.Resolve(texts, "result.burn", who, Place(b.TargetSettlementId), (b.Success ? CommandText.Resolve(texts, "result.burn-gain", b.DurabilityLoss, b.FoodLoss) : CommandText.Resolve(texts, "outcome.failure"))),
                    b.Success ? CommandText.Resolve(texts, "result.burn-loss", Place(b.TargetSettlementId), b.DurabilityLoss, b.FoodLoss) : null); break;
                case SeizeResolvedEvent s:
                    string gain = s.Outcome switch { SeizeOutcomeKind.Item => CommandText.Resolve(texts, "result.treasure-gain", texts.Resolve(before.Information.Treasures.Single(t => t.Id == s.TreasureId).NameKey)),
                        SeizeOutcomeKind.Person => CommandText.Resolve(texts, "result.person-gain", Person(s.RecoveredOfficerId!.Value)), SeizeOutcomeKind.Money => CommandText.Resolve(texts, "result.money-gain", s.Money), _ => CommandText.Resolve(texts, "outcome.failure") };
                    Add(CommandText.Resolve(texts, "result.seize", who, Place(s.TargetSettlementId), gain)); break;
                case GossipResolvedEvent g: Add(CommandText.Resolve(texts, "result.rumor", who, Force(g.FirstTargetForceId), (g.Success ? CommandText.Resolve(texts, "result.rumor-gain", Force(g.SecondTargetForceId), g.RelationLoss) : CommandText.Resolve(texts, "outcome.failure"))),
                    g.Success ? CommandText.Resolve(texts, "result.relation-loss", Force(g.SecondTargetForceId), g.RelationLoss) : null); break;
                case UnitStrategyResolvedEvent u:
                    string name = u.Strategy switch { UnitStrategyKind.FalseReport => CommandText.Resolve(texts, "title.false-report"), UnitStrategyKind.Disorder => CommandText.Resolve(texts, "title.disorder"), UnitStrategyKind.Inspire => CommandText.Resolve(texts, "title.inspire"), _ => CommandText.Resolve(texts, "title.rescue") };
                    string recipient = u.Target.CommanderId is int commander ? Person(commander) : Force(u.Target.ForceId);
                    string outcome = CommandText.Resolve(texts, u.Success ? "outcome.success" : "outcome.failure");
                    Add(u.Strategy == UnitStrategyKind.Reinforce
                        ? CommandText.Resolve(texts, "result.reinforcement-summary", who, recipient, name, outcome, u.DeliveredSoldiers, u.RequestedSoldiers)
                        : CommandText.Resolve(texts, "result.unit-strategy", who, recipient, name, outcome),
                        u.Strategy == UnitStrategyKind.Reinforce ? CommandText.Resolve(texts, "result.reinforcement", u.DeliveredSoldiers, u.RequestedSoldiers) : null); break;
                case GiftResolvedEvent g: Add(CommandText.Resolve(texts, "result.gift", Force(g.TargetForceId), (g.Accepted ? CommandText.Resolve(texts, "outcome.accepted") : CommandText.Resolve(texts, "outcome.refused")), who)+(g.Refunded > 0 ? CommandText.Resolve(texts, "result.refunded", g.Refunded) : ""),
                    g.Refunded > 0 ? CommandText.Resolve(texts, "result.refund-resource", (g.Offer.Kind == GiftKind.Food ? CommandText.Resolve(texts, "label.food") : CommandText.Resolve(texts, "label.money")), g.Refunded) : null); break;
                case AttackRequestResolvedEvent a: Add(CommandText.Resolve(texts, "result.request", Force(a.RequestedForceId), Place(a.TargetSettlementId), a.Outcome switch { AttackRequestOutcome.Accepted => CommandText.Resolve(texts, "outcome.accepted"),AttackRequestOutcome.AlreadyAgreed => CommandText.Resolve(texts, "outcome.agreed"),_ => CommandText.Resolve(texts, "outcome.refused")})); break;
                case PrisonerReturnResolvedEvent p: Add(CommandText.Resolve(texts, "result.prisoner-return", Person(p.PrisonerId), (p.Accepted ? CommandText.Resolve(texts, "outcome.success") : CommandText.Resolve(texts, "outcome.rejected")))); break;
                case SurrenderResolvedEvent s:
                    Add(CommandText.Resolve(texts, "result.surrender", Force(s.TargetForceId), (s.Accepted ? CommandText.Resolve(texts, "outcome.accepted") : CommandText.Resolve(texts, "outcome.refused"))));
                    if (s.Accepted && content is { Count: > 0 } && context is not null)
                    {
                        var values = ResultValues(e,task,before);
                        values["year"] = new(s.Clock.Year); values["month"] = new(s.Clock.Month);
                        var elimination = Produce("surrender-eliminated",content,values,context,texts);
                        messages.Add(elimination with { Message = elimination.Message with { Clock = clock }, BackgroundMusicRole = "force-eliminated-music" });
                    }
                    break;
                case TaskCancelledEvent c: Add(CommandText.Resolve(texts, "result.cancelled", who, Cancellation(texts,c.Reason))); break;
            }
        }
        return messages;
    }
    private static CommandMessage? OriginalResult(DomainEvent e,PersistentTask task,StrategySnapshot before,
        IReadOnlyDictionary<string,DecisionMessageDefinition> content,WorldMessageContext context,TextCatalog texts)
    {
        string? role = e switch
        {
            AlienateResolvedEvent a => a.Success ? "alienate-success" : "alienate-failure",
            BurnResolvedEvent b => b.Success ? "burn-success" : "burn-failure",
            SeizeResolvedEvent s => s.Outcome switch { SeizeOutcomeKind.Person => "seize-person",
                SeizeOutcomeKind.Failure => "seize-failure", _ => null },
            GossipResolvedEvent g => g.Success ? "rumor-success" : "rumor-failure",
            UnitStrategyResolvedEvent u => (u.Strategy switch { UnitStrategyKind.FalseReport => "false-report", UnitStrategyKind.Disorder => "disorder",
                UnitStrategyKind.Inspire => "inspire", _ => "rescue" })+(u.Success ? "-success" : "-failure"),
            GiftResolvedEvent g => g.Accepted ? "gift-success" : "gift-failure",
            AttackRequestResolvedEvent a => a.Outcome switch { AttackRequestOutcome.Accepted => "request-success", AttackRequestOutcome.Refused => "request-failure", _ => null },
            PrisonerReturnResolvedEvent p => p.Accepted ? "return-success" : "return-failure",
            SurrenderResolvedEvent s => s.Accepted ? "surrender-success" : "surrender-failure", _ => null
        };
        if (role is null || content.Count == 0) return null;
        var values = ResultValues(e, task, before);
        // A previous publication may precede a holder change; do not fabricate missing historical identities.
        if (MessageGraph.Requirements(content[role].Key,texts.Messages).Bindings.Any(slot => !values.ContainsKey(content[role].Bindings[slot]))) return null;
        return Produce(role,content,values,context,texts);
    }
    private static Dictionary<string,MessageBinding> ResultValues(DomainEvent e, PersistentTask task, StrategySnapshot before)
    {
        int? person = e switch { AlienateResolvedEvent a => a.TargetOfficerId, UnitStrategyResolvedEvent u => u.Target.CommanderId,
            PrisonerReturnResolvedEvent p => p.PrisonerId, SeizeResolvedEvent s => s.RecoveredOfficerId, _ => null };
        int? place = e switch { BurnResolvedEvent b => b.TargetSettlementId, SeizeResolvedEvent s => s.TargetSettlementId, AttackRequestResolvedEvent a => a.TargetSettlementId, _ => null };
        int? force = e switch { GossipResolvedEvent g => g.FirstTargetForceId, UnitStrategyResolvedEvent u => u.Target.ForceId,
            BurnResolvedEvent b => before.World.Settlements.Single(s => s.Id == b.TargetSettlementId).OwnerId,
            SeizeResolvedEvent s => before.World.Settlements.Single(t => t.Id == s.TargetSettlementId).OwnerId,
            GiftResolvedEvent g => g.TargetForceId, AttackRequestResolvedEvent a => a.RequestedForceId, PrisonerReturnResolvedEvent => task.PrisonerReturn!.TargetForceId,
            SurrenderResolvedEvent s => s.TargetForceId, _ => null };
        Dictionary<string,MessageBinding> values = new()
        { ["actor"] = MessageBinding.Reference(MessageEntityKind.Person,task.OfficerId), ["force"] = MessageBinding.Reference(MessageEntityKind.Force,task.ForceId) };
        if (before.World.Forces.Single(f => f.Id == task.ForceId).LeaderId is int leader)
            values["ruler"] = MessageBinding.Reference(MessageEntityKind.Person, leader);
        if (person is int targetPerson) values["target-person"] = MessageBinding.Reference(MessageEntityKind.Person,targetPerson);
        if (e is UnitStrategyResolvedEvent unitResult && unitResult.Target.ServicePlaceId is int home)
            values["home"] = MessageBinding.Reference(MessageEntityKind.Settlement,home);
        if (place is int location) values["place"] = MessageBinding.Reference(MessageEntityKind.Settlement,location);
        if (e is AttackRequestResolvedEvent attack && before.World.Settlements.Single(s => s.Id == attack.TargetSettlementId).OwnerId is int enemy)
            values["enemy-force"] = MessageBinding.Reference(MessageEntityKind.Force,enemy);
        if (force is int f)
        {
            values["target-force"] = MessageBinding.Reference(MessageEntityKind.Force,f);
            if (before.World.Forces.Single(t => t.Id == f).LeaderId is int ruler) values["target-ruler"] = MessageBinding.Reference(MessageEntityKind.Person,ruler);
            if (before.World.Forces.Single(t => t.Id == f).AdviserId is int adviser) values["target-adviser"] = MessageBinding.Reference(MessageEntityKind.Person,adviser);
        }
        return values;
    }
    private static IReadOnlyList<CommandMessage> OutcomeDialogue(DomainEvent e, PersistentTask task, StrategySnapshot before,
        IReadOnlyDictionary<string,DecisionMessageDefinition> content, WorldMessageContext context, TextCatalog texts,
        IReadOnlyDictionary<string,int>? speakers)
    {
        var values = ResultValues(e, task, before);
        if (speakers?.TryGetValue("soldier", out int soldier) == true)
            values["soldier"] = MessageBinding.Reference(MessageEntityKind.Person,soldier);
        int? place = e switch { BurnResolvedEvent b => b.TargetSettlementId, SeizeResolvedEvent s => s.TargetSettlementId, _ => null };
        if (place is int target)
        {
            var defender = before.World.Officers.FirstOrDefault(p => p.IsCommander && p.SettlementId == target && p.UnitId is null);
            if (defender is not null) values["defender"] = MessageBinding.Reference(MessageEntityKind.Person,defender.Id);
            else if (values.TryGetValue("soldier",out var fallback)) values["defender"] = fallback;
        }
        List<CommandMessage> sequence = [];
        void Add(string role)
        {
            var definition = content[role];
            var required = MessageGraph.Requirements(definition.Key,texts.Messages).Bindings;
            if (required.Any(slot => !values.ContainsKey(definition.Bindings[slot])) ||
                definition.Speaker is string speaker && !values.ContainsKey(speaker)) return;
            var message = Produce(role,content,values,context,texts);
            if (e is UnitStrategyResolvedEvent or BurnResolvedEvent or SeizeResolvedEvent && message.SpeakerId is int person)
            {
                int force = role.EndsWith("-action",StringComparison.Ordinal) ? task.ForceId : e switch
                {
                    UnitStrategyResolvedEvent unit => unit.Target.ForceId,
                    BurnResolvedEvent burn => before.World.Settlements.Single(s => s.Id == burn.TargetSettlementId).OwnerId!.Value,
                    SeizeResolvedEvent seize => before.World.Settlements.Single(s => s.Id == seize.TargetSettlementId).OwnerId!.Value,
                    _ => throw new InvalidOperationException()
                };
                var speech = new MapSpeechMessage(Guid.NewGuid(), message.Message, person,
                    texts.Resolve(before.MessageSpeaker(person).NameKey),ResultLocation(e,before)!.Value,
                    e is UnitStrategyResolvedEvent target ? target.TargetUnitId : null,
                    before.Forces.Single(f => f.Id == force).ColorCode,
                    DelaySeconds: role.EndsWith("-action",StringComparison.Ordinal) ? 0 : 1);
                message = message with { MapSpeech = speech };
            }
            sequence.Add(message);
        }
        switch (e)
        {
            case GiftResolvedEvent gift:
                Diplomacy("gift",gift.Accepted); break;
            case SurrenderResolvedEvent surrender:
                Diplomacy("surrender",surrender.Accepted);
                Add("surrender-actor-"+(surrender.Accepted ? "accepted" : "refused")); break;
            case AttackRequestResolvedEvent:
                Meeting(); Add(values.ContainsKey("enemy-force") ? "request-proposal" : "request-unowned-proposal");
                if (values.ContainsKey("target-adviser")) Add("diplomacy-host-adviser"); break;
            case PrisonerReturnResolvedEvent:
                Meeting(); Add("return-proposal");
                if (values.ContainsKey("target-adviser")) Add("diplomacy-host-adviser"); break;
            case UnitStrategyResolvedEvent unit:
                string kind = unit.Strategy switch { UnitStrategyKind.FalseReport => "false-report", UnitStrategyKind.Disorder => "disorder",
                    UnitStrategyKind.Inspire => "inspire", _ => "rescue" };
                Add(kind+"-action");
                string reaction = unit.Success ? kind+"-reaction-success" :
                    unit.Strategy is UnitStrategyKind.FalseReport or UnitStrategyKind.Disorder ?
                        unit.Target.Condition == UnitCondition.HighSpirits ? "strategy-reaction-united" :
                        unit.Target.HasCounterConfusion ? "strategy-reaction-countered" :
                        unit.Target.DefensiveFormation ? kind+"-reaction-defensive" : kind+"-reaction-failure" : kind+"-reaction-failure";
                Add(reaction); break;
            case BurnResolvedEvent burn:
                Add("burn-action"); Add("burn-reaction-"+(burn.Success ? "success" : "failure")); break;
            case SeizeResolvedEvent seize when seize.Outcome == SeizeOutcomeKind.Person:
                Add("seize-person-action"); Add("seize-person-reaction"); break;
            case SeizeResolvedEvent seize when seize.Outcome is SeizeOutcomeKind.Money or SeizeOutcomeKind.Failure:
                Add("seize-action"); Add("seize-reaction-"+(seize.Outcome == SeizeOutcomeKind.Failure ? "failure" : "success")); break;
        }
        return e is GiftResolvedEvent or SurrenderResolvedEvent or AttackRequestResolvedEvent or PrisonerReturnResolvedEvent
            ? sequence.Select((m,index) => index == 0 ? m : m with { BackgroundMusicRole = "diplomacy-music" }).ToArray() : sequence;
        void Meeting() { Add("diplomacy-arrival"); Add("diplomacy-greeting"); Add("diplomacy-welcome"); }
        void Diplomacy(string kind, bool accepted)
        {
            Meeting(); Add(kind+"-proposal");
            if (values.ContainsKey("target-adviser")) Add(kind+"-host-"+(accepted ? "favorable" : "unfavorable"));
            Add(kind+"-"+(accepted ? "accepted" : "refused"));
        }
    }
    private static bool IsMission(PersistentTask task) => task.Journey is { ReturnLeg: null } &&
        (task.Kind.StartsWith("strategy-",StringComparison.Ordinal) || task.Kind == "unit-strategy" || task.Kind.StartsWith("diplomacy-",StringComparison.Ordinal));

    private static MapCell? ResultLocation(DomainEvent outcome,StrategySnapshot before)
    {
        MapCell Cell(int id)
        {
            var settlement = before.Settlements.Single(s => s.Id == id);
            return new(settlement.X,settlement.Y);
        }
        return outcome switch
        {
            UnitStrategyResolvedEvent unit => MapCoordinates.Cell(unit.Target.Position),
            BurnResolvedEvent burn => Cell(burn.TargetSettlementId),
            SeizeResolvedEvent seize => Cell(seize.TargetSettlementId),
            _ => null
        };
    }
    private static string Cancellation(TextCatalog texts, string reason) => reason switch
    {
        "reinforcement-unfit" => CommandText.Resolve(texts, "cancel.reinforcement-unfit"), "target-invalidated" => CommandText.Resolve(texts, "cancel.target-invalidated"),
        _ => CommandText.Resolve(texts, "cancel.conditions")
    };
}
