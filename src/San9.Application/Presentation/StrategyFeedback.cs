using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Strategy observers are selected by participant roles, independently of task ownership.</summary>
internal static class StrategyFeedback
{
    internal static ExecutionOutput Read(StrategySnapshot before, StrategySnapshot after, WorldMessageContext context,
        TextCatalog texts, CommandPresentation content, DomainEvent fact, int index)
    {
        (StrategyAudience? Audience, int Actor, GameClock Clock) result = fact switch
        {
            BurnResolvedEvent e => (e.Audience, e.OfficerId, e.Clock), AlienateResolvedEvent e => (e.Audience, e.OfficerId, e.Clock),
            GossipResolvedEvent e => (e.Audience, e.OfficerId, e.Clock), SeizeResolvedEvent e => (e.Audience, e.OfficerId, e.Clock),
            UnitStrategyResolvedEvent e => (e.Audience, e.OfficerId, e.Clock), _ => (null, 0, default)
        };
        if (result.Audience is not { } audience || content.Progress is not { } progress) return new([], []);
        bool Player(int force) => before.World.Simulation.Controls.Any(c => c.ForceId == force && c.Kind == ControllerKind.Player);
        bool own = Player(audience.ForceId), victim = Player(audience.TargetForceId);
        var values = new Dictionary<string, MessageBinding>
        {
            ["actor"] = ProgressMessageProducer.Person(result.Actor), ["force"] = ProgressMessageProducer.Force(audience.ForceId),
            ["enemy-force"] = ProgressMessageProducer.Force(audience.TargetForceId), ["target-force"] = ProgressMessageProducer.Force(audience.TargetForceId)
        };
        if (audience.CommanderId is int commander) values["other"] = values["target-person"] = values["defender"] = ProgressMessageProducer.Person(commander);
        if (content.Decisions.People.TryGetValue("soldier", out int soldier))
        {
            values["soldier"] = ProgressMessageProducer.Person(soldier);
            values.TryAdd("defender", values["soldier"]); values.TryAdd("target-person", values["soldier"]);
        }
        if (audience.Target.PlaceId is int place) values["place"] = values["home"] = ProgressMessageProducer.Place(place);
        MapCell? location = audience.Target.Position is { } position ? MapCoordinates.Cell(position) : audience.Target.PlaceId is int home
            ? before.PlaceCell(home) ?? after.PlaceCell(home) : null;
        List<CommandMessage> messages = [];
        List<MapMessageRecord> records = [];
        var occurrence = context.AtOccurrence(result.Clock);
        if (fact is UnitStrategyResolvedEvent unitContext)
            occurrence = context.AtOccurrence(unitContext.Clock, new BattleObjectContext(unitContext.TargetUnitId, false, audience.TargetForceId,
                audience.CommanderId, unitContext.Target.Position, unitContext.Target.ServicePlaceId, false));
        void Record(string role, bool detailed = false, int? color = null, bool anonymous = false)
        {
            var message = ProgressMessageProducer.Produce(progress.Records[role], occurrence, texts, values, _ => 0);
            records.Add(new($"strategy-observer:{before.World.Revision}:{index}:{role}", message.Message, location,
                anonymous ? null : MapRecordColors.Resolve(before, after, color ?? (victim ? audience.TargetForceId : audience.ForceId)),
                detailed ? MapReportImportance.Detailed : MapReportImportance.Summary));
        }
        void Speech(string role)
        {
            if (content.Missions is not { Count: > 0 } || location is null) return;
            var message = ProgressMessageProducer.Produce(content.Missions[role], occurrence, texts, values, _ => 0);
            if (message.SpeakerId is not int speaker) return;
            messages.Add(message with { MapSpeech = new($"strategy-observer:{before.World.Revision}:{index}:{role}", message.Message,
                speaker, texts.Resolve(before.MessageSpeaker(speaker).NameKey), location.Value,
                fact is UnitStrategyResolvedEvent target ? target.TargetUnitId : null,
                before.Forces.Concat(after.Forces).First(f => f.Id == audience.TargetForceId).ColorCode, DelaySeconds: 1) });
        }
        switch (fact)
        {
            case BurnResolvedEvent burn:
                values["place"] = ProgressMessageProducer.Place(burn.TargetSettlementId);
                if (!own && victim)
                {
                    Record(burn.Success ? "burn-victim-success" : "burn-victim-failure");
                    Speech("burn-reaction-" + (burn.Success ? "success" : "failure"));
                }
                if ((own || victim) && burn.FoodLoss > 0)
                { values["current"] = new(audience.Food - burn.FoodLoss); values["amount"] = new(burn.FoodLoss); Record("burn-food", color: audience.TargetForceId); }
                break;
            case AlienateResolvedEvent alienate:
                values["other"] = ProgressMessageProducer.Person(alienate.TargetOfficerId);
                if (!own && victim && !alienate.Success) Record("alienate-victim-failure", color: audience.ForceId);
                if (own && alienate.Success && alienate.LoyaltyLoss > 0)
                {
                    values["current"] = new(audience.Loyalty - alienate.LoyaltyLoss); values["amount"] = new(alienate.LoyaltyLoss);
                    if (audience.Loyalty > 100) { values["ruler"] = ProgressMessageProducer.Person(before.World.Forces.Single(f => f.Id == audience.TargetForceId).LeaderId!.Value); Record("loyalty-hidden", true); }
                    else Record("loyalty-lost", true);
                }
                break;
            case GossipResolvedEvent gossip when !own:
                if (Player(gossip.Success ? gossip.SecondTargetForceId : gossip.FirstTargetForceId))
                { values["enemy-force"] = ProgressMessageProducer.Force(gossip.FirstTargetForceId); Record(gossip.Success ? "rumor-observer-success" : "rumor-observer-failure", color: audience.ForceId); }
                break;
            case SeizeResolvedEvent seize when !own && seize.Outcome != SeizeOutcomeKind.Failure:
                if (seize.PreviousHolderId is int holder) values["other"] = values["previous-holder"] = ProgressMessageProducer.Person(holder);
                if (seize.RecoveredOfficerId is int captive) values["other"] = values["target-person"] = ProgressMessageProducer.Person(captive);
                if (seize.TreasureId is int treasure) values["treasure"] = MessageBinding.Reference(MessageEntityKind.Treasure, treasure);
                values["current"] = new(audience.Money - seize.Money); values["amount"] = new(seize.Money);
                string kind = seize.Outcome switch { SeizeOutcomeKind.Item => "item", SeizeOutcomeKind.Person => "person", _ => "money" };
                Record($"seize-{kind}-" + (victim ? "victim" : "other"), !victim, victim ? audience.TargetForceId : null, anonymous: !victim);
                if (victim)
                {
                    string reaction = kind == "money" ? "seize-reaction-success" : $"seize-{kind}-reaction";
                    Speech(reaction);
                }
                break;
            case SeizeResolvedEvent { Outcome: SeizeOutcomeKind.Failure } when !own && victim:
                Speech("seize-reaction-failure"); break;
            case UnitStrategyResolvedEvent unit:
                if (unit.Target.CommanderId is int targetCommander) values["other"] = ProgressMessageProducer.Person(targetCommander);
                values["unit"] = MessageBinding.Reference(MessageEntityKind.Unit, unit.TargetUnitId);
                if (!own)
                {
                    switch (unit.Strategy)
                    {
                        case UnitStrategyKind.FalseReport when victim && !unit.Success: Record("false-report-victim-blocked"); break;
                        case UnitStrategyKind.Disorder when unit.Success: Record(victim ? "disorder-victim-success" : "disorder-other-success", !victim, anonymous: !victim); break;
                        case UnitStrategyKind.Disorder when victim: Record("disorder-victim-blocked"); break;
                        case UnitStrategyKind.Inspire when unit.Success: Record("inspire-other-success", true, anonymous: true); break;
                        case UnitStrategyKind.Reinforce: Record("rescue-other-result", true, anonymous: true); break;
                    }
                    if (victim && unit.Strategy is UnitStrategyKind.FalseReport or UnitStrategyKind.Disorder)
                    {
                        string role = unit.Strategy == UnitStrategyKind.FalseReport ? "false-report" : "disorder";
                        string reaction = unit.Success ? role + "-reaction-success" : unit.Target.Condition == UnitCondition.HighSpirits
                            ? "strategy-reaction-united" : unit.Target.HasCounterConfusion ? "strategy-reaction-countered"
                            : unit.Target.DefensiveFormation ? role + "-reaction-defensive" : role + "-reaction-failure";
                        Speech(reaction);
                    }
                }
                if (own || victim)
                {
                    if (audience.Morale != unit.PreviousMorale)
                    {
                        values["current"] = new(audience.Morale);
                        bool increase = audience.Morale > unit.PreviousMorale;
                        Record(increase ? "morale-gained" : "morale-lost", color: audience.TargetForceId);
                    }
                    if (unit.DeliveredSoldiers > 0) { values["current"] = new(audience.Soldiers); Record("reinforcement-arrived", color: audience.TargetForceId); }
                }
                break;
        }
        return new(messages, records);
    }
}
