using San9.Core.Simulation;

namespace San9.Application.Persistence;

public sealed record SaveEnvelope(
    int FormatVersion,
    string ContentFingerprint,
    WorldSnapshot World);
