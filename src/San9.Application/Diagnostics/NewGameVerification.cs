using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Core.Domain;

namespace San9.Application.Diagnostics;

/// <summary>Explicit runtime integration checks, separate from converted-package integrity.</summary>
public static class NewGameVerification
{
    public static async Task VerifyAsync(string root, string? scenarioId = null, Action<string>? progress = null, CancellationToken token = default)
    {
        var catalog = await ContentCatalogFiles.LoadAsync(root, token);
        foreach (var entry in scenarioId is null ? catalog.Scenarios : [catalog.RequireScenario(scenarioId)])
        {
            token.ThrowIfCancellationRequested();
            var scenario = await ScenarioContentFiles.LoadAsync(PackageFiles.Resolve(root, entry.Package), token);
            var session = GameSession.Start(scenario.Scenario, 42);
            if (!session.Snapshot.Forces.Select(f => f.Id).Order().SequenceEqual(scenario.Scenario.Forces.Select(f => f.Id).Order()) ||
                !session.Snapshot.Settlements.Select(s => s.Id).Order().SequenceEqual(scenario.Scenario.Settlements.Select(s => s.Id).Order()) ||
                !session.Snapshot.Officers.Select(o => o.Id).Order().SequenceEqual(scenario.Scenario.Officers.Select(o => o.State.Id).Order()))
                throw new InvalidDataException($"Initial world lost scenario identities: {entry.Id}");
            VerifyNewGame(scenario, token);
            var query = new InformationQuery(session, scenario.Text);
            foreach (var category in new[] { InformationCategory.Forces, InformationCategory.Cities,
                InformationCategory.Regions, InformationCategory.Officers, InformationCategory.Treasures })
                _ = query.Read(category);
            progress?.Invoke($"Verified startup {entry.Id}: world identities, queries and all force/mode introductions ready.");
        }
    }

    private static void VerifyNewGame(ScenarioContent content, CancellationToken token)
    {
        // Start each selectable force separately so relations, computer bonuses and reserved activation keep their normal scopes.
        var players = ScenarioForceSelection.Read(content.Scenario, content.Text, content.IllustrationRoles).Forces.Select(f => f.Id).ToArray();
        foreach (var mode in Enum.GetValues<GameMode>())
        foreach (int player in players)
        {
            token.ThrowIfCancellationRequested();
            var session = GameSession.Start(content.Scenario, 42, settings: new() { Mode = mode }, playerForceIds: [player]);
            var messages = NewGameIntroduction.Produce(session.View, session.Messages(content.Text), content.Text, content.Presentation.Progress!);
            if (messages.Count == 0 || messages.Any(m => string.IsNullOrWhiteSpace(m.Message.BbCode)))
                throw new InvalidDataException($"Missing new-game introduction: {content.Scenario.ScenarioId}/{player}/{mode}");
        }
    }
}
