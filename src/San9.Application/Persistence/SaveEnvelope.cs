namespace San9.Application.Persistence;

public sealed record SaveEnvelope(
    int FormatVersion,
    string RulesVersion,
    int HashVersion,
    string ContentFingerprint,
    SessionSnapshot Session);
