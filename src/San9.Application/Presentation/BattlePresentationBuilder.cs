using San9.Content.Interface;
using San9.Content.Maps;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Application.Presentation;

/// <summary>Projects occurrence-ordered facts into one prepared day; owns the presentation clock and feedback context.</summary>
internal sealed class BattlePresentationBuilder(StrategySnapshot before, StrategySnapshot after,
    IReadOnlyList<DomainEvent> events, WorldMessageContext context, TextCatalog texts, CommandPresentation content,
    IReadOnlyDictionary<int, int>? buildingTypes, MapPackage? map)
{
    private readonly List<BattleMessageCue> _messages = [];
    private readonly List<BattleVisualCue> _visuals = [];
    private readonly HashSet<int> _presented = [];
    private readonly List<BattleRecordCue> _records = [];
    private readonly List<BattleValueCue> _stateValues = [];
    private readonly List<BattleMapEffectCue> _mapEffects = [];
    private readonly List<BattleMapNumberCue> _mapNumbers = [];
    private readonly Dictionary<(int Id, bool IsSettlement), BattleObjectContext> _objects = [];
    private readonly Dictionary<(int Id, bool IsSettlement), bool> _mapVisibility = [];
    private readonly Dictionary<(int Id, bool IsSettlement), UnitCondition> _conditions = [];
    private double _cursor = 1, _impact = 1.5, _mapAvailable;
    private int _index;

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
            switch (events[_index])
            {
                case BattleDamageEvent damage: PresentDamage(damage); break;
                case BattleConditionChangedEvent condition: PresentCondition(condition); break;
                case TacticMoraleChangedEvent morale: PresentMorale(morale); break;
                case TacticDefectionEvent defection: PresentDefection(defection); break;
                case TacticHealingEvent healing: PresentHealing(healing); break;
                case BurnResolvedEvent or SeizeResolvedEvent or UnitStrategyResolvedEvent: PresentMission(events[_index]); break;
                case UnitTerminatedEvent or SettlementCapturedEvent or FacilityDestroyedEvent or OfficerCapturedEvent or TacticPrisonerRescuedEvent:
                    PresentAftermath(); break;
                case TacticActivatedEvent or IntelligenceEffectResolvedEvent: PresentTactic(events[_index]); break;
            }
        }
        double finalCue = _visuals.Select(c => c.End).Concat(_records.Select(c => c.Time))
            .Concat(_messages.Select(c => c.Time)).Concat(_stateValues.Select(c => c.Time))
            .Concat(_mapEffects.Select(c => c.Start + c.Sprite.Columns * c.Sprite.FrameMilliseconds / 1000.0))
            .Concat(_mapNumbers.Select(c => c.Start + BattleMapNumberPlayback.Duration(content.Battle!.MapNumbers!))).DefaultIfEmpty(0).Max();
        return new BattlePresentationPlan(_messages, _records, _visuals, _stateValues, _mapEffects, _mapNumbers, content.Battle?.MapNumbers, _presented,
            _messages.Count == 0 && _records.Count == 0 && _visuals.Count == 0 && _mapEffects.Count == 0 && _mapNumbers.Count == 0 ? 0 : Math.Max(_cursor, finalCue));
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

    private Guid Identity(string role) => ProgressMessageProducer.Identity($"battle:{before.World.Revision}:{_index}:{role}");
    private void Number(BattleObjectContext? obj, long amount, double time, string role)
    {
        if (amount != 0 && obj is not null && MapVisible(obj) && content.Battle?.MapNumbers is { } digits)
        {
            double start = MapTime(time);
            _mapNumbers.Add(new(Identity(role), start, obj, amount));
            _cursor = Math.Max(_cursor, start + BattleMapNumberPlayback.Duration(digits));
        }
    }
    private void Outcome(string role, GameClock clock, TacticOccurrence occurrence, uint amount)
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
        _records.Add(new(_impact + 0.2, new(id, produced.Message, Location(target),
            target.ForceId is int force ? before.Forces.Concat(after.Forces).First(f => f.Id == force).ColorCode : null), produced.SoundRole));
    }

    private void PresentDamage(BattleDamageEvent damage)
    {
        var damageTarget = damage.Target ?? TargetContext(damage.TargetUnitId ?? damage.SettlementId!.Value, damage.SettlementId is not null);
        var damageSource = damage.Source ?? TargetContext(damage.AttackerId, damage.AttackerIsSettlement);
        if (damageSource is not null && damageTarget is not null) Capture(damageSource, damageTarget);
        double time = damage.TacticId is null ? MapTime(1.5) : _impact;
        _impact = time;
        _stateValues.Add(new(time, damage.TargetUnitId ?? damage.SettlementId!.Value,
            damage.SettlementId is not null, -(long)damage.SoldierLoss, -damage.MoraleLoss, -damage.DurabilityLoss));
        if (damage.TacticId is not null)
            Number(damageTarget, -(long)damage.SoldierLoss, time, "map-loss");
    }

    private void PresentCondition(BattleConditionChangedEvent condition)
    {
        var obj = TargetContext(condition.ObjectId, condition.IsSettlement);
        double time = obj is null ? _impact : MapTime(_impact);
        var key = (condition.ObjectId, condition.IsSettlement);
        var previous = _conditions.GetValueOrDefault(key, UnitCondition.Normal);
        _conditions[key] = condition.Condition;
        _stateValues.Add(new(time, condition.ObjectId, condition.IsSettlement, 0, 0, 0, condition.Condition));
        if (previous != condition.Condition && obj is not null && MapVisible(obj) &&
            content.Battle?.ConditionEffects.TryGetValue(condition.Condition, out var sprite) == true)
        {
            _mapEffects.Add(new(Identity("condition"), time, obj, sprite));
            _cursor = Math.Max(_cursor, time + sprite.Columns * sprite.FrameMilliseconds / 1000.0);
        }
    }

    private void PresentMorale(TacticMoraleChangedEvent morale)
    {
        _stateValues.Add(new(_impact, morale.TargetId, morale.TargetIsSettlement, 0, morale.Current - morale.Previous, 0));
        int visual = _visuals.FindLastIndex(c => c.TacticId == morale.TacticId && c.Zoom is null && !c.ScreenEffect && c.Target.Id == morale.TargetId && c.Target.IsSettlement == morale.TargetIsSettlement);
        if (visual >= 0 && morale.Current > morale.Previous) _visuals[visual] = _visuals[visual] with { MoraleGain = morale.Current - morale.Previous, End = Math.Max(_visuals[visual].End, _impact + 1.5) };
        if (visual >= 0) _cursor = Math.Max(_cursor, _visuals[visual].End);
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
        Outcome("defection", defection.Clock, defection.Occurrence, defection.SourceGain);
    }

    private void PresentMission(DomainEvent fact)
    {
        var mission = MissionPresentation.Results(before, texts, [fact], content: content.Missions, context: context, speakers: content.Decisions.People);
        if (mission.Messages.Count == 0 && mission.Records.Count == 0) return;
        _presented.Add(_index);
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
                    _visuals.Add(new(Identity($"mission-effect-{frame}"), _cursor + 0.3, _cursor + 0.3 + sprites[frame].Columns * sprites[frame].FrameMilliseconds / 1000.0, obj, obj, -1, 0, false, false, 0, 0, 0, sprites[frame], null));
            }
        }
        foreach (var message in mission.Messages)
        {
            double time = _cursor + (message.MapSpeech?.DelaySeconds ?? 2);
            var id = message.MapSpeech?.Id ?? Identity($"mission-{_messages.Count}");
            var fixedMessage = message with
            {
                MapSpeech = message.MapSpeech is { } speech ? speech with { Id = id, DelaySeconds = 0 } : null
            };
            _messages.Add(new(id, time, fixedMessage));
        }
        foreach (var record in mission.Records)
            _records.Add(new(_cursor + 2, record));
        _cursor = Math.Max(_cursor + 6, _visuals.Select(v => v.End).DefaultIfEmpty(0).Max());
    }

    private void PresentAftermath()
    {
        if (content.Progress is { } progress)
        {
            var reports = ProgressMessages.Read(before, after, context, texts, progress, events, _index);
            if (reports.Count > 0)
            {
                _presented.Add(_index);
                foreach (var report in reports) _records.Add(new(_cursor, report));
                _cursor += 1;
            }
        }
    }

    private void PresentHealing(TacticHealingEvent healing)
    {
        if (healing.Occurrence is not { } heal || healing.TacticId is not int healTactic ||
            content.Battle is not { } battle || !battle.Tactics.TryGetValue(healTactic, out var healDefinition) || !Visible(heal)) return;
        Capture(heal.Source, heal.Target);
        var sprite = healDefinition.MapEffect;
        _visuals.Add(new(Identity("healing"), _impact - (sprite?.Columns * sprite?.FrameMilliseconds ?? 0) / 1000.0, _impact + 1.5, heal.Source, heal.Target, healTactic,
            healing.ActorId ?? Soldier, false, false, 0, 0, 0, sprite, null)
        { SoldierGain = healing.RecoveredSoldiers });
        _stateValues.Add(new(_impact, healing.TargetUnitId, false, healing.RecoveredSoldiers, 0, 0));
        Number(heal.Target, healing.RecoveredSoldiers, _impact, "map-healing");
        if (healing.RecoveredSoldiers > 0) Outcome("healed", healing.Clock, heal, healing.RecoveredSoldiers);
        _cursor = Math.Max(_cursor, _impact + 1.5);
    }

    private void PresentTactic(DomainEvent fact)
    {
        if (content.Battle is not { } battle) return;
        TacticOccurrence? occurrence; int tacticId, actor, effect; GameClock clock;
        if (fact is TacticActivatedEvent activation)
        {
            occurrence = activation.Occurrence; tacticId = activation.TacticId; actor = activation.OfficerIds[0]; effect = activation.Effect; clock = activation.Clock;
        }
        else if (fact is IntelligenceEffectResolvedEvent intelligence)
        {
            occurrence = intelligence.Occurrence; tacticId = intelligence.TacticId; actor = intelligence.ActorId ?? Soldier; effect = intelligence.Effect; clock = intelligence.Clock;
        }
        else return;
        _impact = _cursor;
        if (occurrence is not null) Capture(occurrence.Source, occurrence.Target);
        if (occurrence is null || !Visible(occurrence) || !battle.Tactics.TryGetValue(tacticId, out var definition)) return;
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
        void Add(string role, DecisionMessageDefinition message, double time, BattleMessageChannel? channel = null, bool atSource = false)
        {
            var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"battle:{_index}:{role}:{_messages.Count + _records.Count}"));
            var produced = ProgressMessageProducer.Produce(message, occurrenceContext, texts, values, n => (int)(random.NextUInt64() % (ulong)n));
            var id = Identity($"{role}:{_messages.Count + _records.Count}"); var obj = atSource ? source : target;
            int? color = obj.ForceId is int force ? before.Forces.Concat(after.Forces).First(f => f.Id == force).ColorCode : null;
            if (channel == BattleMessageChannel.MapSpeech && produced.SpeakerId is int speaker)
                produced = produced with
                {
                    MapSpeech = new(id, produced.Message, speaker,
                    texts.Resolve(before.MessageSpeaker(speaker).NameKey), Location(obj), obj.IsSettlement ? null : obj.Id, color)
                };
            else if (channel is null)
            {
                _records.Add(new(time, new(id, produced.Message, Location(obj), color), produced.SoundRole));
                return;
            }
            _messages.Add(new(id, time, produced, channel!.Value));
        }
        if (fact is TacticActivatedEvent active)
        {
            bool support = definition.Support;
            string report = support ? "support" : (active.IsChain ? "chain-" : "activation-") + (Player(source.ForceId) ? "own" : "enemy");
            Add(report, battle.Messages[report], _cursor);
            if (definition.Zoom is null)
                Add("announcement", definition.Messages["announcement"], _cursor, BattleMessageChannel.Dialogue, true);
            if (active.OfficerIds.Length > 1)
                foreach (int officer in active.OfficerIds)
                { values["actor"] = ProgressMessageProducer.Person(officer); Add("link", battle.Messages["link"], _cursor); }
            for (int member = 0; member < active.OfficerIds.Length; member++)
            {
                values["actor"] = ProgressMessageProducer.Person(active.OfficerIds[member]);
                string role = member == 0 ? active.IsChain ? occurrence.TargetCondition == UnitCondition.NoFormation ? "chain-unformed" : "chain-confused" : "action" : $"link-{Math.Min(member - 1, 3)}";
                Add(role, role == "action" ? definition.Messages[role] : battle.Messages[role],
                    _cursor,
                    definition.Zoom is null ? BattleMessageChannel.MapSpeech : BattleMessageChannel.Dialogue, true);
            }
            values["actor"] = ProgressMessageProducer.Person(actor);
            double preludeEnd = _cursor;
            for (int frame = 0; frame < definition.Prelude.Length; frame++)
            {
                double frameEnd = preludeEnd + definition.Prelude[frame].Columns * definition.Prelude[frame].FrameMilliseconds / 1000.0;
                _visuals.Add(new(Identity($"prelude-{frame}"), preludeEnd, frameEnd,
                    source, target, tacticId, actor, false, false, 0, 0, 0,
                    definition.Prelude[frame] with { Sound = frame == 0 ? definition.Prelude[frame].Sound : null }, null)
                { ScreenEffect = true });
                preludeEnd = frameEnd;
            }
            if (definition.Zoom is { } zoom)
            {
                var following = events.Skip(_index + 1).TakeWhile(e => e is not TacticActivatedEvent).ToArray();
                var hit = following.OfType<BattleDamageEvent>().FirstOrDefault(e => e.TacticId == tacticId &&
                    e.AttackerId == source.Id && e.AttackerIsSettlement == source.IsSettlement &&
                    e.TargetUnitId == (target.IsSettlement ? null : target.Id) && e.SettlementId == (target.IsSettlement ? target.Id : null));
                var sniping = following.OfType<TacticSnipingResolvedEvent>().FirstOrDefault(e => e.TacticId == tacticId &&
                    e.Source.Id == source.Id && e.Source.IsSettlement == source.IsSettlement &&
                    e.Target.Id == target.Id && e.Target.IsSettlement == target.IsSettlement);
                var scene = BattleScenePreparation.Create(before, occurrence, zoom, battle, hit, sniping,
                    effect < 10, clock, _index, buildingTypes, map);
                zoom = scene.Zoom;
                var playback = scene.Playback;
                // Dialogue completion starts darkening; action and reveal share the scene boundary.
                double start = _cursor + BattleSceneTransition.DarkenDuration, end = start + playback.Duration;
                _impact = start + playback.Impacts[0].Time;
                _visuals.Add(new(Identity("zoom"), start, end, source, target, tacticId, actor, effect < 10, active.Doubled,
                    hit?.SoldierLoss ?? 0, hit?.DurabilityLoss ?? 0, hit?.MoraleLoss ?? 0, null, zoom)
                { Playback = playback, Officers = scene.Officers, Backdrop = scene.Backdrop, EntranceStart = _cursor });
                double responseStart = end - 4;
                if (effect < 10)
                {
                    Add("block-reaction", battle.Messages["block-reaction"], responseStart, BattleMessageChannel.Dialogue);
                    Add("block-reply", battle.Messages["block-reply"], responseStart, BattleMessageChannel.Dialogue, true);
                    Add("blocked", battle.Messages["blocked"], end);
                }
                else Add("hit-reaction", battle.Messages["hit-reaction"], responseStart, BattleMessageChannel.Dialogue);
                values["damage"] = new(hit?.SoldierLoss ?? 0);
                if (effect >= 10) Add("damage", battle.Messages["damage"], end);
                if (sniping is not null)
                {
                    values["actor"] = ProgressMessageProducer.Person(sniping.ActorId);
                    values["responder"] = ProgressMessageProducer.Person(sniping.OfficerId);
                    double snipingStart = start + playback.ActionEnd;
                    Add("sniping-action", battle.Messages["sniping-action"], snipingStart, BattleMessageChannel.Dialogue, true);
                    string response = sniping.Injury == 7 ? "sniping-fatal" : sniping.Injury == 4 ? "sniping-heavy" : "sniping-light";
                    Add(response, battle.Messages[response], start + playback.FeedbackStart, BattleMessageChannel.Dialogue);
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
                    values["injury"] = new(0, CommandText.Resolve(texts, sniping.Injury == 0 ? "battle.injury-healthy" : sniping.Injury <= 4 ? "battle.injury-light" : "battle.injury-heavy"));
                    string reportRole = Player(target.ForceId) ? "sniping-own" : "sniping-enemy";
                    Add(reportRole, battle.Messages[reportRole], end);
                }
                _mapAvailable = end;
                _cursor = end;
            }
            else if (support)
            {
                _impact = preludeEnd + (definition.MapEffect?.Columns * definition.MapEffect?.FrameMilliseconds ?? 0) / 1000.0;
                if (definition.EffectAtSource && definition.MapEffect is { } sprite) _visuals.Add(new(Identity("support"), preludeEnd, _impact, source, source, tacticId, actor, false, false, 0, 0, 0, sprite, null));
                Add("hit", definition.Messages["hit"], _cursor + 1, BattleMessageChannel.MapSpeech, true);
                _cursor = Math.Max(_cursor + 6, _impact);
            }
            else _cursor = preludeEnd;
        }
        else
        {
            // A failed candidate has no activation fact; it still owns an attempted action and response.
            if (fact is IntelligenceEffectResolvedEvent { IsAttempt: true })
            {
                Add("announcement", definition.Messages["announcement"], _cursor, BattleMessageChannel.Dialogue, true);
                Add("action", definition.Messages["action"], _cursor, BattleMessageChannel.MapSpeech, true);
            }
            string role = effect >= 10 ? "hit" : effect > 0 ? "reduced" : "failed";
            values["responder"] = ProgressMessageProducer.Person((effect == 0 ? occurrence.IntelligenceOfficerId : occurrence.ResistanceOfficerId) ?? Soldier);
            if (effect == 0)
            {
                if (definition.UnitedResponse && occurrence.TargetCondition == UnitCondition.HighSpirits) role = "united";
                else if (definition.CounterResponse is { } counterRole && occurrence.CounterOfficerId is int counter)
                { role = counterRole; values["responder"] = ProgressMessageProducer.Person(counter); }
                else if (occurrence.DefensiveFormation && definition.FormationResponse) Add("defensive", definition.Messages["defensive"], _cursor + 1, BattleMessageChannel.MapSpeech);
            }
            if (effect > 0 && definition.MapEffect is { } sprite)
            {
                _impact = _cursor + sprite.Columns * sprite.FrameMilliseconds / 1000.0;
                var hit = events.Skip(_index + 1).TakeWhile(e => e is not IntelligenceEffectResolvedEvent and not TacticActivatedEvent)
                    .OfType<BattleDamageEvent>().FirstOrDefault(e => e.TacticId == tacticId);
                _visuals.Add(new(Identity("effect"), _cursor, _impact + 1.5,
                    source, target, tacticId, actor, false, false, hit?.SoldierLoss ?? 0, hit?.DurabilityLoss ?? 0, hit?.MoraleLoss ?? 0, sprite, null));
            }
            Add(role, definition.Messages[role], _cursor + 1, BattleMessageChannel.MapSpeech);
            _cursor = Math.Max(_cursor + 6, _impact + 1.5);
        }
    }
}
