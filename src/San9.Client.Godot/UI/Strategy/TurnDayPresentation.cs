using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>One prepared day, retained until all its map presentation has completed.</summary>
public sealed class TurnDayPresentation(StrategySnapshot before, StrategySnapshot after, IReadOnlyList<DomainEvent> events)
{
    public StrategySnapshot Before { get; } = before;
    public StrategySnapshot After { get; } = after;
    public IReadOnlyList<DomainEvent> Events { get; } = events;
    public bool HasCombat { get; } = events.OfType<BattleDamageEvent>().Any(e => e.SoldierLoss > 0 || e.DurabilityLoss > 0 || e.MoraleLoss > 0);
    public double Elapsed { get; private set; }
    public double Duration { get; private set; } = events.OfType<BattleDamageEvent>().Any(e => e.SoldierLoss > 0 || e.DurabilityLoss > 0 || e.MoraleLoss > 0) ? 2.8 : 0.9;
    public double MovementProgress => Math.Clamp(Elapsed / (HasCombat ? 0.9 : Duration), 0, 1);
    public double CombatMilliseconds => (Elapsed - 0.9) * 1000;
    public double Progress => Math.Clamp(Elapsed / Duration, 0, 1);
    public void RequireDuration(double seconds) => Duration = Math.Max(Duration, seconds);
    public void Advance(double seconds) => Elapsed = Math.Min(Duration, Elapsed + Math.Max(0, seconds));
}
