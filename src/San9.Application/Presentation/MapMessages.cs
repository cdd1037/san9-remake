using San9.Core.Domain;
using San9.Content.Interface;

namespace San9.Application.Presentation;

/// <summary>A retained, already-visible fact. Location is where it happened, not the object's later location.</summary>
public sealed record MapMessageRecord(Guid Id, ProducedMessage Message, MapCell? Location = null, int? ForceColorCode = null);

/// <summary>Immutable speech inputs captured by its producer before participants or targets disappear.</summary>
public sealed record MapSpeechMessage(Guid Id, ProducedMessage Message, int SpeakerId, string SpeakerName,
    MapCell Location, int? UnitId = null, int? ForceColorCode = null,
    MapSpeechPlacement Placement = MapSpeechPlacement.RightBelow, double DelaySeconds = 0);
