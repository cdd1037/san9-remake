using San9.Application;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Core.Domain;

internal static class NewGameContentVerification
{
    internal static void Verify(ScenarioContent content)
    {
        // Start each selectable force separately so relations, computer bonuses and reserved activation keep their normal scopes.
        var players = ScenarioForceSelection.Read(content.Scenario, content.Text, content.IllustrationRoles).Forces.Select(f => f.Id).ToArray();
        foreach (var mode in Enum.GetValues<GameMode>())
        foreach (int player in players)
        {
            var session = GameSession.Start(content.Scenario, 42, settings: new() { Mode = mode }, playerForceIds: [player]);
            var messages = NewGameIntroduction.Produce(session.View, session.Messages(content.Text), content.Text, content.Presentation.Progress!);
            if (messages.Count == 0 || messages.Any(m => string.IsNullOrWhiteSpace(m.Message.BbCode)))
                throw new InvalidDataException($"Missing new-game introduction: {content.Scenario.ScenarioId}/{player}/{mode}");
        }
    }
}
