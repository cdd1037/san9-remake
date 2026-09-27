using San9.Content.Scenarios;

namespace San9.Client.Godot.Diagnostics;

internal static class ClientVerificationScenario
{
    // In-memory fixture only; the imported package is never modified.
    public static ScenarioInitialState Create(ScenarioInitialState initial) => initial with
    {
        Settlements = initial.Settlements.Select(s => s.Id == 15
            ? s with { Commerce = 300, Cultivation = 150 } : s).ToArray()
    };
}
