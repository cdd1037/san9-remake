using San9.Application.Presentation;
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
    private readonly IReadOnlyList<BattleAttackPhase> _attackPhases = battle?.Attacks ??
        (plan?.Attacks.Count > 0 ? [new(DayDuration, plan.Attacks.Min(a => a.FactIndex), plan.Attacks.Max(a => a.FactIndex), null)] : []);
    private int _nextAttackPhase;
    private readonly Dictionary<MapAttackMotion, (double Start, MapAttackAdmission Admission)> _attacks = [];
    private bool _attackPhaseActive;
    private double _phaseEnd;
    private BattleAttackPhase? NextAttackPhase => _nextAttackPhase < _attackPhases.Count ? _attackPhases[_nextAttackPhase] : null;
    public bool IsFactReady(int factIndex) => NextAttackPhase is not { } phase || factIndex <= phase.LastFactIndex;
    public bool IsAttackPresented(MapAttackMotion attack) => _attacks.TryGetValue(attack, out var state) && (state.Admission.Body || state.Admission.Shots.Count > 0);
    public bool IsBodyPresented(MapAttackMotion attack) => _attacks.TryGetValue(attack, out var state) && state.Admission.Body;
    public double AttackMilliseconds(MapAttackMotion attack) => _attacks.TryGetValue(attack, out var state) ? (VisualElapsed - state.Start) * 1000 : -1;
    public double AttackVisualStart(MapAttackMotion attack) => _attacks[attack].Start * 1000;
    public IReadOnlySet<int> PresentedShots(MapAttackMotion attack) => _attacks.TryGetValue(attack, out var state) ? state.Admission.Shots : new HashSet<int>();
    public bool IsCombatBlocking => _attackPhaseActive && VisualElapsed < _phaseEnd - 1e-9;
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
        .Where(c => c.Channel == BattleMessageChannel.MapSpeech && c.Time <= Elapsed && IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId)).ToArray() ?? [];
    public IReadOnlyList<BattleRecordCue> ReadyRecords => Battle?.Records
        .Where(c => c.Time <= Elapsed && IsFactReady(c.FactIndex) && IsDialogueComplete(c.AfterDialogueId)).ToArray() ?? [];
    public bool IsComplete => Elapsed >= Duration && NextAttackPhase is null && NextDialogueTime is null;
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
            if (_attackPhaseActive)
            {
                double held = Math.Min(seconds, Math.Max(0, _phaseEnd - VisualElapsed));
                Hold(held); seconds -= held;
                if (IsCombatBlocking) return;
                _nextAttackPhase++;
                PresentationRevision++;
                _attackPhaseActive = false;
            }
            if (NextAttackPhase is { } phase && phase.Time <= Elapsed + 1e-9)
            {
                if (!IsDialogueComplete(phase.AfterDialogueId)) return;
                _attackPhaseActive = true; _phaseEnd = VisualElapsed;
                PresentationRevision++;
                var viewport = Viewport?.Invoke();
                foreach (var attack in Plan?.Attacks.Where(a => a.FactIndex >= phase.FirstFactIndex && a.FactIndex <= phase.LastFactIndex) ?? [])
                {
                    var admitted = viewport is { } bounds ? Plan!.Admit(attack, bounds) : new MapAttackAdmission(false, new HashSet<int>(), 0);
                    _attacks.Add(attack, (VisualElapsed, admitted));
                    _phaseEnd = Math.Max(_phaseEnd, VisualElapsed + admitted.BlockingDuration);
                }
                continue;
            }
            double remaining = NextAttackPhase is { } next ? Math.Max(0, next.Time - Elapsed) : seconds;
            double consumed = AdvanceTimeline(Math.Min(seconds, remaining));
            seconds -= consumed;
            if (NextAttackPhase is { } due && due.Time <= Elapsed + 1e-9) continue;
            return;
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
