namespace San9.Application.Persistence;

public sealed record SaveEnvelope(
    int FormatVersion,
    string RulesVersion,
    SessionSnapshot Session);
