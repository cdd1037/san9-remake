using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using San9.Core.Diagnostics;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Diagnostics;

public sealed record SessionDiagnosticSnapshot(WorldDiagnosticSnapshot World, ImmutableArray<SessionRecord> Records,
    ImmutableArray<ChronicleEntry> Chronicle, ImmutableArray<ChronicleFact> History, EndingSelection? Ending, ImmutableDictionary<int, Presentation.StrategicContextState> StrategicContexts,
    long NextOperationSequence, ControllerKind PlayerForceController, AI.OriginalAiState OriginalAi);

public static class SessionDiagnostics
{
    /// <summary>Compare published session state, including pending decisions that cannot be saved.</summary>
    public static StateDifference? Compare(GameSession expected, GameSession actual) =>
        Compare(expected.CaptureCurrentState(), actual.CaptureCurrentState());

    public static SessionDiagnosticSnapshot Capture(SessionSnapshot session) => new(WorldDiagnostics.Capture(session.World),
        session.Records, session.Chronicle, session.History, session.Ending, session.StrategicContexts,
        session.NextOperationSequence, session.PlayerForceController, session.OriginalAi);

    public static JsonElement Serialize(SessionSnapshot session) =>
        JsonSerializer.SerializeToElement(Capture(session), SessionDiagnosticJsonContext.Default.SessionDiagnosticSnapshot);

    public static string ToDiagnosticJson(this SessionSnapshot session) => DiagnosticJson.Text(Serialize(session));

    public static bool AreEqual(SessionSnapshot expected, SessionSnapshot actual) => CompareParts(expected, actual, false, out _);

    public static StateDifference? Compare(SessionSnapshot expected, SessionSnapshot actual)
    {
        CompareParts(expected, actual, true, out var difference);
        return difference;
    }

    private static bool CompareParts(SessionSnapshot expected, SessionSnapshot actual, bool describeDifference, out StateDifference? difference)
    {
        difference = null;
        if (ReferenceEquals(expected, actual)) return true;
        var comparison = new DiagnosticComparison(SessionDiagnosticJsonContext.Default, describeDifference);
        // Keep diagnostic paths in ordinal order; World owns its own normalization and comparison.
        bool equal = comparison.Check(expected.Chronicle, actual.Chronicle, "$.chronicle") &&
            comparison.Check(expected.Ending, actual.Ending, "$.ending") &&
            comparison.Check(expected.History, actual.History, "$.history") &&
            comparison.Check(expected.NextOperationSequence, actual.NextOperationSequence, "$.nextOperationSequence") &&
            comparison.Check(expected.OriginalAi, actual.OriginalAi, "$.originalAi") &&
            comparison.Check(expected.PlayerForceController, actual.PlayerForceController, "$.playerForceController") &&
            comparison.Check(expected.Records, actual.Records, "$.records") &&
            comparison.Check(expected.StrategicContexts, actual.StrategicContexts, "$.strategicContexts");
        difference = comparison.Difference;
        if (!equal) return false;
        if (!describeDifference) return WorldDiagnostics.AreEqual(expected.World, actual.World);
        if (WorldDiagnostics.Compare(expected.World, actual.World) is not { } worldDifference) return true;
        difference = worldDifference with { Path = "$.world" + worldDifference.Path[1..] };
        return false;
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionDiagnosticSnapshot))]
internal partial class SessionDiagnosticJsonContext : JsonSerializerContext;
