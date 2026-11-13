using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Application.Presentation;

internal sealed partial class BattlePresentationBuilder
{
    private WorldMessageContext TacticContext(GameClock clock, TacticOccurrence occurrence) => context.AtOccurrence(clock,
        occurrence.Source.Id == occurrence.Target.Id && occurrence.Source.IsSettlement == occurrence.Target.IsSettlement
            ? [occurrence.Source] : [occurrence.Source, occurrence.Target]);

    private Dictionary<string, MessageBinding> TacticBindings(int actor, int tacticId, TacticOccurrence occurrence)
    {
        var source = occurrence.Source; var target = occurrence.Target;
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
        return values;
    }

    private CommandMessage TacticMessage(string role, DecisionMessageDefinition message, WorldMessageContext occurrenceContext,
        Dictionary<string, MessageBinding> values)
    {
        var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"battle:{_index}:{role}"));
        return ProgressMessageProducer.Produce(message, occurrenceContext, texts, values, n => (int)(random.NextUInt64() % (ulong)n));
    }

    private CommandMessage TacticMapSpeech(CommandMessage message, string id, BattleObjectContext obj, double delay = 0)
    {
        if (message.SpeakerId is not int speaker) return message;
        int? color = obj.ForceId is int force ? before.Forces.Concat(after.Forces).First(f => f.Id == force).ColorCode : null;
        return message with { MapSpeech = new(id, message.Message, speaker,
            texts.Resolve(before.MessageSpeaker(speaker).NameKey), Location(obj), obj.IsSettlement ? null : obj.Id, color, DelaySeconds: delay) };
    }

    private static BattleDamageEvent? TacticDamage(IEnumerable<DomainEvent> following, int tacticId,
        BattleObjectContext source, BattleObjectContext target) => following.OfType<BattleDamageEvent>().FirstOrDefault(e =>
            e.TacticId == tacticId && e.AttackerId == source.Id && e.AttackerIsSettlement == source.IsSettlement &&
            e.TargetUnitId == (target.IsSettlement ? null : target.Id) && e.SettlementId == (target.IsSettlement ? target.Id : null));

    private void Outcome(string role, GameClock clock, TacticOccurrence occurrence, uint amount, int? colorForce, string valueRole = "damage")
    {
        if (content.Battle is not { } battle || !Visible(occurrence)) return;
        var source = occurrence.Source; var target = occurrence.Target;
        Dictionary<string, MessageBinding> values = new()
        {
            ["source-commander"] = ProgressMessageProducer.Person(source.CommanderId ?? Soldier),
            ["target-commander"] = ProgressMessageProducer.Person(target.CommanderId ?? Soldier),
            ["target"] = Object(target),
            [valueRole] = new(amount)
        };
        var produced = TacticMessage(role, battle.Messages[role], TacticContext(clock, occurrence), values);
        var id = Identity(role);
        AddRecordCue(new(_impact, new(id, produced.Message, Location(target),
            MapRecordColors.Resolve(before, after, colorForce)), produced.SoundRole));
    }

    private void PresentLeaderInspiration(LeaderInspiredEvent inspired)
    {
        _impact = _cursor;
        var occurrence = inspired.Occurrence;
        var source = occurrence.Source;
        Capture(source, inspired.Opponent);
        if (content.Battle is not { } battle || !battle.Tactics.TryGetValue(inspired.TacticId, out var definition)) return;

        if (Player(source.ForceId) || Player(inspired.Opponent.ForceId))
        {
            var message = TacticMessage("leader-inspiration", definition.Messages["announcement"],
                TacticContext(inspired.Clock, occurrence), TacticBindings(inspired.ActorId, inspired.TacticId, occurrence));
            AddMessageCue(new(Identity("leader-inspiration"), _cursor, message, BattleMessageChannel.Dialogue));
        }

        // The native effect has its own viewport and spatial-audio admission, independent of the dialogue gate.
        if (definition.MapEffect is { } sprite)
            _mapEffects.Add(new(Identity("leader-inspiration-effect"), _impact, source, sprite));
    }

    private void PresentMorale(TacticMoraleChangedEvent morale)
    {
        _stateValues.Add(new(_impact, morale.TargetId, morale.TargetIsSettlement, 0, morale.Current - morale.Previous, 0));
        int visual = _visuals.FindLastIndex(c => c.TacticId == morale.TacticId && c.Zoom is null && !c.ScreenEffect && c.Target.Id == morale.TargetId && c.Target.IsSettlement == morale.TargetIsSettlement);
        if (visual >= 0 && morale.Current > morale.Previous) _visuals[visual] = _visuals[visual] with { MoraleGain = morale.Current - morale.Previous };
        if (morale.Current != morale.Previous)
            Outcome(morale.Current > morale.Previous ? "morale-increased" : "morale-decreased", morale.Clock,
                morale.Occurrence, (uint)morale.Current, morale.Occurrence.Target.ForceId, valueRole: "current");
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
        if (defection.Effect >= 10)
            Outcome("defection", defection.Clock, defection.Occurrence, defection.SourceGain,
                Player(from.ForceId) ? from.ForceId : to.ForceId);
    }

    private void PresentHealing(TacticHealingEvent healing)
    {
        _stateValues.Add(new(_impact, healing.TargetUnitId, false, healing.RecoveredSoldiers, 0, 0));
        if (healing.Occurrence is not { } heal || healing.TacticId is not int healTactic ||
            content.Battle is not { } battle || !battle.Tactics.TryGetValue(healTactic, out var healDefinition)) return;
        Capture(heal.Source, heal.Target);
        if (_supportPresentationVisible)
        {
            var sprite = healDefinition.MapEffect;
            _visuals.Add(new(Identity("healing"), _impact, _impact + (sprite?.Columns * sprite?.FrameMilliseconds ?? 0) / 1000.0,
                heal.Source, heal.Target, healTactic, healing.ActorId ?? Soldier, false, false, 0, 0, 0, sprite, null)
            { SoldierGain = healing.RecoveredSoldiers });
            Number(heal.Target, healing.RecoveredSoldiers, _impact, "map-healing");
        }
        if (healing.RecoveredSoldiers > 0) Outcome("healed", healing.Clock, heal, healing.RecoveredSoldiers, heal.Source.ForceId);
        _cursor = Math.Max(_cursor, _impact);
    }

    private void PresentSecondaryPrevention(TacticSecondaryPreventedEvent prevented)
    {
        if (content.Battle is not { } battle || !battle.Tactics.TryGetValue(prevented.TacticId, out var definition)) return;
        _impact = _cursor;
        var occurrence = prevented.Occurrence;
        Capture(occurrence.Source, occurrence.Target);
        if (!Visible(occurrence)) return;
        var values = TacticBindings(prevented.ActorId, prevented.TacticId, occurrence);
        values["responder"] = ProgressMessageProducer.Person(prevented.ResponderId ?? Soldier);
        bool countered = prevented.Reason == TacticPreventionReason.PassiveCounter;
        string role = countered ? "secondary-countered" : "secondary-defensive";
        string identity = $"{role}:{_messages.Count + _records.Count}";
        var message = TacticMessage(identity, definition.Messages[role], TacticContext(prevented.Clock, occurrence), values);
        string id = Identity(identity);
        var target = occurrence.Target;
        if (countered)
            AddRecordCue(new(_cursor, new(id, message.Message, Location(target),
                MapRecordColors.Resolve(before, after, target.ForceId)), message.SoundRole));
        else AddMessageCue(new(id, _cursor, TacticMapSpeech(message, id, target), BattleMessageChannel.MapSpeech));
    }

    private void PresentTactic(DomainEvent fact)
    {
        if (content.Battle is not { } battle) return;
        TacticOccurrence? occurrence; int tacticId, actor, effect; GameClock clock;
        if (fact is TacticActivatedEvent activation)
        {
            _supportPresentationVisible = false;
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
        var occurrenceContext = TacticContext(clock, occurrence);
        var values = TacticBindings(actor, tacticId, occurrence);
        void AddRecord(string role, DecisionMessageDefinition message, double time, BattleObjectContext location, int? colorForce, MapReportImportance importance = MapReportImportance.Summary)
        {
            var produced = TacticMessage($"{role}:{_messages.Count + _records.Count}", message, occurrenceContext, values);
            var id = Identity($"{role}:{_messages.Count + _records.Count}");
            AddRecordCue(new(time, new(id, produced.Message, Location(location), MapRecordColors.Resolve(before, after, colorForce), importance), produced.SoundRole));
        }
        void Add(string role, DecisionMessageDefinition message, double time, BattleMessageChannel channel, bool atSource = false, double delay = 0)
        {
            var produced = TacticMessage($"{role}:{_messages.Count + _records.Count}", message, occurrenceContext, values);
            var id = Identity($"{role}:{_messages.Count + _records.Count}"); var obj = atSource ? source : target;
            if (channel == BattleMessageChannel.MapSpeech) produced = TacticMapSpeech(produced, id, obj, delay);
            AddMessageCue(new(id, time, produced, channel));
        }
        double Prelude()
        {
            if (definition.Prelude.Length == 0) return _cursor;
            string? preludeId = definition.Zoom is null ? Identity("prelude") : null;
            double end = _cursor;
            for (int frame = 0; frame < definition.Prelude.Length; frame++)
            {
                var sprite = definition.Prelude[frame];
                double next = end + sprite.Columns * sprite.FrameMilliseconds / 1000.0;
                _visuals.Add(new(Identity($"prelude-{frame}"), end, next,
                    source, target, tacticId, actor, false, false, 0, 0, 0,
                    sprite with { Sound = frame == 0 ? sprite.Sound : null }, null)
                { ScreenEffect = true, PreludeId = preludeId });
                end = next;
            }
            if (preludeId is not null) _preludes.Add(new(preludeId, _cursor, end, target));
            return end;
        }
        bool visible = Visible(occurrence);
        if (fact is TacticActivatedEvent supportActivation && definition.Support)
        {
            visible = DirectPlayer(source) || supportActivation.Opponent is { } opponent && DirectPlayer(opponent);
            _supportPresentationVisible = visible;
        }
        if (!visible)
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
                var hit = TacticDamage(following, tacticId, source, target);
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
            var hit = TacticDamage(events.Skip(_index + 1).TakeWhile(e => e is not IntelligenceEffectResolvedEvent and not TacticActivatedEvent),
                tacticId, source, target);
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
                _visuals.Add(new(Identity("effect"), _cursor, _cursor + sprite.Columns * sprite.FrameMilliseconds / 1000.0,
                    source, target, tacticId, actor, false, false, hit?.SoldierLoss ?? 0, hit?.DurabilityLoss ?? 0, hit?.MoraleLoss ?? 0, sprite, null));
            }
            Add(role, definition.Messages[role], _cursor, BattleMessageChannel.MapSpeech, delay: 1);
            // Reports select their own responder, independently of the visible bubble.
            values["responder"] = ProgressMessageProducer.Person((effect == 0 ? occurrence.IntelligenceOfficerId : occurrence.ResistanceOfficerId) ?? Soldier);
            values["damage"] = new(hit?.CalculatedSoldierLoss ?? hit?.SoldierLoss ?? 0);
            string report = effect == 0 ? "failed-report" : effect < 10 ? "reduced-report" : "hit-report";
            if (definition.Messages.TryGetValue(report, out var message))
                AddRecord(report, message, _cursor, target, target.ForceId);
            _cursor = Math.Max(_cursor, _impact);
        }
    }
}
