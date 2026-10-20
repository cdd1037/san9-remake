using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Application.Presentation;

internal sealed partial class BattlePresentationBuilder
{
    private readonly List<DuelSceneCue> _duels = [];

    private void PresentDuelDeath(OfficerDiedEvent death)
    {
        var unit = death.PreviousUnitId is int unitId ? TargetContext(unitId, false) : null;
        if (content.Battle?.Duel is not { } definition || !(Player(death.PreviousForceId) || unit is not null && MapVisible(unit))) return;
        var message = ProgressMessageProducer.Produce(definition.Messages["death"], context.ForWorld(before.World).AtOccurrence(death.Clock),
            texts, new Dictionary<string, MessageBinding> { ["actor"] = ProgressMessageProducer.Person(death.OfficerId) }, _ => 0);
        // The duel death line replaces only the generic casualty announcement.
        _ = FeedbackMessages(FeedbackKind.Casualty).ToArray();
        _messages.Add(new(Identity("duel-death"), _cursor, message, BattleMessageChannel.Dialogue));
        PresentAftermath();
        _cursor += .5;
        if (PublishFeedback(_cursor, FeedbackKind.WorldEvent)) _cursor += .5;
    }

    private void PresentDuel(DuelStartedEvent started)
    {
        var roster = started.Roster;
        var source = TargetContext(roster.AttackingUnitId, false);
        var target = TargetContext(roster.DefendingUnitId, false);
        if (content.Battle?.Duel is not { } definition || source is null || target is null ||
            !(Player(source.ForceId) || Player(target.ForceId))) return;
        Capture(source, target);
        var people = before.World.Officers.ToDictionary(p => p.Id);
        foreach (var person in started.Participants) people[person.Id] = person;
        var participantIds = started.Participants.Select(p => p.Id).ToHashSet();
        // Keep valid treasure references, but discard these participants' obsolete holdings.
        var treasures = before.World.Treasures.ToDictionary(t => t.Id,
            t => t.HolderId is int holder && participantIds.Contains(holder) ? t with { HolderId = null } : t);
        foreach (var treasure in started.Treasures) treasures[treasure.Id] = treasure;
        var occurrenceWorld = before.World with { Treasures = treasures.Values.ToImmutableArray() };
        WorldMessageContext OccurrenceContext() => context.ForWorld(occurrenceWorld with { Officers = people.Values.ToImmutableArray() })
            .AtOccurrence(started.Clock, source, target);
        var occurrenceContext = OccurrenceContext();
        var abilities = new OfficerAbilityRules(new(before.Information.Treasures.Select(t => t.ToRule())),
            new(before.Information.Offices.Select(o => new GovernorOfficeRule(o.Id, o.CommandCapacity, o.GovernorPriority) { AbilityBonuses = o.AbilityBonuses })));
        DuelFighterView Fighter(int id, int hp = 100) => new(id, people[id].IsFemale,
            abilities.Evaluate(people[id], occurrenceWorld.Treasures).War, people[id].Injury, hp);
        var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, Identity("duel")));
        int Next(int n) => (int)(random.NextUInt64() % (ulong)n);
        string id = Identity("duel");
        double start = Math.Max(_cursor, _mapAvailable);
        double time = start + (duelScenesEnabled ? BattleSceneTransition.DarkenDuration : 0);
        var left = Fighter(roster.Attackers[0]); var right = Fighter(roster.Defenders[0]);
        List<DuelStage> stages = [];
        List<DuelParticleCue> particles = [];
        var flagRandom = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, id + ":flags"));
        int flagDirection = (int)(flagRandom.NextUInt64() % 8);
        var flags = new List<DuelFlagView>();
        if (duelScenesEnabled)
            foreach (var (positions, forceId) in new[] { (definition.Flags.Left, source.ForceId), (definition.Flags.Right, target.ForceId) })
            {
                var force = before.Forces.Single(f => f.Id == forceId);
                flags.AddRange(positions.Select(p => new DuelFlagView(p, force.ColorCode, force.FlagGlyph, flagDirection, (int)(flagRandom.NextUInt64() % 4))));
            }
        void Particle(DuelParticleKind kind, bool onRight)
        {
            if (!duelScenesEnabled) return;
            var effect = definition.Particles.Single(p => p.Kind == kind);
            particles.Add(new(time, time + effect.Animation.Duration, effect, onRight));
        }
        double Duration(bool onLeft, DuelAction action) => definition.Bodies.Single(b => b.Female == (onLeft ? left : right).Female && b.Right != onLeft).Actions[action].Duration;
        double leftHpStart = time, rightHpStart = time, leftHpFrom = left.HitPoints, rightHpFrom = right.HitPoints;
        var leftPosition = definition.Positions.LeftEntry; var rightPosition = definition.Positions.RightEntry;
        float leftOpacity = 0, rightOpacity = 0;
        DuelAction leftAction = DuelAction.Idle, rightAction = DuelAction.Idle;
        double leftAnimationStart = time, rightAnimationStart = time;
        DuelAudienceMood leftAudience = DuelAudienceMood.Idle, rightAudience = DuelAudienceMood.Idle;
        double leftAudienceStart = time, rightAudienceStart = time;
        List<(CommandMessage Message, BattleObjectContext Owner, string Role, int Index)> victories = [];
        CommandMessage Message(string role, int actor, int opponent)
        {
            var values = new Dictionary<string, MessageBinding>
            {
                ["actor"] = ProgressMessageProducer.Person(actor), ["opponent"] = ProgressMessageProducer.Person(opponent),
                ["source-commander"] = ProgressMessageProducer.Person(source.CommanderId ?? roster.Attackers[0]),
                ["target-commander"] = ProgressMessageProducer.Person(target.CommanderId ?? roster.Defenders[0]),
                ["force"] = ProgressMessageProducer.Force((roster.Attackers.Contains(actor) ? source : target).ForceId!.Value)
            };
            var speechRandom = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"{id}:{_index}:{role}:{actor}:{opponent}"));
            return ProgressMessageProducer.Produce(definition.Messages[role], occurrenceContext, texts, values, n => (int)(speechRandom.NextUInt64() % (ulong)n));
        }
        void Add(DuelStageKind kind, double duration, DuelAction? la = null, DuelAction? ra = null,
            string? sound = null, CommandMessage? dialogue = null, BattleSprite[]? effects = null, int? leftHp = null, int? rightHp = null,
            BattlePoint? leftTo = null, BattlePoint? rightTo = null, float? leftAlpha = null, float? rightAlpha = null)
        {
            if (!duelScenesEnabled) return;
            if (stages.Count > 0)
            {
                if (stages[^1].LeftAudience != leftAudience) leftAudienceStart = time;
                if (stages[^1].RightAudience != rightAudience) rightAudienceStart = time;
            }
            if (la is { } nextLeft) { leftAction = nextLeft; leftAnimationStart = time; }
            if (ra is { } nextRight) { rightAction = nextRight; rightAnimationStart = time; }
            if (leftHp is { } lh && lh != left.HitPoints) { leftHpFrom = left.HitPoints; leftHpStart = time; }
            if (rightHp is { } rh && rh != right.HitPoints) { rightHpFrom = right.HitPoints; rightHpStart = time; }
            stages.Add(new(time, time + duration, kind, left, right, leftAction, rightAction, leftHp ?? left.HitPoints, rightHp ?? right.HitPoints,
                sound is null ? null : definition.Sounds[sound], dialogue, (effects ?? []).ToImmutableArray())
            {
                LeftFrom = leftPosition, LeftTo = leftTo ?? leftPosition, RightFrom = rightPosition, RightTo = rightTo ?? rightPosition,
                LeftOpacityFrom = leftOpacity, LeftOpacityTo = leftAlpha ?? leftOpacity, RightOpacityFrom = rightOpacity, RightOpacityTo = rightAlpha ?? rightOpacity,
                LeftAnimationAge = time - leftAnimationStart, RightAnimationAge = time - rightAnimationStart,
                LeftAudience = leftAudience, RightAudience = rightAudience,
                LeftAudienceAge = time - leftAudienceStart, RightAudienceAge = time - rightAudienceStart,
                LeftHitPointsStart = leftHpStart, RightHitPointsStart = rightHpStart,
                LeftHitPointsFrom = leftHpFrom, RightHitPointsFrom = rightHpFrom
            });
            leftPosition = leftTo ?? leftPosition; rightPosition = rightTo ?? rightPosition;
            leftOpacity = leftAlpha ?? leftOpacity; rightOpacity = rightAlpha ?? rightOpacity;
            if (dialogue is not null) _messages.Add(new($"{id}:speech:{stages.Count}", time, dialogue, BattleMessageChannel.DuelDialogue) { SceneId = id });
            time += duration;
        }
        void Speech(string role, int actor, int opponent, DuelAction? la = null, DuelAction? ra = null, string? sound = null)
        {
            if (duelScenesEnabled) Add(DuelStageKind.Dialogue, 2.4, la, ra, sound, dialogue: Message(role, actor, opponent));
        }
        void Preparation(bool onLeft, DuelAction action, string sound)
        {
            if (!duelScenesEnabled) return;
            double duration = Duration(onLeft, action);
            Add(DuelStageKind.Preparation, duration, onLeft ? action : DuelAction.Idle, onLeft ? DuelAction.Idle : action, sound);
        }
        void EntryPose(bool onLeft)
        {
            if (!duelScenesEnabled) return;
            bool prepared = Next(2) == 0;
            Preparation(onLeft, prepared ? DuelAction.EntryPreparation : DuelAction.Challenge, prepared ? "entry-prepare" : "entry");
        }
        void Effect(BattleSprite[] effects, string sound = "resolution-effect") => Add(DuelStageKind.Effect,
            effects.Sum(e => e.Columns * e.FrameMilliseconds) / 1000.0 + DuelSceneCue.EffectFadeDuration, sound: sound, effects: effects);
        void MoveToCombat()
        {
            leftAudience = rightAudience = DuelAudienceMood.Idle;
            if (leftPosition == definition.Positions.LeftCombat && rightPosition == definition.Positions.RightCombat) return;
            double duration = Math.Max(Math.Abs(leftPosition.X - definition.Positions.LeftCombat.X),
                Math.Abs(rightPosition.X - definition.Positions.RightCombat.X)) / (double)definition.Positions.MovementPixelsPerSecond;
            Add(DuelStageKind.Approach, duration,
                leftPosition == definition.Positions.LeftCombat ? DuelAction.Idle : DuelAction.Return,
                rightPosition == definition.Positions.RightCombat ? DuelAction.Idle : DuelAction.Return, "return",
                leftTo: definition.Positions.LeftCombat, rightTo: definition.Positions.RightCombat);
        }
        void Welcome(bool onLeft, bool replacement, string role, int actor, int opponent)
        {
            EntryPose(onLeft);
            leftAudience = onLeft ? DuelAudienceMood.Cheer : DuelAudienceMood.Idle;
            rightAudience = onLeft ? DuelAudienceMood.Idle : DuelAudienceMood.Cheer;
            int leftHp = replacement && !onLeft ? Math.Min(100, left.HitPoints + 20) : left.HitPoints;
            int rightHp = replacement && onLeft ? Math.Min(100, right.HitPoints + 20) : right.HitPoints;
            if (replacement) Particle(DuelParticleKind.Cheer, !onLeft);
            Add(DuelStageKind.Dialogue, 2.4, DuelAction.Idle, DuelAction.Idle, "cheer",
                dialogue: duelScenesEnabled ? Message(role, actor, opponent) : null, leftHp: leftHp, rightHp: rightHp);
            left = left with { HitPoints = leftHp }; right = right with { HitPoints = rightHp };
        }
        if (duelScenesEnabled)
        {
            _messages.Add(new($"{id}:introduction", start, Message("introduction", left.Id, right.Id), BattleMessageChannel.Dialogue) { SceneId = id });
            Add(DuelStageKind.Entry, 1, DuelAction.Idle, DuelAction.Idle, leftAlpha: 1);
            Welcome(true, false, "entry", left.Id, target.CommanderId ?? right.Id);
            Add(DuelStageKind.Entry, 1, DuelAction.Idle, DuelAction.Idle, rightAlpha: 1);
            Welcome(false, false, "answer", right.Id, left.Id);
        }
        int li = 0, ri = 0, leftAdvantage = 0, rightAdvantage = 0;
        while (_index + 1 < events.Count && events[_index + 1] is DuelStrikeEvent or DuelPairWonEvent)
        {
            _index++;
            if (events[_index] is DuelStrikeEvent strike)
            {
                for (int p = 0; p < particles.Count; p++)
                    if (particles[p].End > time) particles[p] = particles[p] with { End = time };
                MoveToCombat();
                leftAction = rightAction = DuelAction.Idle;
                leftAnimationStart = rightAnimationStart = time;
                bool attackLeft = strike.AttackerId == left.Id;
                bool strong = strike.Kind is DuelStrikeKind.DoubleStrike or DuelStrikeKind.FinishingStrike;
                bool finisher = strike.Kind == DuelStrikeKind.FinishingStrike;
                if (duelScenesEnabled)
                {
                    if (strong) Speech(finisher ? "finisher" : "strong", strike.AttackerId, strike.DefenderId);
                    else if (strike.Kind == DuelStrikeKind.Parry)
                    {
                        if (Next(100) < 8) Speech("strong", strike.AttackerId, strike.DefenderId);
                        else if (Next(100) < 4) Speech("finisher", strike.AttackerId, strike.DefenderId);
                    }
                    if (strong) Preparation(attackLeft, finisher ? DuelAction.FinisherPreparation : DuelAction.Prepare, finisher ? "finisher" : "prepare");
                    if (finisher) Effect(definition.Finishers[(attackLeft ? 0 : 4) + (left.Female ? 2 : 0) + (right.Female ? 1 : 0)], "finisher-effect");
                }
                if (duelScenesEnabled && !strong && Next(3) == 0)
                    for (int exchange = 0, count = Next(3) == 0 ? 2 : 1; exchange < count; exchange++)
                    {
                        bool feintLeft = Next(2) == 0;
                        var feint = Next(2) == 0 ? DuelAction.Attack : DuelAction.HeavyAttack;
                        Add(DuelStageKind.Feint, feint == DuelAction.Attack ? .2 : .1,
                            feintLeft ? feint : DuelAction.Idle, feintLeft ? DuelAction.Idle : feint);
                        Add(DuelStageKind.Feint, Duration(!feintLeft, DuelAction.Parry), feintLeft ? null : DuelAction.Parry, feintLeft ? DuelAction.Parry : null, "parry");
                    }
                var attack = Next(2) == 0 ? DuelAction.Attack : DuelAction.HeavyAttack;
                double hitDelay = attack == DuelAction.Attack ? .3 : .2;
                if (strike.Kind == DuelStrikeKind.Parry) hitDelay -= .1;
                Add(DuelStageKind.Strike, hitDelay, attackLeft ? attack : DuelAction.Idle, attackLeft ? DuelAction.Idle : attack);
                var reaction = strike.Kind == DuelStrikeKind.Parry ? DuelAction.Parry : strike.DefenderHitPoints == 0 ? DuelAction.Defeated : DuelAction.Hit;
                if (people[strike.DefenderId].Injury != strike.DefenderInjury)
                {
                    people[strike.DefenderId] = people[strike.DefenderId] with { Injury = strike.DefenderInjury };
                    occurrenceContext = OccurrenceContext();
                }
                if (attackLeft) right = Fighter(right.Id, right.HitPoints); else left = Fighter(left.Id, left.HitPoints);
                if (strong) Particle(DuelParticleKind.StrongHit, !attackLeft);
                // Only a fatal hit waits for the HP gauge; ordinary body animations continue across stages.
                double hpDuration = DuelSceneCue.HitPointsDuration((attackLeft ? right : left).HitPoints, strike.DefenderHitPoints);
                Add(DuelStageKind.Impact, strike.DefenderHitPoints == 0 ? Math.Max(.3, hpDuration) : .3, attackLeft ? null : reaction, attackLeft ? reaction : null,
                    reaction == DuelAction.Parry ? "entry" : reaction == DuelAction.Defeated ? "defeat" : strong ? "strong" : attack == DuelAction.Attack ? "hit" : "heavy-hit",
                    leftHp: attackLeft ? left.HitPoints : strike.DefenderHitPoints, rightHp: attackLeft ? strike.DefenderHitPoints : right.HitPoints);
                if (attackLeft) right = right with { HitPoints = strike.DefenderHitPoints }; else left = left with { HitPoints = strike.DefenderHitPoints };
                bool advantage = strike.Kind != DuelStrikeKind.Parry && strike.Damage > 0 && strike.DefenderHitPoints < (attackLeft ? left : right).HitPoints;
                if (attackLeft) leftAdvantage = advantage ? leftAdvantage + 1 : 0;
                else rightAdvantage = advantage ? rightAdvantage + 1 : 0;
                if (strike.DefenderHitPoints > 0)
                {
                    if ((attackLeft ? leftAdvantage : rightAdvantage) >= 3) Effect(definition.Resolution);
                    var moods = DuelSceneCue.AudienceReaction(left.HitPoints, right.HitPoints, attackLeft, strike.Kind,
                        attackLeft ? leftAdvantage : rightAdvantage);
                    leftAudience = moods.Left; rightAudience = moods.Right;
                    if (leftAudience != DuelAudienceMood.Idle || rightAudience != DuelAudienceMood.Idle)
                    {
                        if (strong)
                        {
                            Particle(DuelParticleKind.Cheer, !attackLeft);
                            Particle(DuelParticleKind.Dismay, attackLeft);
                        }
                        else if ((attackLeft ? leftAdvantage : rightAdvantage) >= 3) Particle(DuelParticleKind.Cheer, !attackLeft);
                        bool soundLeft = strong && Player((attackLeft ? target : source).ForceId) ? !attackLeft : attackLeft;
                        Add(DuelStageKind.Audience, .8, sound: strong ? (soundLeft ? leftAudience : rightAudience) == DuelAudienceMood.Cheer ? "cheer" : "despair" : null);
                    }
                    leftAction = rightAction = DuelAction.Idle;
                    leftAnimationStart = rightAnimationStart = time;
                    double healthEnd = Math.Max(leftHpStart + DuelSceneCue.HitPointsDuration(leftHpFrom, left.HitPoints),
                        rightHpStart + DuelSceneCue.HitPointsDuration(rightHpFrom, right.HitPoints));
                    if (healthEnd > time) Add(DuelStageKind.Recovery, healthEnd - time);
                    if (strong || strike.Kind == DuelStrikeKind.Parry)
                        Speech(strike.Kind == DuelStrikeKind.Parry ? "parry" : finisher ? "finisher-hit" : "strong-hit", strike.DefenderId, strike.AttackerId);
                }
            }
            else if (events[_index] is DuelPairWonEvent won)
            {
                bool leftWon = won.WinnerId == left.Id;
                if (leftWon) left = left with { HitPoints = won.RemainingHitPoints }; else right = right with { HitPoints = won.RemainingHitPoints };
                leftAudience = leftWon ? DuelAudienceMood.Cheer : DuelAudienceMood.Dismay;
                rightAudience = leftWon ? DuelAudienceMood.Dismay : DuelAudienceMood.Cheer;
                Add(DuelStageKind.Audience, .8, sound: Player((leftWon ? target : source).ForceId) ? "despair" : "cheer");
                Effect(definition.Resolution);
                Add(DuelStageKind.Victory, Duration(leftWon, DuelAction.Victory), leftWon ? DuelAction.Victory : null, leftWon ? null : DuelAction.Victory);
                Particle(DuelParticleKind.Cheer, !leftWon);
                Particle(DuelParticleKind.Dismay, leftWon);
                Speech("victory", won.WinnerId, won.LoserId, sound: Player((leftWon ? source : target).ForceId) ? "win" : "lose");
                // Results use their own stable choice stream, unaffected by the scene preference.
                var winner = leftWon ? source : target;
                var loser = leftWon ? target : source;
                if (Player(winner.ForceId))
                    victories.Add((Message("won", won.WinnerId, won.LoserId), winner, "won", _index));
                if (Player(loser.ForceId))
                    victories.Add((Message("lost", won.LoserId, won.WinnerId), loser, "lost", _index));
                if (leftWon) ri++; else li++;
                if (li < roster.Attackers.Length && ri < roster.Defenders.Length)
                {
                    Add(DuelStageKind.Departure, 1, leftAlpha: leftWon ? 1 : 0, rightAlpha: leftWon ? 0 : 1);
                    if (leftWon) { right = Fighter(roster.Defenders[ri]); rightPosition = definition.Positions.RightEntry; }
                    else { left = Fighter(roster.Attackers[li]); leftPosition = definition.Positions.LeftEntry; }
                    if (leftWon) { rightHpFrom = right.HitPoints; rightHpStart = time; }
                    else { leftHpFrom = left.HitPoints; leftHpStart = time; }
                    leftAudience = rightAudience = DuelAudienceMood.Idle;
                    Add(DuelStageKind.Entry, 1, DuelAction.Idle, DuelAction.Idle, leftAlpha: 1, rightAlpha: 1);
                    Welcome(!leftWon, true, "entry", leftWon ? right.Id : left.Id, won.LoserId);
                    leftAdvantage = rightAdvantage = 0;
                }
            }
        }
        Add(DuelStageKind.Exit, .7, leftAlpha: 0, rightAlpha: 0);
        if (duelScenesEnabled)
        {
            var cell = Location(target);
            int terrain = map is null ? 0 : map.Squares[cell.Y * map.Width + cell.X].Terrain;
            string backdrop = definition.Backdrops.FirstOrDefault(b => b.Terrains.Contains(terrain) && b.Seasons.Contains((started.Clock.Month - 1) / 3))?.Asset ?? definition.DefaultBackdrop;
            _duels.Add(new(id, start, time, definition, backdrop, roster.Attackers, roster.Defenders, stages.ToImmutableArray())
            { Flags = flags.ToImmutableArray(), Particles = particles.ToImmutableArray() });
        }
        foreach (var (message, owner, role, index) in victories)
        {
            _records.Add(new(time, new($"{id}:result:{index}:{role}", message.Message, Location(owner), MapRecordColors.Resolve(before, after, owner.ForceId))));
        }
        if (victories.Count > 0)
            _messages.Add(new($"{id}:result", time, victories[^1].Message, BattleMessageChannel.Dialogue));
        _cursor = _impact = _mapAvailable = time + .5;
    }
}
