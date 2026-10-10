using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public enum CommandMapTargetKind { Settlement, Unit }
public enum CommandTargetMarker { Enter, Attack, Hostile, Friendly }
public readonly record struct CommandMapObject(int Id, bool IsUnit);

/// <summary>One revision-bound candidate set for list selection and world-map selection.</summary>
public sealed class CommandTargetSelection
{
    public StrategySnapshot View { get; }
    public CommandMapTargetKind Kind { get; }
    public CommandTargetMarker Marker { get; }
    public ImmutableHashSet<int> Candidates { get; }
    public ImmutableHashSet<CommandMapObject> Objects { get; }
    public ImmutableHashSet<MapCell> ClearCells { get; }
    public CommandTargetSelection(StrategySnapshot view, CommandMapTargetKind kind, IEnumerable<int> candidates, CommandTargetMarker marker)
    {
        View = view; Kind = kind; Marker = marker; Candidates = candidates.ToImmutableHashSet();
        Objects = view.World.Settlements.Where(s => !s.IsDestroyed && !s.IsDormant &&
            kind == CommandMapTargetKind.Settlement && Candidates.Contains(s.Id))
            .Select(s => new CommandMapObject(s.Id,false)).Concat(view.World.Units.Where(u => !u.IsEmbedded && !u.IsPersonnel && u.Soldiers > 0 &&
                kind == CommandMapTargetKind.Unit && Candidates.Contains(u.Id))
                .Select(u => new CommandMapObject(u.Id,true))).ToImmutableHashSet();
        var cells = ImmutableHashSet.CreateBuilder<MapCell>();
        if (view.MilitaryRules is {} rules)
        {
            foreach (var place in view.World.Settlements.Where(s => Objects.Contains(new(s.Id,false)))) cells.UnionWith(rules.GeographicFootprint(place));
            foreach (var unit in view.World.Units.Where(u => Objects.Contains(new(u.Id,true)))) cells.Add(MapCoordinates.Cell(unit.Position));
        }
        ClearCells = cells.ToImmutable();
    }
    public int? Resolve(CommandMapObject target)
    {
        if (!Objects.Contains(target)) return null;
        return target.Id;
    }
}
