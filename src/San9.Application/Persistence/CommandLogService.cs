using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using San9.Core.Simulation;

namespace San9.Application.Persistence;

public sealed record CommandLogHeader(int FormatVersion, string RulesVersion, ulong Seed)
{
    [System.Text.Json.Serialization.JsonRequired] public int ComputerDamageBonusPercent { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool BattleDeathsEnabled { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public ControllerKind PlayerForceController { get; init; } = ControllerKind.Player;
    [System.Text.Json.Serialization.JsonRequired] public San9.Content.Scenarios.OriginalAiDefinition OriginalAi { get; init; } = San9.Content.Scenarios.OriginalAiDefinition.Default;
}
public sealed record ReplayAttempt(int Line, SessionRequest? Request, SessionDisposition Disposition, SessionRejection Rejection);
public sealed record ReplayResult(int Accepted, int AlreadyProcessed, ReplayAttempt? FirstFailure,
    ImmutableArray<DomainEvent> Events, ImmutableArray<ReplayAttempt> Attempts);

public sealed class CommandLogException(int line, string message, Exception? inner = null) : Exception(message, inner)
{
    public int Line { get; } = line;
}

public static class CommandLogService
{
    public static void Write(string path, GameSession session)
    {
        var recording = session.CaptureRecording();
        Write(path, new(SessionProtocol.LogFormatVersion, SessionProtocol.RulesVersion, recording.Initial.World.Simulation.InitialSeed)
            { BattleDeathsEnabled = recording.Initial.World.Simulation.BattleDeathsEnabled,
                ComputerDamageBonusPercent = recording.Initial.World.Simulation.ComputerDamageBonusPercent,
                PlayerForceController = recording.Initial.PlayerForceController, OriginalAi = recording.Initial.OriginalAi.Policy },
            recording.Operations.Select(e => e.Request));
    }

    public static void Write(string path, CommandLogHeader header, IEnumerable<SessionRequest> requests)
    {
        var text = new StringBuilder(CanonicalJson.Text(header)).Append('\n');
        foreach (var request in requests) text.Append(CanonicalJson.Text(request)).Append('\n');
        AtomicFile.Write(path, Encoding.UTF8.GetBytes(text.ToString()));
    }

    public static CommandLogHeader ReadHeader(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true));
        return ReadHeader(reader);
    }

    private static CommandLogHeader ReadHeader(StreamReader reader)
    {
        var header = Parse<CommandLogHeader>(reader.ReadLine(), 1);
        if (!Enum.IsDefined(header.PlayerForceController) || header.OriginalAi is null)
            throw new CommandLogException(1, "Invalid player force controller.");
        return header;
    }

    public static ReplayResult Replay(string path, GameSession session)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true));
        var header = ReadHeader(reader);
        if (header.FormatVersion != SessionProtocol.LogFormatVersion ||
            header.ComputerDamageBonusPercent != session.Snapshot.Simulation.ComputerDamageBonusPercent ||
            header.Seed != session.Snapshot.Simulation.InitialSeed || header.BattleDeathsEnabled != session.Snapshot.Simulation.BattleDeathsEnabled ||
            header.PlayerForceController != session.PlayerForceController ||
            CanonicalJson.Text(header.OriginalAi) != CanonicalJson.Text(session.OriginalAi.Policy))
            throw new CommandLogException(1, "Log version, seed or session configuration mismatch.");
        if (string.IsNullOrWhiteSpace(header.RulesVersion)) throw new CommandLogException(1, "Missing log rules version.");
        session.ReportRulesVersion(header.RulesVersion);
        int line = 1, accepted = 0, alreadyProcessed = 0;
        var events = ImmutableArray.CreateBuilder<DomainEvent>();
        var attempts = ImmutableArray.CreateBuilder<ReplayAttempt>();
        while (reader.ReadLine() is string text)
        {
            line++;
            SessionRequest request;
            try { request = Parse<SessionRequest>(text, line); }
            catch (CommandLogException)
            {
                var invalid = new ReplayAttempt(line, null, SessionDisposition.Rejected, SessionRejection.InvalidRequest);
                attempts.Add(invalid);
                return new(accepted, alreadyProcessed, invalid, events.ToImmutable(), attempts.ToImmutable());
            }
            var result = session.Apply(request);
            var attempt = new ReplayAttempt(line, request, result.Disposition, result.Rejection);
            attempts.Add(attempt);
            if (result.Disposition == SessionDisposition.Rejected)
                return new(accepted, alreadyProcessed, attempt, events.ToImmutable(), attempts.ToImmutable());
            if (result.Disposition == SessionDisposition.AlreadyProcessed) alreadyProcessed++;
            else accepted++;
            events.AddRange(result.Events);
        }
        return new(accepted, alreadyProcessed, null, events.ToImmutable(), attempts.ToImmutable());
    }

    private static T Parse<T>(string? text, int line)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new CommandLogException(line, "Missing log record.");
        try { return StrictJson.Deserialize<T>(Encoding.UTF8.GetBytes(text)); }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        { throw new CommandLogException(line, "Invalid log record.", error); }
    }
}
