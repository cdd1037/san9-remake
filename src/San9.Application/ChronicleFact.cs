using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Application;

/// <summary>Persistent semantic history. Message selection belongs to authored presentation content.</summary>
public sealed record ChronicleFact(GameClock Clock, int ForceId, string Role, int? ForceColorCode,
    ImmutableDictionary<string, MessageBinding> Values, ImmutableDictionary<string, string> TextKeys);
