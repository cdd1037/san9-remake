using San9.Core.Domain;
using San9.Content.Interface;

namespace San9.Application.Presentation;

public enum MapReportImportance { Summary, Detailed }

/// <summary>A retained fact, independent of immediate report filtering. Location is where it happened.</summary>
public sealed record MapMessageRecord(Guid Id, ProducedMessage Message, MapCell? Location = null, int? ForceColorCode = null,
    MapReportImportance Importance = MapReportImportance.Summary);

/// <summary>Immutable speech inputs captured by its producer before participants or targets disappear.</summary>
public sealed record MapSpeechMessage(Guid Id, ProducedMessage Message, int SpeakerId, string SpeakerName,
    MapCell Location, int? UnitId = null, int? ForceColorCode = null,
    MapSpeechPlacement Placement = MapSpeechPlacement.RightBelow, double DelaySeconds = 0);
