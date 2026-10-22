using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class MissionPresentation
{
    private static CommandMessage Produce(string role,IReadOnlyDictionary<string,DecisionMessageDefinition> content,
        IReadOnlyDictionary<string, MessageBinding> values, WorldMessageContext context, TextCatalog texts, Func<int, int>? random = null) =>
        ProgressMessageProducer.Produce(content[role], context, texts, values, random ?? Random.Shared.Next)
            with { IllustrationRole = content[role].Illustration };
    public static IReadOnlyList<CommandMessage> Advice(MissionDraft draft,IReadOnlyDictionary<string,DecisionMessageDefinition> content,WorldMessageContext context,TextCatalog texts)
    {
        if (draft.Advice is not { AdviserId: int adviser, Opinion: AdviserOpinion.Favorable or AdviserOpinion.Unfavorable or AdviserOpinion.Unavailable or AdviserOpinion.Uncertain } advice || content.Count == 0) return [];
        Dictionary<string,MessageBinding> values = new()
        {
            ["adviser"] = MessageBinding.Reference(MessageEntityKind.Person,adviser),
            ["ruler"] = MessageBinding.Reference(MessageEntityKind.Person,draft.View.World.Forces.Single(f => f.Id == draft.View.PlayerForceId).LeaderId!.Value)
        };
        if (draft.TargetId is int force && draft.TargetKind == MissionTargetKind.Force)
            values["target-force"] = MessageBinding.Reference(MessageEntityKind.Force, force);
        string role = advice.Opinion == AdviserOpinion.Unavailable ? "diplomacy-unpredictable" :
            advice.Opinion == AdviserOpinion.Uncertain ? "request-uncertain" :
            (draft.IsDiplomacy ? draft.Kind : "strategy") + (advice.Opinion == AdviserOpinion.Favorable ? "-favorable" : "-unfavorable");
        return [Produce(role,content,values,context,texts)];
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
    public static ExecutionOutput Results(StrategySnapshot before,TextCatalog texts,IReadOnlyList<DomainEvent> events,
        IReadOnlySet<Guid>? alreadyPresented = null, IReadOnlyDictionary<string,DecisionMessageDefinition>? content = null,
        WorldMessageContext? context = null, IReadOnlyDictionary<string,int>? speakers = null)
    {
        List<CommandMessage> messages = [];
        List<MapMessageRecord> records = [];
        string Person(int id) => texts.Resolve(before.Officers.Single(p => p.Id == id).NameKey);
        string Place(int id) => before.Settlements.Single(s => s.Id == id).ResolveName(texts);
        string Force(int id) => texts.Resolve(before.Forces.Single(f => f.Id == id).NameKey);
        bool Player(PersistentTask task) => before.World.Simulation.Controls.Any(c => c.ForceId == task.ForceId && c.Kind == ControllerKind.Player);
        foreach (var e in events)
        {
            // Only mission results enter this projection; identity, occurrence and authored role travel together.
            (int? Actor, Guid? TaskId, GameClock Clock, string? Role) outcome = e switch
            {
                AlienateResolvedEvent a => (a.OfficerId, null, a.Clock, a.Success ? "alienate-success" : "alienate-failure"),
                BurnResolvedEvent b => (b.OfficerId, null, b.Clock, b.Success ? "burn-success" : "burn-failure"),
                SeizeResolvedEvent s => (s.OfficerId, null, s.Clock, s.Outcome switch
                {
                    SeizeOutcomeKind.Person => "seize-person", SeizeOutcomeKind.Item => "seize-item",
                    SeizeOutcomeKind.Failure => "seize-failure", _ => null
                }),
                GossipResolvedEvent g => (g.OfficerId, null, g.Clock, g.Success ? "rumor-success" : "rumor-failure"),
                UnitStrategyResolvedEvent u => (u.OfficerId, null, u.Clock, StrategyRole(u.Strategy) + (u.Success ? "-success" : "-failure")),
                GiftResolvedEvent g => (null, g.TaskId, g.Clock, g.Accepted ? "gift-success" : "gift-failure"),
                AttackRequestResolvedEvent a => (null, a.TaskId, a.Clock, a.Outcome switch
                {
                    AttackRequestOutcome.Accepted => "request-success", AttackRequestOutcome.Refused => "request-failure",
                    AttackRequestOutcome.AlreadyAgreed => "request-already-agreed", _ => null
                }),
                PrisonerReturnResolvedEvent p => (null, p.TaskId, p.Clock, p.Accepted ? "return-success" : "return-failure"),
                SurrenderResolvedEvent s => (null, s.TaskId, s.Clock, s.Accepted ? "surrender-success" : "surrender-failure"),
                TaskCancelledEvent c => (null, c.TaskId, c.Clock, null),
                _ => (null, null, default, null)
            };
            var (actor, taskId, clock, role) = outcome;
            if (actor is null && taskId is null) continue;
            var task = before.World.Simulation.Tasks.FirstOrDefault(t => taskId is Guid id ? t.Id == id : t.OfficerId == actor && IsMission(t));
            if (task is null || !Player(task) || !IsMission(task)) continue;
            bool narrated = alreadyPresented?.Contains(task.Id) == true && e is not TaskCancelledEvent;
            var occurrence = context?.AtOccurrence(clock);
            var values = content is { Count: > 0 } && occurrence is not null ? ResultValues(e,task,before) : null;
            if (values is not null && e is AttackRequestResolvedEvent { Outcome: AttackRequestOutcome.AlreadyAgreed })
                values["command-name"] = new(0, CommandText.Resolve(texts, "title.request"));
            var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"mission:{task.Id}:{clock.DayNumber}"));
            int Pick(int count) => (int)(random.NextUInt64() % (ulong)count);
            // Resolve the record first so dialogue suppression cannot change its random text selection.
            var original = role is not null && values is not null && content is not null && occurrence is not null
                ? ResultMessage(role, content, values, occurrence, texts, Pick) : null;
            string who = Person(task.OfficerId);
            string Outcome(bool success) => CommandText.Resolve(texts, success ? "outcome.success" : "outcome.failure");
            string summary = original?.Message.BbCode ?? e switch
            {
                AlienateResolvedEvent a => CommandText.Resolve(texts, "result.alienate", who, Person(a.TargetOfficerId),
                    a.Success ? CommandText.Resolve(texts, "result.alienate-gain", a.LoyaltyLoss) : Outcome(false)),
                BurnResolvedEvent b => CommandText.Resolve(texts, "result.burn", who, Place(b.TargetSettlementId),
                    b.Success ? CommandText.Resolve(texts, "result.burn-gain", b.DurabilityLoss, b.FoodLoss) : Outcome(false)),
                SeizeResolvedEvent s => CommandText.Resolve(texts, "result.seize", who, Place(s.TargetSettlementId), s.Outcome switch
                {
                    SeizeOutcomeKind.Item => CommandText.Resolve(texts, "result.treasure-gain", texts.Resolve(before.Information.Treasures.Single(t => t.Id == s.TreasureId).NameKey)),
                    SeizeOutcomeKind.Person => CommandText.Resolve(texts, "result.person-gain", Person(s.RecoveredOfficerId!.Value)),
                    SeizeOutcomeKind.Money => CommandText.Resolve(texts, "result.money-gain", s.Money), _ => Outcome(false)
                }),
                GossipResolvedEvent g => CommandText.Resolve(texts, "result.rumor", who, Force(g.FirstTargetForceId),
                    g.Success ? CommandText.Resolve(texts, "result.rumor-gain", Force(g.SecondTargetForceId), g.RelationLoss) : Outcome(false)),
                UnitStrategyResolvedEvent u => UnitSummary(u),
                GiftResolvedEvent g => CommandText.Resolve(texts, "result.gift", Force(g.TargetForceId),
                    CommandText.Resolve(texts, g.Accepted ? "outcome.accepted" : "outcome.refused"), who) +
                    (g.Refunded > 0 ? CommandText.Resolve(texts, "result.refunded", g.Refunded) : ""),
                AttackRequestResolvedEvent a => CommandText.Resolve(texts, "result.request", Force(a.RequestedForceId), Place(a.TargetSettlementId),
                    CommandText.Resolve(texts, a.Outcome switch { AttackRequestOutcome.Accepted => "outcome.accepted", AttackRequestOutcome.AlreadyAgreed => "outcome.agreed", _ => "outcome.refused" })),
                PrisonerReturnResolvedEvent p => CommandText.Resolve(texts, "result.prisoner-return", Person(p.PrisonerId),
                    CommandText.Resolve(texts, p.Accepted ? "outcome.success" : "outcome.rejected")),
                SurrenderResolvedEvent s => CommandText.Resolve(texts, "result.surrender", Force(s.TargetForceId),
                    CommandText.Resolve(texts, s.Accepted ? "outcome.accepted" : "outcome.refused")),
                TaskCancelledEvent c => CommandText.Resolve(texts, "result.cancelled", who, Cancellation(texts,c.Reason)),
                _ => throw new InvalidOperationException("Unknown mission result.")
            };
            string UnitSummary(UnitStrategyResolvedEvent u)
            {
                object[] arguments = [who, u.Target.CommanderId is int commander ? Person(commander) : Force(u.Target.ForceId),
                    CommandText.Resolve(texts, "title." + StrategyRole(u.Strategy)), Outcome(u.Success)];
                return u.Strategy == UnitStrategyKind.Reinforce
                    ? CommandText.Resolve(texts, "result.reinforcement-summary", [..arguments, u.DeliveredSoldiers, u.RequestedSoldiers])
                    : CommandText.Resolve(texts, "result.unit-strategy", arguments);
            }
            string? detail = original is null ? null : e switch
            {
                GossipResolvedEvent { Success: true } g => CommandText.Resolve(texts, "result.relation-loss", Force(g.SecondTargetForceId), g.RelationLoss),
                UnitStrategyResolvedEvent { Strategy: UnitStrategyKind.Reinforce } u => CommandText.Resolve(texts, "result.reinforcement", u.DeliveredSoldiers, u.RequestedSoldiers),
                GiftResolvedEvent { Refunded: > 0 } g => CommandText.Resolve(texts, "result.refund-resource",
                    CommandText.Resolve(texts, g.Offer.Kind == GiftKind.Food ? "label.food" : "label.money"), g.Refunded),
                _ => null
            };
            var message = new ProducedMessage(original?.Message.Key ?? "mission-result", clock, summary + (detail is null ? "" : "\n" + detail));
            records.Add(new(ResultIdentity(before, task, e, "record"), message, ResultLocation(e,before), MapRecordColors.Resolve(before, before, task.ForceId)));
            if (narrated || values is null || content is null || occurrence is null) continue;
            messages.AddRange(OutcomeDialogue(e, task, before, content, values, occurrence, texts, speakers, Pick));
            if (original is { SoundRole: not null } or { IllustrationRole: not null } ||
                original is not null && e is AttackRequestResolvedEvent { Outcome: AttackRequestOutcome.AlreadyAgreed })
                messages.Add(original with { Message = message });
            if (e is SurrenderResolvedEvent { Accepted: true } surrender)
            {
                values["year"] = new(surrender.Clock.Year); values["month"] = new(surrender.Clock.Month);
                messages.Add(Produce("surrender-eliminated",content,values,occurrence,texts,Pick)
                    with { BackgroundMusicRole = "force-eliminated-music" });
            }
        }
        return new(messages, records);
    }
    private static string StrategyRole(UnitStrategyKind kind) => kind switch
    {
        UnitStrategyKind.FalseReport => "false-report", UnitStrategyKind.Disorder => "disorder",
        UnitStrategyKind.Inspire => "inspire", UnitStrategyKind.Reinforce => "rescue",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static string ResultIdentity(StrategySnapshot before, PersistentTask task, DomainEvent fact, string role) =>
        $"mission:{before.World.Revision}:{task.Id}:{fact.GetType().Name}:{role}";

    private static CommandMessage? ResultMessage(string role, IReadOnlyDictionary<string, DecisionMessageDefinition> content,
        IReadOnlyDictionary<string, MessageBinding> values, WorldMessageContext context, TextCatalog texts, Func<int, int> random)
    {
        // A previous publication may precede a holder change; do not fabricate missing historical identities.
        var definition = content[role];
        if (MessageGraph.Requirements(definition.Key,texts.Messages).Bindings.Any(slot => !values.ContainsKey(definition.Bindings[slot])) ||
            definition.Speaker is string speaker && !values.ContainsKey(speaker)) return null;
        return Produce(role, content, values, context, texts, random);
    }
    private static Dictionary<string,MessageBinding> ResultValues(DomainEvent e, PersistentTask task, StrategySnapshot before)
    {
        int? person = e switch
        {
            AlienateResolvedEvent a => a.TargetOfficerId,
            UnitStrategyResolvedEvent u => u.Target.CommanderId,
            PrisonerReturnResolvedEvent p => p.PrisonerId,
            SeizeResolvedEvent s => s.RecoveredOfficerId,
            _ => null
        };
        int? place = e switch { BurnResolvedEvent b => b.TargetSettlementId, SeizeResolvedEvent s => s.TargetSettlementId, AttackRequestResolvedEvent a => a.TargetSettlementId, _ => null };
        int? force = e switch
        {
            GossipResolvedEvent g => g.FirstTargetForceId,
            UnitStrategyResolvedEvent u => u.Target.ForceId,
            BurnResolvedEvent b => before.World.Settlements.Single(s => s.Id == b.TargetSettlementId).OwnerId,
            SeizeResolvedEvent s => before.World.Settlements.Single(t => t.Id == s.TargetSettlementId).OwnerId,
            GiftResolvedEvent g => g.TargetForceId,
            AttackRequestResolvedEvent a => a.RequestedForceId,
            PrisonerReturnResolvedEvent => task.PrisonerReturn!.TargetForceId,
            SurrenderResolvedEvent s => s.TargetForceId,
            _ => null
        };
        Dictionary<string,MessageBinding> values = new()
        { ["actor"] = MessageBinding.Reference(MessageEntityKind.Person,task.OfficerId), ["force"] = MessageBinding.Reference(MessageEntityKind.Force,task.ForceId) };
        if (before.World.Forces.Single(f => f.Id == task.ForceId).LeaderId is int leader)
            values["ruler"] = MessageBinding.Reference(MessageEntityKind.Person, leader);
        if (person is int targetPerson) values["target-person"] = MessageBinding.Reference(MessageEntityKind.Person,targetPerson);
        if (e is AttackRequestResolvedEvent { Outcome: AttackRequestOutcome.AlreadyAgreed })
            values["home"] = MessageBinding.Reference(MessageEntityKind.Settlement, task.OriginSettlementId);
        if (e is UnitStrategyResolvedEvent unitResult && unitResult.Target.ServicePlaceId is int home)
            values["home"] = MessageBinding.Reference(MessageEntityKind.Settlement,home);
        if (e is SeizeResolvedEvent seized)
        {
            if (seized.PreviousHolderId is int holder) values["previous-holder"] = MessageBinding.Reference(MessageEntityKind.Person, holder);
            if (seized.TreasureId is int treasure) values["treasure"] = MessageBinding.Reference(MessageEntityKind.Treasure, treasure);
        }
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
        IReadOnlyDictionary<string,DecisionMessageDefinition> content, Dictionary<string,MessageBinding> values, WorldMessageContext context, TextCatalog texts,
        IReadOnlyDictionary<string, int>? speakers, Func<int, int> random)
    {
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
            if (ResultMessage(role, content, values, context, texts, random) is not { } message) return;
            if (e is UnitStrategyResolvedEvent or BurnResolvedEvent or SeizeResolvedEvent && message.SpeakerId is int person)
            {
                int force = role.EndsWith("-action",StringComparison.Ordinal) ? task.ForceId : e switch
                {
                    UnitStrategyResolvedEvent unit => unit.Target.ForceId,
                    BurnResolvedEvent burn => before.World.Settlements.Single(s => s.Id == burn.TargetSettlementId).OwnerId!.Value,
                    SeizeResolvedEvent seize => before.World.Settlements.Single(s => s.Id == seize.TargetSettlementId).OwnerId!.Value,
                    _ => throw new InvalidOperationException()
                };
                var speech = new MapSpeechMessage(ResultIdentity(before, task, e, $"speech:{role}"), message.Message, person,
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
                Diplomacy("gift",gift.Accepted); Add("gift-actor-" + (gift.Accepted ? "accepted" : "refused")); break;
            case SurrenderResolvedEvent surrender:
                Diplomacy("surrender",surrender.Accepted);
                Add("surrender-actor-"+(surrender.Accepted ? "accepted" : "refused")); break;
            case AttackRequestResolvedEvent request:
                if (request.Outcome == AttackRequestOutcome.AlreadyAgreed)
                { Add("request-already-agreed-actor"); return sequence; }
                Meeting(); Add(values.ContainsKey("enemy-force") ? "request-proposal" : "request-unowned-proposal");
                if (ReceivingPlayer() && values.ContainsKey("target-adviser")) Add("diplomacy-host-adviser");
                if (request.Offer is { } terms) Negotiation("attack-request", terms, request.Outcome == AttackRequestOutcome.Accepted);
                break;
            case PrisonerReturnResolvedEvent prisoner:
                Meeting(); Add("return-proposal");
                if (ReceivingPlayer() && values.ContainsKey("target-adviser")) Add("diplomacy-host-adviser");
                Negotiation("prisoner-return", prisoner.Offer, prisoner.Accepted); break;
            case UnitStrategyResolvedEvent unit:
                string kind = StrategyRole(unit.Strategy);
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
            case SeizeResolvedEvent seize when seize.Outcome == SeizeOutcomeKind.Item:
                Add("seize-item-action"); Add("seize-item-reaction"); break;
            case SeizeResolvedEvent seize when seize.Outcome is SeizeOutcomeKind.Money or SeizeOutcomeKind.Failure:
                Add("seize-action"); Add("seize-reaction-"+(seize.Outcome == SeizeOutcomeKind.Failure ? "failure" : "success")); break;
        }
        if (e is not (GiftResolvedEvent or SurrenderResolvedEvent or AttackRequestResolvedEvent or PrisonerReturnResolvedEvent)) return sequence;
        int host = values["target-force"].Entity!.Id;
        var meeting = new DiplomacyMeeting(task.Id, host, task.OfficerId);
        bool farewell = e is GiftResolvedEvent { Accepted: true } or SurrenderResolvedEvent { Accepted: true } or
            AttackRequestResolvedEvent { Outcome: AttackRequestOutcome.Accepted } or PrisonerReturnResolvedEvent { Accepted: true };
        return sequence.Select((m,index) => index == 0 ? m : m with
        {
            Diplomacy = meeting, DiplomacyFarewell = farewell,
            BackgroundMusicRole = "diplomacy-music", BackgroundMusicLoop = true
        }).ToArray();
        bool ReceivingPlayer() => before.World.Simulation.Controls.Any(c => c.ForceId == values["target-force"].Entity!.Id && c.Kind == ControllerKind.Player);
        void Meeting()
        {
            values["host"] = values["target-ruler"]; values["requester-ruler"] = values["ruler"];
            values["requester-force"] = values["force"];
            if (values.TryGetValue("target-adviser", out var adviser)) values["adviser"] = adviser;
            Add("diplomacy-arrival"); Add("diplomacy-greeting"); Add("diplomacy-welcome");
        }
        void Negotiation(string role, CompensationOffer offer, bool accepted)
        {
            if (values.TryGetValue("target-person", out var person)) values["other"] = person;
            DiplomacyNegotiationMessages.BindOffer(values, offer);
            Add(DiplomacyNegotiationMessages.Quote(role, offer));
            Add(DiplomacyNegotiationMessages.Reply(role, offer, accepted));
        }
        void Diplomacy(string kind, bool accepted)
        {
            Meeting(); Add(kind+"-proposal");
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
        "reinforcement-unfit" => CommandText.Resolve(texts, "cancel.reinforcement-unfit"),
        "target-invalidated" => CommandText.Resolve(texts, "cancel.target-invalidated"),
        _ => CommandText.Resolve(texts, "cancel.conditions")
    };
}
