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
            int firstChange = _mapChanges.Count;
            int firstDuel = _duels.Count;
            switch (events[_index])
            {
                case DuelStartedEvent duel: PresentDuel(duel); break;
                case OfficerDiedEvent { Cause: OfficerDeathCause.Duel } death: PresentDuelDeath(death); break;
                case BattleDamageEvent damage: PresentDamage(damage); break;
                case BattleContactStartedEvent or UnitWaypointReachedEvent or UnitPolicyRespondedEvent: PresentMapNotification(); break;
                case BattleConditionChangedEvent condition: PresentCondition(condition); break;
                case SecretContactReportedEvent or OfficerAbilityGrownEvent or RecruitmentResolvedEvent or
                    PersonnelJourneyInterruptedEvent or OfficerMovedEvent or PersonnelReturnedEvent or
                    ReturnCargoDeliveredEvent or ReturnCargoLostEvent: PresentFeedback(); break;
                case TacticLearnedEvent or OfficerInjuryWorsenedEvent or CommanderChangedEvent or UnitCommanderChangedEvent or
                    CorpsLeaderChangedEvent or CorpsDissolvedEvent or HighSpiritsExpiredEvent or OfficerAppearedEvent or
                    TreasuryCapturedEvent or PrisonerEscapedEvent or BattlePersonnelResolvedEvent: PresentOutcome(); break;
                case TacticMoraleChangedEvent morale: PresentMorale(morale); break;
                case TacticDefectionEvent defection: PresentDefection(defection); break;
                case TacticHealingEvent healing: PresentHealing(healing); break;
                case BurnResolvedEvent or SeizeResolvedEvent or UnitStrategyResolvedEvent or AlienateResolvedEvent or GossipResolvedEvent:
                    PresentMission(events[_index]); break;
                case UnitTerminatedEvent or SettlementCapturedEvent or FacilityDestroyedEvent or OfficerCapturedEvent or TacticPrisonerRescuedEvent:
                    PresentAftermath(); break;
                case TacticActivatedEvent or IntelligenceEffectResolvedEvent: PresentTactic(events[_index]); break;
            }
            for (int i = firstValue; i < _stateValues.Count; i++)
                _stateValues[i] = _stateValues[i] with { FactIndex = _index, AfterDialogueId = DialogueBefore(_stateValues[i].Time) ?? _mapDialogue };
            for (int i = firstVisual; i < _visuals.Count; i++)
                _visuals[i] = _visuals[i] with { FactIndex = _index, AfterDialogueId = DialogueBefore(_visuals[i].EntranceStart ?? _visuals[i].Start) };
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
                ? finalCue : Math.Max(_cursor, finalCue), _duels, _mapChanges, _batches);
    }

    private bool Player(int? force) => before.World.Simulation.Controls.Any(c => c.ForceId == force && c.Kind == ControllerKind.Player);
    private bool Visible(TacticOccurrence occurrence) => Player(occurrence.Source.ForceId) || Player(occurrence.Target.ForceId);
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
            return new(id, true, place.OwnerId, null, null, id, true);
        if (!settlement && before.World.Units.FirstOrDefault(u => u.Id == id) is { } unit)
            return new(id, false, unit.OwnerId, unit.CommanderId, unit.Position, unit.ServicePlaceId, unit.IsEmbedded);
        return null;
    }
    private bool MapVisible(BattleObjectContext obj) => _mapVisibility.GetValueOrDefault((obj.Id, obj.IsSettlement), Player(obj.ForceId));
    private double MapTime(double time) => Math.Max(time, _mapAvailable);

    private string Identity(string role) => $"battle:{before.World.Revision}:{_index}:{role}";
    private void Number(BattleObjectContext? obj, long amount, double time, string role)
    {
        if (amount != 0 && obj is not null && MapVisible(obj) && content.Battle?.MapNumbers is not null)
        {
            double start = MapTime(time);
            _mapNumbers.Add(new(Identity(role), start, obj, amount));
        }
    }
    private void Outcome(string role, GameClock clock, TacticOccurrence occurrence, uint amount, int? colorForce)
    {
        if (content.Battle is not { } battle || !Visible(occurrence)) return;
        var source = occurrence.Source; var target = occurrence.Target;
        Dictionary<string, MessageBinding> values = new()
        {
            ["source-commander"] = ProgressMessageProducer.Person(source.CommanderId ?? Soldier),
            ["target-commander"] = ProgressMessageProducer.Person(target.CommanderId ?? Soldier),
            ["target"] = Object(target),
            ["damage"] = new(amount)
        };
        var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"battle:{_index}:{role}"));
        var produced = ProgressMessageProducer.Produce(battle.Messages[role], context.AtOccurrence(clock,
            source.Id == target.Id && source.IsSettlement == target.IsSettlement ? [source] : [source, target]), texts, values, n => (int)(random.NextUInt64() % (ulong)n));
        var id = Identity(role);
        AddRecordCue(new(_impact, new(id, produced.Message, Location(target),
            MapRecordColors.Resolve(before, after, colorForce)), produced.SoundRole));
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
        if (previous != condition.Condition && obj is not null && MapVisible(obj) &&
            content.Battle?.ConditionEffects.TryGetValue(condition.Condition, out var sprite) == true)
        {
            _mapEffects.Add(new(Identity("condition"), time, obj, sprite));
        }
    }

    private void PresentMorale(TacticMoraleChangedEvent morale)
    {
        _stateValues.Add(new(_impact, morale.TargetId, morale.TargetIsSettlement, 0, morale.Current - morale.Previous, 0));
        int visual = _visuals.FindLastIndex(c => c.TacticId == morale.TacticId && c.Zoom is null && !c.ScreenEffect && c.Target.Id == morale.TargetId && c.Target.IsSettlement == morale.TargetIsSettlement);
        if (visual >= 0 && morale.Current > morale.Previous) _visuals[visual] = _visuals[visual] with { MoraleGain = morale.Current - morale.Previous };
    }

    private void PresentDefection(TacticDefectionEvent defection)
    {
        var from = defection.Occurrence.Source; var to = defection.Occurrence.Target;
        Capture(from, to);
        _stateValues.Add(new(_impact, from.Id, from.IsSettlement, defection.SourceGain, 0, 0));
        _stateValues.Add(new(_impact, to.Id, to.IsSettlement, -(long)defection.TargetLoss, 0, 0));
        Number(from, defection.SourceGain, _impact, "map-defection-gain");
        Number(to, -(long)defection.TargetLoss, _impact, "map-defection-loss");
        int visual = _visuals.FindLastIndex(c => c.TacticId == defection.TacticId && c.Zoom is null && !c.ScreenEffect && c.Target.Id == to.Id && c.Target.IsSettlement == to.IsSettlement);
        if (visual >= 0 && Visible(defection.Occurrence)) _visuals[visual] = _visuals[visual] with { SoldierLoss = defection.TargetLoss };
        Outcome("defection", defection.Clock, defection.Occurrence, defection.SourceGain,
            Player(from.ForceId) ? from.ForceId : to.ForceId);
    }

    private void PresentMission(DomainEvent fact)
    {
        var messages = FeedbackMessages(FeedbackKind.Mission, FeedbackKind.Strategy).ToArray();
        var records = FeedbackRecords(FeedbackKind.Mission, FeedbackKind.Strategy, FeedbackKind.Progress).ToArray();
        int? burning = fact is BurnResolvedEvent { Success: true } burn ? burn.TargetSettlementId : null;
        int? seizing = fact is SeizeResolvedEvent { Outcome: not SeizeOutcomeKind.Failure } seize ? seize.TargetSettlementId : null;
        if (messages.Length > 0 && (burning ?? seizing) is int building && content.Battle?.BuildingEffects.TryGetValue(burning is null ? "seize" : "burn", out var effects) == true)
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

    private void PresentHealing(TacticHealingEvent healing)
    {
        if (healing.Occurrence is not { } heal || healing.TacticId is not int healTactic ||
            content.Battle is not { } battle || !battle.Tactics.TryGetValue(healTactic, out var healDefinition) || !Visible(heal)) return;
        Capture(heal.Source, heal.Target);
        var sprite = healDefinition.MapEffect;
        _visuals.Add(new(Identity("healing"), _impact, _impact + (sprite?.Columns * sprite?.FrameMilliseconds ?? 0) / 1000.0,
            heal.Source, heal.Target, healTactic, healing.ActorId ?? Soldier, false, false, 0, 0, 0, sprite, null)
        { SoldierGain = healing.RecoveredSoldiers });
        _stateValues.Add(new(_impact, healing.TargetUnitId, false, healing.RecoveredSoldiers, 0, 0));
        Number(heal.Target, healing.RecoveredSoldiers, _impact, "map-healing");
        if (healing.RecoveredSoldiers > 0) Outcome("healed", healing.Clock, heal, healing.RecoveredSoldiers, heal.Source.ForceId);
        _cursor = Math.Max(_cursor, _impact);
    }

    private void PresentTactic(DomainEvent fact)
    {
        if (content.Battle is not { } battle) return;
        TacticOccurrence? occurrence; int tacticId, actor, effect; GameClock clock;
        if (fact is TacticActivatedEvent activation)
        {
            occurrence = activation.Occurrence; tacticId = activation.TacticId; actor = activation.Participants[0].OfficerId; effect = activation.Effect; clock = activation.Clock;
        }
        else if (fact is IntelligenceEffectResolvedEvent intelligence)
        {
            occurrence = intelligence.Occurrence; tacticId = intelligence.TacticId; actor = intelligence.ActorId ?? Soldier; effect = intelligence.Effect; clock = intelligence.Clock;
        }
        else return;
        _impact = _cursor;
        if (occurrence is null || !battle.Tactics.TryGetValue(tacticId, out var definition)) return;
        // Area tactics announce once at the first affected target visible to the player.
        if (fact is TacticActivatedEvent && definition.Zoom is null && !definition.Support)
            occurrence = events.Skip(_index + 1)
                .TakeWhile(e => e is not TacticActivatedEvent)
                .OfType<IntelligenceEffectResolvedEvent>()
                .Where(e => e.TacticId == tacticId && e.Source.Id == occurrence.Source.Id && e.Source.IsSettlement == occurrence.Source.IsSettlement)
                .Select(e => e.Occurrence).FirstOrDefault(o => o is not null && Visible(o)) ?? occurrence;
        Capture(occurrence.Source, occurrence.Target);
        var source = occurrence.Source; var target = occurrence.Target;
        var occurrenceContext = context.AtOccurrence(clock, source.Id == target.Id && source.IsSettlement == target.IsSettlement ? [source] : [source, target]);
        Dictionary<string, MessageBinding> values = new()
        {
            ["actor"] = ProgressMessageProducer.Person(actor),
            ["source-commander"] = ProgressMessageProducer.Person(source.CommanderId ?? Soldier),
            ["target-commander"] = ProgressMessageProducer.Person(target.CommanderId ?? Soldier),
            ["soldier"] = ProgressMessageProducer.Person(Soldier),
            ["responder"] = ProgressMessageProducer.Person(occurrence.ResistanceOfficerId ?? Soldier),
            ["source"] = Object(source),
            ["target"] = Object(target),
            ["tactic"] = MessageBinding.Reference(MessageEntityKind.Tactic, tacticId),
            ["force"] = ProgressMessageProducer.Force(source.ForceId!.Value),
            ["damage"] = new(0)
        };
        if (target.ForceId is int targetForce) values["target-force"] = ProgressMessageProducer.Force(targetForce);
        void AddRecord(string role, DecisionMessageDefinition message, double time, BattleObjectContext location, int? colorForce, MapReportImportance importance = MapReportImportance.Summary)
        {
            var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"battle:{_index}:{role}:{_messages.Count + _records.Count}"));
            var produced = ProgressMessageProducer.Produce(message, occurrenceContext, texts, values, n => (int)(random.NextUInt64() % (ulong)n));
            var id = Identity($"{role}:{_messages.Count + _records.Count}");
            AddRecordCue(new(time, new(id, produced.Message, Location(location), MapRecordColors.Resolve(before, after, colorForce), importance), produced.SoundRole));
        }
        void Add(string role, DecisionMessageDefinition message, double time, BattleMessageChannel channel, bool atSource = false, double delay = 0)
        {
            var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"battle:{_index}:{role}:{_messages.Count + _records.Count}"));
            var produced = ProgressMessageProducer.Produce(message, occurrenceContext, texts, values, n => (int)(random.NextUInt64() % (ulong)n));
            var id = Identity($"{role}:{_messages.Count + _records.Count}"); var obj = atSource ? source : target;
            int? color = obj.ForceId is int force ? before.Forces.Concat(after.Forces).First(f => f.Id == force).ColorCode : null;
            if (channel == BattleMessageChannel.MapSpeech && produced.SpeakerId is int speaker)
                produced = produced with
                {
                    MapSpeech = new(id, produced.Message, speaker,
                    texts.Resolve(before.MessageSpeaker(speaker).NameKey), Location(obj), obj.IsSettlement ? null : obj.Id, color, DelaySeconds: delay)
                };
            AddMessageCue(new(id, time, produced, channel));
        }
        double Prelude()
        {
            double end = _cursor;
            for (int frame = 0; frame < definition.Prelude.Length; frame++)
            {
                var sprite = definition.Prelude[frame];
                double next = end + sprite.Columns * sprite.FrameMilliseconds / 1000.0;
                _visuals.Add(new(Identity($"prelude-{frame}"), end, next,
                    source, target, tacticId, actor, false, false, 0, 0, 0,
                    sprite with { Sound = frame == 0 ? sprite.Sound : null }, null)
                { ScreenEffect = true });
                end = next;
            }
            return end;
        }
        if (!Visible(occurrence))
        {
            if (fact is TacticActivatedEvent && !definition.Support)
            {
                AddRecord("activation-other", battle.Messages["activation-other"], _cursor, source, null, MapReportImportance.Detailed);
            }
            return;
        }
        if (fact is TacticActivatedEvent active)
        {
            void BindParticipant(TacticParticipant participant)
            {
                values["actor"] = ProgressMessageProducer.Person(participant.OfficerId);
                values["tactic"] = MessageBinding.Reference(MessageEntityKind.Tactic, participant.TacticId);
            }
            bool support = definition.Support;
            string report = support ? "support" : (active.IsChain ? "chain-" : "activation-") + (Player(source.ForceId) ? "own" : "enemy");
            var reportingSide = Player(source.ForceId) ? source : target;
            AddRecord(report, battle.Messages[report], _cursor, support ? source : reportingSide, reportingSide.ForceId);
            bool showScene = definition.Zoom is not null && tacticScenesEnabled;
            if (definition.Zoom is null)
                Add("announcement", definition.Messages["announcement"], _cursor, BattleMessageChannel.Dialogue, true);
            if (active.Participants.Length > 1)
                foreach (var participant in active.Participants)
                {
                    BindParticipant(participant);
                    AddRecord("link", battle.Messages["link"], _cursor, source, source.ForceId);
                }
            for (int member = 0; member < active.Participants.Length; member++)
            {
                BindParticipant(active.Participants[member]);
                string role = member == 0 ? active.IsChain ? occurrence.TargetCondition == UnitCondition.NoFormation ? "chain-unformed" : "chain-confused" : "action" : $"link-{Math.Min(member - 1, 3)}";
                Add(role, role == "action" ? definition.Messages[role] : battle.Messages[role],
                    _cursor,
                    definition.Zoom is not null ? BattleMessageChannel.Dialogue : BattleMessageChannel.MapSpeech, true);
            }
            BindParticipant(active.Participants[0]);
            double preludeEnd = definition.Zoom is null || showScene ? Prelude() : _cursor;
            if (definition.Zoom is { } zoom)
            {
                var following = events.Skip(_index + 1).TakeWhile(e => e is not TacticActivatedEvent).ToArray();
                var hit = following.OfType<BattleDamageEvent>().FirstOrDefault(e => e.TacticId == tacticId &&
                    e.AttackerId == source.Id && e.AttackerIsSettlement == source.IsSettlement &&
                    e.TargetUnitId == (target.IsSettlement ? null : target.Id) && e.SettlementId == (target.IsSettlement ? target.Id : null));
                var sniping = following.OfType<TacticSnipingResolvedEvent>().FirstOrDefault(e => e.TacticId == tacticId &&
                    e.Source.Id == source.Id && e.Source.IsSettlement == source.IsSettlement &&
                    e.Target.Id == target.Id && e.Target.IsSettlement == target.IsSettlement);
                double start = preludeEnd, end = preludeEnd;
                double responseStart = preludeEnd, snipingStart = preludeEnd, snipingResponse = preludeEnd;
                if (showScene)
                {
                    var scene = BattleScenePreparation.Create(before, occurrence, zoom, battle, hit, sniping,
                        effect < 10, clock, _index, buildingTypes, map);
                    var playback = scene.Playback;
                    // Dialogue completion starts darkening; action and reveal share the scene boundary.
                    start = _cursor + BattleSceneTransition.DarkenDuration;
                    end = start + playback.Duration;
                    _impact = start + playback.Impacts[0].Time;
                    _visuals.Add(new(Identity("zoom"), start, end, source, target, tacticId, actor, effect < 10, active.Doubled,
                        hit?.SoldierLoss ?? 0, hit?.DurabilityLoss ?? 0, hit?.MoraleLoss ?? 0, null, scene.Zoom)
                    { Playback = playback, Officers = scene.Officers, Backdrop = scene.Backdrop, EntranceStart = _cursor });
                    responseStart = end - 4;
                    snipingStart = start + playback.ActionEnd;
                    snipingResponse = start + playback.FeedbackStart;
                    if (effect < 10)
                    {
                        Add("block-reaction", battle.Messages["block-reaction"], responseStart, BattleMessageChannel.Dialogue);
                        Add("block-reply", battle.Messages["block-reply"], responseStart, BattleMessageChannel.Dialogue, true);
                    }
                    else Add("hit-reaction", battle.Messages["hit-reaction"], responseStart, BattleMessageChannel.Dialogue);
                }
                else _impact = preludeEnd;
                values["damage"] = new(hit?.SoldierLoss ?? 0);
                string resultRole = effect < 10 ? "blocked" : "damage";
                AddRecord(resultRole, battle.Messages[resultRole], end, target, target.ForceId);
                if (sniping is not null)
                {
                    values["actor"] = ProgressMessageProducer.Person(sniping.ActorId);
                    values["responder"] = ProgressMessageProducer.Person(sniping.OfficerId);
                    if (showScene)
                    {
                        Add("sniping-action", battle.Messages["sniping-action"], snipingStart, BattleMessageChannel.Dialogue, true);
                        string response = sniping.Injury == 7 ? "sniping-fatal" : sniping.Injury == 4 ? "sniping-heavy" : "sniping-light";
                        Add(response, battle.Messages[response], snipingResponse, BattleMessageChannel.Dialogue);
                        double frameStart = snipingStart;
                        for (int frame = 0; frame < battle.SnipingEffect.Length; frame++)
                        {
                            var sprite = battle.SnipingEffect[frame];
                            double frameEnd = frameStart + sprite.Columns * sprite.FrameMilliseconds / 1000.0;
                            double? fadeStart = frame == battle.SnipingEffect.Length - 1 ? frameEnd + sprite.FrameMilliseconds / 1000.0 : null;
                            _visuals.Add(new(Identity($"sniping-effect-{frame}"), frameStart, fadeStart + BattleEffectFade.Duration ?? frameEnd,
                                source, target, tacticId, sniping.ActorId, false, false, 0, 0, 0,
                                sprite with { Sound = frame == 0 ? sprite.Sound : null }, null)
                            { ScreenEffect = true, FadeOutStart = fadeStart });
                            frameStart = frameEnd;
                        }
                    }
                    values["injury"] = new(0, CommandText.Resolve(texts, sniping.Injury == 0 ? "battle.injury-healthy" : sniping.Injury <= 4 ? "battle.injury-light" : "battle.injury-heavy"));
                    string reportRole = Player(target.ForceId) ? "sniping-own" : "sniping-enemy";
                    AddRecord(reportRole, battle.Messages[reportRole], end, source,
                        Player(target.ForceId) ? target.ForceId : source.ForceId);
                }
                _mapAvailable = end;
                _cursor = end;
            }
            else if (support)
            {
                _impact = preludeEnd;
                if (definition.EffectAtSource && definition.MapEffect is { } sprite)
                    _visuals.Add(new(Identity("support"), preludeEnd, preludeEnd + sprite.Columns * sprite.FrameMilliseconds / 1000.0,
                        source, source, tacticId, actor, false, false, 0, 0, 0, sprite, null));
                Add("hit", definition.Messages["hit"], _cursor, BattleMessageChannel.MapSpeech, true, delay: 1);
                _cursor = Math.Max(_cursor, _impact);
            }
            else _cursor = preludeEnd;
        }
        else
        {
            string role = effect >= 10 ? "hit" : effect > 0 ? "reduced" : "failed";
            values["responder"] = ProgressMessageProducer.Person((effect == 0 ? occurrence.IntelligenceOfficerId : occurrence.ResistanceOfficerId) ?? Soldier);
            if (effect == 0)
            {
                if (definition.UnitedResponse && occurrence.TargetCondition == UnitCondition.HighSpirits) role = "united";
                else if (definition.CounterResponse is { } counterRole && occurrence.CounterOfficerId is int counter)
                { role = counterRole; values["responder"] = ProgressMessageProducer.Person(counter); }
                else if (occurrence.DefensiveFormation && definition.FormationResponse) Add("defensive", definition.Messages["defensive"], _cursor, BattleMessageChannel.MapSpeech, delay: 1);
            }
            if (effect > 0 && definition.MapEffect is { } sprite)
            {
                _impact = _cursor;
                var hit = events.Skip(_index + 1).TakeWhile(e => e is not IntelligenceEffectResolvedEvent and not TacticActivatedEvent)
                    .OfType<BattleDamageEvent>().FirstOrDefault(e => e.TacticId == tacticId);
                _visuals.Add(new(Identity("effect"), _cursor, _cursor + sprite.Columns * sprite.FrameMilliseconds / 1000.0,
                    source, target, tacticId, actor, false, false, hit?.SoldierLoss ?? 0, hit?.DurabilityLoss ?? 0, hit?.MoraleLoss ?? 0, sprite, null));
            }
            Add(role, definition.Messages[role], _cursor, BattleMessageChannel.MapSpeech, delay: 1);
            _cursor = Math.Max(_cursor, _impact);
        }
    }
}
