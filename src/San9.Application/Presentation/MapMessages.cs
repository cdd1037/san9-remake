using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Content.Interface;

namespace San9.Application.Presentation;

public enum MapReportImportance { Summary, Detailed }

/// <summary>A retained fact, independent of immediate report filtering. Location is where it happened.</summary>
public sealed record MapMessageRecord(string Id, ProducedMessage Message, MapCell? Location = null, int? ForceColorCode = null,
    MapReportImportance Importance = MapReportImportance.Summary)
{
    public string? SoundRole { get; init; }
}

internal static class MapRecordColors
{
    public static int? Resolve(StrategySnapshot before, StrategySnapshot after, int? forceId) =>
        forceId is int id && before.World.Simulation.Controls.Any(c => c.ForceId == id && c.Kind == ControllerKind.Player)
            ? before.Forces.Concat(after.Forces).First(f => f.Id == id).ColorCode : null;
}

/// <summary>Immutable speech inputs captured by its producer before participants or targets disappear.</summary>
public sealed record MapSpeechMessage(string Id, ProducedMessage Message, int SpeakerId, string SpeakerName,
    MapCell Location, int? UnitId = null, int? ForceColorCode = null,
    MapSpeechPlacement Placement = MapSpeechPlacement.RightBelow, double DelaySeconds = 0);
