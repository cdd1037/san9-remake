using San9.Application.Presentation;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>One prepared day, retained until all its map presentation has completed.</summary>
public sealed class TurnDayPresentation(StrategySnapshot before, StrategySnapshot after, IReadOnlyList<DomainEvent> events, MapDayPlan? plan = null, double? duration = null)
{
    public const double MovementDuration = MapMovementCadence.ArrivalMilliseconds / 1000.0;
    public const double MovementMilliseconds = MovementDuration * 1000;
    public StrategySnapshot Before { get; } = before;
    public StrategySnapshot After { get; } = after;
    public IReadOnlyList<DomainEvent> Events { get; } = events;
    public double Elapsed { get; private set; }
    public double VisualElapsed { get; private set; }
    private readonly List<(double Position, double Duration)> _pauses = [];
    // Visual age since a progress timestamp was reached, including subsequent pauses.
    public double VisualAge(double progressMilliseconds) =>
        (VisualElapsed - progressMilliseconds / 1000 - _pauses.Where(p => p.Position < progressMilliseconds / 1000).Sum(p => p.Duration)) * 1000;
    public MapDayPlan? Plan { get; } = plan;
    public double Duration { get; } = Math.Max(BaseDuration(events), plan?.Duration ?? duration ?? 0);
    internal static double BaseDuration(IReadOnlyList<DomainEvent> events) =>
        events.OfType<BattleDamageEvent>().Any(e => e.SoldierLoss > 0 || e.DurabilityLoss > 0 || e.MoraleLoss > 0) ? 2.8 : 1.0;
    public double CombatMilliseconds => Elapsed < MovementDuration ? (Elapsed - MovementDuration) * 1000 : VisualAge(MovementMilliseconds);
    public double Progress => Math.Clamp(Elapsed / Duration, 0, 1);
    public void Advance(double seconds, bool paused = false)
    {
        seconds = Math.Max(0, seconds);
        if (paused)
        {
            if (_pauses.Count > 0 && _pauses[^1].Position == Elapsed)
                _pauses[^1] = (Elapsed, _pauses[^1].Duration + seconds);
            else _pauses.Add((Elapsed, seconds));
            VisualElapsed += seconds;
        }
        else
        {
            double next = Math.Min(Duration, Elapsed + seconds);
            VisualElapsed += next - Elapsed;
            Elapsed = next;
        }
    }
}
