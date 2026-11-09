using San9.Content.Interface;
using San9.Content.Maps;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Application.Presentation;

/// <summary>Projects occurrence-ordered facts into one prepared day; owns the presentation clock and feedback context.</summary>
internal sealed partial class BattlePresentationBuilder(StrategySnapshot before, StrategySnapshot after,
    IReadOnlyList<DomainEvent> events, WorldMessageContext context, TextCatalog texts, CommandPresentation content,
    IReadOnlyDictionary<int, int>? buildingTypes, MapPackage? map, bool tacticScenesEnabled, bool duelScenesEnabled)
{
    private readonly List<BattleMessageCue> _messages = [];
    private readonly List<BattleVisualCue> _visuals = [];
    private readonly List<BattlePreludeCue> _preludes = [];
    private readonly HashSet<string> _presented = [];
    private readonly List<BattleRecordCue> _records = [];
    private readonly List<BattleValueCue> _stateValues = [];
    private readonly List<BattleMapEffectCue> _mapEffects = [];
    private readonly List<BattleMapNumberCue> _mapNumbers = [];
    private readonly Dictionary<(int Id, bool IsSettlement), BattleObjectContext> _objects = [];
    private readonly Dictionary<(int Id, bool IsSettlement), bool> _mapVisibility = [];
    private readonly Dictionary<(int Id, bool IsSettlement), UnitCondition> _conditions = [];
    private readonly PresentationIdentity _identities = new(before, after);
    private double _cursor = MapMovementCadence.DayMilliseconds / 1000.0;
    private double _impact = MapMovementCadence.DayMilliseconds / 1000.0, _mapAvailable;
    private readonly List<BattleMapBatch> _batches = [];
    private bool _batchOpen;
    private int _index;

    private int _feedbackIndex = -1;
    private IReadOnlyList<FeedbackBatch> _feedback = [];

    private string? DialogueBefore(double time) => _messages
        .Where(c => c.Channel != BattleMessageChannel.MapSpeech && c.Time <= time)
        .OrderBy(c => c.Time).LastOrDefault()?.Id;

    private void AddMessageCue(BattleMessageCue cue) => _messages.Add(cue with { FactIndex = _index, AfterDialogueId = DialogueBefore(cue.Time) });
    private void AddRecordCue(BattleRecordCue cue) => _records.Add(cue with { FactIndex = _index, AfterDialogueId = DialogueBefore(cue.Time) });

    private int BlockingCueCount => _messages.Count(c => c.Channel != BattleMessageChannel.MapSpeech) +
        _visuals.Count(c => !c.IsMapEffect) + _mapChanges.Count + _duels.Count;

    private double LastBlockingCueTime() => _visuals.Where(c => !c.IsMapEffect).Select(c => c.End)
        .Concat(_messages.Where(c => c.Channel != BattleMessageChannel.MapSpeech).Select(c => c.Time))
        .Concat(_mapChanges.Select(c => c.End)).Concat(_duels.Select(c => c.End)).DefaultIfEmpty(0).Max();

    private IEnumerable<FeedbackBatch> Feedback(params FeedbackKind[] kinds)
    {
        if (_feedbackIndex != _index)
        {
            var view = _identities.View;
            _feedback = OccurrenceFeedback.Read(before, view, after, context, context.ForWorld(view.World), texts, content, events, _index);
            _feedbackIndex = _index;
        }
        foreach (var kind in kinds)
            foreach (var batch in _feedback.Where(b => b.Kind == kind)) yield return batch;
    }

    private IEnumerable<(string Id, CommandMessage Message)> FeedbackMessages(params FeedbackKind[] kinds)
    {
        foreach (var batch in Feedback(kinds))
            for (int i = 0; i < batch.Output.Messages.Count; i++)
            {
                string id = batch.MessageId(i);
                if (_presented.Add(id)) yield return (id, batch.Output.Messages[i]);
            }
    }

    private IEnumerable<MapMessageRecord> FeedbackRecords(params FeedbackKind[] kinds)
    {
        foreach (var batch in Feedback(kinds))
            for (int i = 0; i < batch.Output.Records.Count; i++)
                if (_presented.Add(batch.RecordId(i))) yield return batch.Output.Records[i];
    }

    private bool PublishFeedback(double time, params FeedbackKind[] kinds)
    {
        int count = _messages.Count;
        foreach (var (id, message) in FeedbackMessages(kinds))
            AddMessageCue(new(id, time, message,
                message.MapSpeech is null ? BattleMessageChannel.Dialogue : BattleMessageChannel.MapSpeech));
        foreach (var record in FeedbackRecords(kinds)) AddRecordCue(new(time, record));
        return _messages.Count > count;
    }

    private void Capture(BattleObjectContext source, BattleObjectContext target)
    {
        bool visible = Player(source.ForceId) || Player(target.ForceId);
        foreach (var obj in new[] { source, target })
        {
            _objects[(obj.Id, obj.IsSettlement)] = obj;
            _mapVisibility[(obj.Id, obj.IsSettlement)] = visible;
        }
    }

    public BattlePresentationPlan Build()
    {
        foreach (var unit in before.World.Units) _conditions[(unit.Id, false)] = unit.Condition;
        foreach (var place in before.World.Settlements)
            _conditions[(place.Id, true)] = place.ConfusionDaysRemaining > 0 ? UnitCondition.Confused : UnitCondition.Normal;
        for (_index = 0; _index < events.Count; _index++)
        {
            bool ordinaryDamage = events[_index] is BattleDamageEvent { TacticId: null, IsTacticExchange: false };
            int firstCue = BlockingCueCount;
            double firstCursor = _cursor;
            int firstValue = _stateValues.Count;
            int firstVisual = _visuals.Count, firstEffect = _mapEffects.Count, firstNumber = _mapNumbers.Count;
            int firstPrelude = _preludes.Count;
            int firstChange = _mapChanges.Count;
            int firstDuel = _duels.Count;
            switch (events[_index])
            {
                case DuelStartedEvent duel: PresentDuel(duel); break;
                case OfficerDiedEvent { Cause: OfficerDeathCause.Duel } death: PresentDuelDeath(death); break;
                case BattleDamageEvent damage: PresentDamage(damage); break;
                case BattleContactStartedEvent or UnitWaypointReachedEvent or UnitPolicyRespondedEvent: PresentMapNotification(); break;
                case BattleConditionChangedEvent condition: PresentCondition(condition); break;
                case DailyAttritionEvent attrition when attrition.UnitId is not null || attrition.SettlementId is not null:
                    _stateValues.Add(new(MapTime(_cursor), attrition.UnitId ?? attrition.SettlementId!.Value,
                        attrition.SettlementId is not null, -(long)attrition.SoldierLoss, attrition.Morale - attrition.PreviousMorale, 0));
                    break;
                case PeriodicMoraleLostEvent lost:
                    _stateValues.Add(new(MapTime(_cursor), lost.UnitId, false, 0, lost.Morale - lost.PreviousMorale, 0));
                    break;
                case GarrisonMoraleRecoveredEvent recovered:
                    _stateValues.Add(new(MapTime(_cursor), recovered.SettlementId, true, 0, recovered.Morale - recovered.PreviousMorale, 0));
                    break;
                case HighSpiritsExpiredEvent expired:
                    _stateValues.Add(new(MapTime(_cursor), expired.UnitId, false, 0, expired.CurrentMorale - expired.PreviousMorale, 0));
                    PresentOutcome();
                    break;
                case SecretContactReportedEvent or OfficerAbilityGrownEvent or RecruitmentResolvedEvent or
                    PersonnelJourneyInterruptedEvent or OfficerMovedEvent or PersonnelReturnedEvent or
                    ReturnCargoDeliveredEvent or ReturnCargoLostEvent: PresentFeedback(); break;
                case TacticLearnedEvent or OfficerInjuryWorsenedEvent or CommanderChangedEvent or UnitCommanderChangedEvent or
                    CorpsLeaderChangedEvent or CorpsDissolvedEvent or OfficerAppearedEvent or
                    TreasuryCapturedEvent or PrisonerEscapedEvent or BattlePersonnelResolvedEvent or DiplomaticConflictsEndedEvent: PresentOutcome(); break;
                case BattleVictoryMoraleChangedEvent victory:
                    PresentAftermath();
                    _stateValues.Add(new(MapTime(_cursor), victory.Victor.Id, victory.Victor.IsSettlement, 0, victory.Current - victory.Previous, 0));
                    break;
                case TacticMoraleChangedEvent morale: PresentMorale(morale); break;
                case LeaderInspiredEvent inspired: PresentLeaderInspiration(inspired); break;
                case TacticDefectionEvent defection: PresentDefection(defection); break;
                case TacticHealingEvent healing: PresentHealing(healing); break;
                case BurnResolvedEvent or SeizeResolvedEvent or UnitStrategyResolvedEvent or AlienateResolvedEvent or GossipResolvedEvent:
                    PresentMission(events[_index]); break;
                case UnitDefeatedEvent or BattleWoundedCapturedEvent or SettlementCapturedEvent or FacilityDestroyedEvent or OfficerCapturedEvent or TacticPrisonerRescuedEvent:
                    PresentAftermath(); break;
                case TacticActivatedEvent or IntelligenceEffectResolvedEvent: PresentTactic(events[_index]); break;
                case TacticSecondaryPreventedEvent prevented: PresentSecondaryPrevention(prevented); break;
            }
            for (int i = firstValue; i < _stateValues.Count; i++)
                _stateValues[i] = _stateValues[i] with { FactIndex = _index, AfterDialogueId = DialogueBefore(_stateValues[i].Time) ?? _mapDialogue };
            for (int i = firstVisual; i < _visuals.Count; i++)
                _visuals[i] = _visuals[i] with { FactIndex = _index, AfterDialogueId = DialogueBefore(_visuals[i].EntranceStart ?? _visuals[i].Start) };
            for (int i = firstPrelude; i < _preludes.Count; i++)
                _preludes[i] = _preludes[i] with { FactIndex = _index, AfterDialogueId = DialogueBefore(_preludes[i].Start) };
            for (int i = firstEffect; i < _mapEffects.Count; i++)
                _mapEffects[i] = _mapEffects[i] with { FactIndex = _index, AfterDialogueId = DialogueBefore(_mapEffects[i].Start) };
            for (int i = firstNumber; i < _mapNumbers.Count; i++)
                _mapNumbers[i] = _mapNumbers[i] with { FactIndex = _index, AfterDialogueId = DialogueBefore(_mapNumbers[i].Start) };
            for (int i = firstDuel; i < _duels.Count; i++) _duels[i] = _duels[i] with { FactIndex = _index };
            _identities.Apply(events[_index]);
            PresentMapChange(events[_index]);
            for (int i = firstChange; i < _mapChanges.Count; i++)
            {
                _mapChanges[i] = _mapChanges[i] with { AfterDialogueId = DialogueBefore(_mapChanges[i].Time) ?? _mapChanges[i].AfterDialogueId };
                if (events[_index] is UnitTerminatedEvent { Reason: UnitTerminationReason.Defeated or UnitTerminationReason.MoraleCollapsed }
                    or SettlementCapturedEvent or FacilityDestroyedEvent)
                    PublishTerminalLoss(_mapChanges[i]);
            }
            // A batch includes state publication and live map effects. Only a modal or
            // lifecycle boundary separates attacks; the client admits and waits live visuals.
            if (!ordinaryDamage && (BlockingCueCount != firstCue || _cursor != firstCursor)) _batchOpen = false;
            var newMapEffects = _mapEffects.Skip(firstEffect).Select(c => c.Start)
                .Concat(_visuals.Skip(firstVisual).Where(c => c.IsMapEffect && c.Effect is not null).Select(c => c.Start)).ToArray();
            if (!_batchOpen && newMapEffects.Length > 0)
            {
                double time = newMapEffects.Min();
                _batches.Add(new(time, _index, _index, DialogueBefore(time)));
                _batchOpen = true;
            }
            if (_batchOpen) _batches[^1] = _batches[^1] with { LastFactIndex = _index };
        }
        double finalCue = LastBlockingCueTime();
        return new BattlePresentationPlan(_messages, _records, _visuals, _stateValues, _mapEffects, _mapNumbers, content.Battle?.MapNumbers, _presented,
            _duels.Count == 0 && _messages.Count == 0 && _records.Count == 0 && _visuals.Count == 0 && _mapEffects.Count == 0 &&
            _mapNumbers.Count == 0 && _mapChanges.Count == 0 && _stateValues.Count == 0 && _batches.Count == 0
                ? finalCue : Math.Max(_cursor, finalCue), _duels, _mapChanges, _batches, _preludes);
    }

    private bool Player(int? force) => before.World.Simulation.Controls.Any(c => c.ForceId == force && c.Kind == ControllerKind.Player);
    private bool DirectPlayer(BattleObjectContext obj) => Player(obj.ForceId) && before.World.Corps.Any(c =>
        c.OwnerId == obj.ForceId && (obj.CorpsId is int corpsId ? c.Id == corpsId : c.IsRoot) &&
        CorpsControl.PlayerOperates(c, before.Companion));
    private bool Visible(TacticOccurrence occurrence) => DirectPlayer(occurrence.Source) || DirectPlayer(occurrence.Target);
    private int Soldier => content.Decisions.People.GetValueOrDefault("soldier");
    private MapCell Location(BattleObjectContext obj)
    {
        if (obj.Position is { } position) return MapCoordinates.Cell(position);
        var place = before.Settlements.Concat(after.Settlements).First(s => s.Id == obj.ServicePlaceId);
        return new(place.X, place.Y);
    }
    private MessageBinding Object(BattleObjectContext obj) => MessageBinding.Reference(obj.IsSettlement ? MessageEntityKind.Settlement : MessageEntityKind.Unit, obj.Id);
    private BattleObjectContext? TargetContext(int id, bool settlement)
    {
        if (_objects.TryGetValue((id, settlement), out var captured)) return captured;
        if (settlement && before.World.Settlements.FirstOrDefault(s => s.Id == id) is { } place)
            return new(id, true, place.OwnerId, null, null, id, true) { CorpsId = place.CorpsId };
        if (!settlement && before.World.Units.FirstOrDefault(u => u.Id == id) is { } unit)
            return new(id, false, unit.OwnerId, unit.CommanderId, unit.Position, unit.ServicePlaceId, unit.IsEmbedded) { CorpsId = unit.CorpsId };
        return null;
    }
    private bool MapVisible(BattleObjectContext obj) => _mapVisibility.GetValueOrDefault((obj.Id, obj.IsSettlement), Player(obj.ForceId));
    private double MapTime(double time) => Math.Max(time, _mapAvailable);

    private string Identity(string role) => $"battle:{before.World.Revision}:{_index}:{role}";
    private void Number(BattleObjectContext? obj, long amount, double time, string role)
    {
        if (amount != 0 && obj is not null && content.Battle?.MapNumbers is not null)
        {
            double start = MapTime(time);
            _mapNumbers.Add(new(Identity(role), start, obj, amount));
        }
    }
    private void PresentDamage(BattleDamageEvent damage)
    {
        var damageTarget = damage.Target ?? TargetContext(damage.TargetUnitId ?? damage.SettlementId!.Value, damage.SettlementId is not null);
        var damageSource = damage.Source ?? TargetContext(damage.AttackerId, damage.AttackerIsSettlement);
        if (damageSource is not null && damageTarget is not null) Capture(damageSource, damageTarget);
        bool ordinary = damage.TacticId is null && !damage.IsTacticExchange;
        if (ordinary)
        {
            if (!_batchOpen)
            {
                _cursor = MapTime(Math.Max(_cursor, LastBlockingCueTime()));
                _batches.Add(new(_cursor, _index, _index, DialogueBefore(_cursor)));
                _batchOpen = true;
            }
            else _batches[^1] = _batches[^1] with { LastFactIndex = _index };
        }
        double time = ordinary ? _cursor : _impact;
        _impact = time;
        _stateValues.Add(new(time, damage.TargetUnitId ?? damage.SettlementId!.Value,
            damage.SettlementId is not null, -(long)damage.SoldierLoss, -damage.MoraleLoss, -damage.DurabilityLoss));
        if (damage.TacticId is not null)
            Number(damageTarget, -(long)damage.SoldierLoss, time, "map-loss");
    }

    private void PresentCondition(BattleConditionChangedEvent condition)
    {
        if (condition.Condition == UnitCondition.Normal)
            PublishFeedback(MapTime(_impact), FeedbackKind.Progress);
        var obj = condition.Target ?? TargetContext(condition.ObjectId, condition.IsSettlement);
        if (condition.SourceForceId is int sourceForce && obj is not null)
        {
            _objects[(obj.Id, obj.IsSettlement)] = obj;
            _mapVisibility[(obj.Id, obj.IsSettlement)] = Player(sourceForce) || Player(obj.ForceId);
            _impact = MapTime(_cursor);
        }
        double time = obj is null ? _impact : MapTime(_impact);
        var key = (condition.ObjectId, condition.IsSettlement);
        var previous = _conditions.GetValueOrDefault(key, UnitCondition.Normal);
        _conditions[key] = condition.Condition;
        _stateValues.Add(new(time, condition.ObjectId, condition.IsSettlement, 0, 0, 0, condition.Condition));
        if (previous != condition.Condition && obj is not null &&
            content.Battle?.ConditionEffects.TryGetValue(condition.Condition, out var sprite) == true)
        {
            _mapEffects.Add(new(Identity("condition"), time, obj, sprite));
        }
    }

    private void PresentMission(DomainEvent fact)
    {
        _cursor = MapTime(_cursor);
        if (fact is UnitStrategyResolvedEvent strategy)
        {
            int morale = strategy.Audience.Morale - strategy.PreviousMorale;
            if (strategy.DeliveredSoldiers != 0 || morale != 0)
                _stateValues.Add(new(_cursor, strategy.TargetUnitId, false, strategy.DeliveredSoldiers, morale, 0));
        }
        else if (fact is BurnResolvedEvent { DurabilityLoss: > 0 } burned)
            _stateValues.Add(new(_cursor, burned.TargetSettlementId, true, 0, 0, -burned.DurabilityLoss));
        var messages = FeedbackMessages(FeedbackKind.Mission, FeedbackKind.Strategy).ToArray();
        var records = FeedbackRecords(FeedbackKind.Mission, FeedbackKind.Strategy, FeedbackKind.Progress).ToArray();
        if (fact is UnitStrategyResolvedEvent { Strategy: UnitStrategyKind.Reinforce, Success: true } reinforcement &&
            content.Battle?.ReinforcementEffect is { } reinforcementEffect)
        {
            var target = reinforcement.Target;
            var obj = new BattleObjectContext(reinforcement.TargetUnitId, false, target.ForceId,
                target.CommanderId, target.Position, target.ServicePlaceId, false);
            _mapEffects.Add(new(Identity("reinforcement"), MapTime(_cursor), obj, reinforcementEffect));
        }
        int? burning = fact is BurnResolvedEvent { Success: true } burn ? burn.TargetSettlementId : null;
        int? seizing = fact is SeizeResolvedEvent { Outcome: not SeizeOutcomeKind.Failure } seize ? seize.TargetSettlementId : null;
        if ((burning ?? seizing) is int building && content.Battle?.BuildingEffects.TryGetValue(burning is null ? "seize" : "burn", out var effects) == true)
        {
            var place = before.Settlements.First(s => s.Id == building);
            int? type = place.FacilityTypeId ?? buildingTypes?.GetValueOrDefault(building);
            if (type is int fixedType && effects.TryGetValue(fixedType, out var sprites))
            {
                var obj = new BattleObjectContext(building, true, place.OwnerId, null, null, building, true);
                for (int frame = 0; frame < sprites.Length; frame++)
                    _visuals.Add(new(Identity($"mission-effect-{frame}"), _cursor, _cursor + sprites[frame].Columns * sprites[frame].FrameMilliseconds / 1000.0, obj, obj, -1, 0, false, false, 0, 0, 0, sprites[frame], null));
            }
        }
        foreach (var (feedbackId, message) in messages)
        {
            var id = message.MapSpeech?.Id ?? feedbackId;
            var fixedMessage = message with
            {
                MapSpeech = message.MapSpeech is { } speech ? speech with { Id = id } : null
            };
            AddMessageCue(new(id, _cursor, fixedMessage,
                fixedMessage.MapSpeech is null ? BattleMessageChannel.Dialogue : BattleMessageChannel.MapSpeech));
        }
        foreach (var record in records)
            AddRecordCue(new(_cursor, record));
    }

    private void PresentAftermath()
    {
        _cursor = Math.Max(_cursor, _impact);
        PublishFeedback(_cursor, FeedbackKind.Report, FeedbackKind.Capture, FeedbackKind.MapSpeech);
    }

    private void PresentOutcome()
    {
        PublishFeedback(_cursor, FeedbackKind.Outcome);
    }

    private void PresentFeedback()
    {
        PublishFeedback(_cursor, FeedbackKind.Progress, FeedbackKind.Personnel);
    }

    private void PresentMapNotification()
    {
        PublishFeedback(_cursor, FeedbackKind.Report, FeedbackKind.Progress, FeedbackKind.MapSpeech);
    }
}
