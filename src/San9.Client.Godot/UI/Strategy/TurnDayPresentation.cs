using San9.Application.Presentation;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>One prepared day, retained until all its map presentation has completed.</summary>
public sealed class TurnDayPresentation(StrategySnapshot before, StrategySnapshot after, IReadOnlyList<DomainEvent> events, MapDayPlan? plan = null, double? duration = null, BattlePresentationPlan? battle = null)
{
    public const double MovementDuration = MapMovementCadence.ArrivalMilliseconds / 1000.0;
    public const double MovementMilliseconds = MovementDuration * 1000;
    public const double DayDuration = MapMovementCadence.DayMilliseconds / 1000.0;
    public StrategySnapshot Before { get; } = before;
    public StrategySnapshot After { get; } = after;
    public IReadOnlyList<DomainEvent> Events { get; } = events;
    public double Elapsed { get; private set; }
    public double VisualElapsed { get; private set; }
    public long PresentationRevision { get; private set; }
    private readonly List<(double Position, double Duration)> _pauses = [];
    // Visual age since a progress timestamp was reached, including subsequent pauses.
    public double VisualAge(double progressMilliseconds) =>
        (VisualElapsed - progressMilliseconds / 1000 - _pauses.Where(p => p.Position < progressMilliseconds / 1000).Sum(p => p.Duration)) * 1000;
    public MapDayPlan? Plan { get; } = plan;
    public Func<MapPresentationBounds?>? Viewport { get; init; }
    public IReadOnlyDictionary<string, ImageAsset> MapEffectAssets { get; init; } = new Dictionary<string, ImageAsset>();
    private sealed record MapEffect(string Id, double Time, double Duration, int FactIndex, string? AfterDialogueId,
        BattleObjectContext Target, BattleSprite Sprite);
    private readonly MapEffect[] _mapEffects = ReadMapEffects(battle);
    private readonly Dictionary<string, (double Start, bool Admitted)> _mapEffectStarts = [];
    private double _mapEffectEnd;
    private bool IsMapEffectBlocking => VisualElapsed < _mapEffectEnd - 1e-9;
    public bool IsMapEffectPresented(string id) => _mapEffectStarts.TryGetValue(id, out var state) && state.Admitted;
    public double MapEffectElapsed(string id) => IsMapEffectPresented(id) ? VisualElapsed - _mapEffectStarts[id].Start : -1;
    private static MapEffect[] ReadMapEffects(BattlePresentationPlan? plan) => plan is null ? [] :
        plan.MapEffects.Select(c => new MapEffect(c.Id, c.Start, c.Sprite.Columns * c.Sprite.FrameMilliseconds / 1000.0,
            c.FactIndex, c.AfterDialogueId, c.Target, c.Sprite))
        .Concat(plan.Visuals.Where(c => c.IsMapEffect && c.Effect is not null).Select(c =>
            new MapEffect(c.Id, c.Start, c.End - c.Start, c.FactIndex, c.AfterDialogueId, c.Target, c.Effect!))).ToArray();
    private readonly IReadOnlyList<BattleMapBatch> _mapBatches = battle?.MapBatches ??
        (plan?.Attacks.Count > 0 ? [new(DayDuration, plan.Attacks.Min(a => a.FactIndex), plan.Attacks.Max(a => a.FactIndex), null)] : []);
    private int _nextBatch;
    private readonly Dictionary<MapAttackMotion, (double Start, MapAttackAdmission Admission)> _attacks = [];
    private bool _batchActive;
    private double _batchEnd;
    private BattleMapBatch? NextBatch => _nextBatch < _mapBatches.Count ? _mapBatches[_nextBatch] : null;
    public bool IsFactReady(int factIndex) => NextBatch is not { } phase || factIndex <= phase.LastFactIndex;
    public bool IsAttackPresented(MapAttackMotion attack) => _attacks.TryGetValue(attack, out var state) && (state.Admission.Body || state.Admission.Shots.Count > 0);
    public bool IsBodyPresented(MapAttackMotion attack) => _attacks.TryGetValue(attack, out var state) && state.Admission.Body;
    public double AttackMilliseconds(MapAttackMotion attack) => _attacks.TryGetValue(attack, out var state) ? (VisualElapsed - state.Start) * 1000 : -1;
    public double AttackVisualStart(MapAttackMotion attack) => _attacks[attack].Start * 1000;
    public IReadOnlySet<int> PresentedShots(MapAttackMotion attack) => _attacks.TryGetValue(attack, out var state) ? state.Admission.Shots : new HashSet<int>();
    public bool IsCombatBlocking => _batchActive && VisualElapsed < _batchEnd - 1e-9;
    public BattlePresentationPlan? Battle { get; } = battle;
    private BattleMapFrame? _mapFrame;
    private (int Changes, int Values) _mapVersion = (-1, -1);
    public BattleMapFrame MapFrame
    {
        get
        {
            var changes = Battle?.MapChanges.Where(c => c.Time <= Elapsed && IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId)) ?? [];
            var values = Battle?.Values.Where(c => c.IsSettlement && c.Time <= Elapsed && IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId)) ?? [];
            var version = (changes.Count(), values.Count());
            if (_mapFrame is null || _mapVersion != version)
            {
                _mapFrame = new(Before, changes, values);
                _mapVersion = version;
            }
            return _mapFrame;
        }
    }
    private readonly HashSet<string> _completedDialogues = [];
    public bool IsDialogueComplete(string? id) => id is null || _completedDialogues.Contains(id);
    public double DuelDialogueElapsed { get; private set; }
    public IReadOnlyList<BattleMessageCue> PendingDialogues => Battle?.Messages
        .Where(c => c.Channel == BattleMessageChannel.Dialogue && !_completedDialogues.Contains(c.Id) && IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId) && c.Time <= Elapsed + 1e-9)
        .OrderBy(c => c.Time).ToArray() ?? [];
    public IReadOnlyList<BattleMessageCue> PendingDuelDialogues => Battle?.Messages
        .Where(c => c.Channel == BattleMessageChannel.DuelDialogue && !_completedDialogues.Contains(c.Id) && IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId) && c.Time <= Elapsed + 1e-9)
        .OrderBy(c => c.Time).ToArray() ?? [];
    private double? NextDialogueTime => Battle?.Messages
        .Where(c => c.Channel != BattleMessageChannel.MapSpeech && !_completedDialogues.Contains(c.Id))
        .Select(c => (double?)c.Time).Min();
    public IReadOnlyList<BattleMessageCue> ReadyMapSpeeches => Battle?.Messages
        .Where(c => c.Channel == BattleMessageChannel.MapSpeech && (c.Time <= Elapsed || IsComplete) && IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId)).ToArray() ?? [];
    public IReadOnlyList<BattleRecordCue> ReadyRecords => Battle?.Records
        .Where(c => (c.Time <= Elapsed || IsComplete) && IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId)).ToArray() ?? [];
    public bool IsComplete => Elapsed >= Duration && NextBatch is null && NextDialogueTime is null &&
        !IsMapEffectBlocking && _mapEffectStarts.Count == _mapEffects.Length;
    public void CompleteDialogue(string id)
    {
        if (!PendingDialogues.Concat(PendingDuelDialogues).Any(c => c.Id == id)) throw new InvalidOperationException("Battle dialogue is not pending.");
        _completedDialogues.Add(id);
        PresentationRevision++;
        DuelDialogueElapsed = 0;
    }
    public double Duration { get; } = Math.Max(battle?.Duration ?? 0, Math.Max(MapMovementCadence.DayMilliseconds / 1000.0, duration ?? 0));
    public void SkipVisual()
    {
        var duel = Battle?.Duels.FirstOrDefault(c => IsFactReady(c.FactIndex) && Elapsed >= c.Start && Elapsed < c.End);
        if (duel is not null)
        {
            DuelDialogueElapsed = 0;
            foreach (var message in Battle!.Messages.Where(c => c.SceneId == duel.Id)) _completedDialogues.Add(message.Id);
            Advance(duel.End - Elapsed); return;
        }
        var current = Battle?.Visuals.FirstOrDefault(c => IsFactReady(c.FactIndex) && c.Zoom is not null && Elapsed >= (c.EntranceStart ?? c.Start) && Elapsed < c.End);
        if (current is not null) Advance(current.End - Elapsed);
    }
    public double Progress => Math.Clamp(Elapsed / Duration, 0, 1);
    public void Advance(double seconds, bool paused = false)
    {
        seconds = Math.Max(0, seconds);
        if (paused)
        {
            Hold(seconds);
            return;
        }
        while (true)
        {
            StartMapEffects();
            if (_batchActive)
            {
                double held = Math.Min(seconds, Math.Max(0, Math.Max(_batchEnd, _mapEffectEnd) - VisualElapsed));
                Hold(held); seconds -= held;
                if (IsCombatBlocking || IsMapEffectBlocking) return;
                _nextBatch++;
                PresentationRevision++;
                _batchActive = false;
            }
            if (NextBatch is { } phase && phase.Time <= Elapsed + 1e-9)
            {
                if (!IsDialogueComplete(phase.AfterDialogueId)) return;
                _batchActive = true; _batchEnd = VisualElapsed;
                PresentationRevision++;
                var viewport = Viewport?.Invoke();
                Dictionary<int, double> bodies = [];
                foreach (var attack in Plan?.Attacks.Where(a => a.FactIndex >= phase.FirstFactIndex && a.FactIndex <= phase.LastFactIndex) ?? [])
                {
                    var admitted = viewport is { } bounds ? Plan!.Admit(attack, bounds) : new MapAttackAdmission(false, new HashSet<int>(), 0, 0);
                    _attacks.Add(attack, (VisualElapsed, admitted));
                    // A later notification replaces this unit's body action, while every
                    // admitted projectile retains its independent lifetime.
                    if (admitted.Body) bodies[attack.SourceId] = admitted.BodyBlockingDuration;
                    _batchEnd = Math.Max(_batchEnd, VisualElapsed + admitted.ProjectileBlockingDuration);
                }
                _batchEnd = Math.Max(_batchEnd, VisualElapsed + bodies.Values.DefaultIfEmpty(0).Max());
                continue;
            }
            if (IsMapEffectBlocking)
            {
                double held = Math.Min(seconds, _mapEffectEnd - VisualElapsed);
                Hold(held); seconds -= held;
                if (IsMapEffectBlocking) return;
                continue;
            }
            double remaining = NextBatch is { } next ? Math.Max(0, next.Time - Elapsed) : seconds;
            double? effectTime = _mapEffects.Where(c => !_mapEffectStarts.ContainsKey(c.Id) && IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId))
                .Select(c => (double?)c.Time).Min();
            if (effectTime is { } start) remaining = Math.Min(remaining, Math.Max(0, start - Elapsed));
            double consumed = AdvanceTimeline(Math.Min(seconds, remaining));
            seconds -= consumed;
            if (NextBatch is { } due && due.Time <= Elapsed + 1e-9) continue;
            if (effectTime is { } effectStart && effectStart <= Elapsed + 1e-9) continue;
            return;
        }
    }
    private void StartMapEffects()
    {
        foreach (var cue in _mapEffects.Where(c => !_mapEffectStarts.ContainsKey(c.Id) && c.Time <= Elapsed + 1e-9 &&
            IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId)))
        {
            bool admitted = Plan is not null && Viewport?.Invoke() is { } bounds &&
                MapEffectAssets.TryGetValue(cue.Sprite.Asset, out var asset) && Plan.AdmitMapEffect(cue.Target, cue.Sprite, asset, bounds);
            _mapEffectStarts.Add(cue.Id, (VisualElapsed, admitted));
            if (admitted) _mapEffectEnd = Math.Max(_mapEffectEnd, VisualElapsed + cue.Duration);
            PresentationRevision++;
        }
    }
    private void Hold(double seconds)
    {
        if (seconds <= 0) return;
        if (_pauses.Count > 0 && _pauses[^1].Position == Elapsed)
            _pauses[^1] = (Elapsed, _pauses[^1].Duration + seconds);
        else _pauses.Add((Elapsed, seconds));
        VisualElapsed += seconds;
    }
    private double AdvanceTimeline(double seconds)
    {
        double next = Math.Min(Duration, Elapsed + seconds);
        if (NextDialogueTime is { } boundary) next = Math.Min(next, Math.Max(Elapsed, boundary));
        double consumed = next - Elapsed;
        VisualElapsed += consumed;
        Elapsed = next;
        if (PendingDuelDialogues.Count > 0) DuelDialogueElapsed += seconds - consumed;
        return consumed;
    }
}
