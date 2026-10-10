using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class AppointmentPresentation
{
    public static string Person(AppointmentDraft draft, TextCatalog texts, int id) => texts.Resolve(draft.View.Officers.Single(p => p.Id == id).NameKey);
    public static string Office(AppointmentDraft draft, TextCatalog texts, int? id) => id is int key
        ? texts.Resolve(draft.View.Information.Offices.Single(o => o.Id == key).NameKey) : CommandText.Resolve(texts, "label.none");
    public static string Treasure(AppointmentDraft draft, TextCatalog texts, int id) => texts.Resolve(draft.View.Information.Treasures.Single(t => t.Id == id).NameKey);

    public static string Result(AppointmentDraft draft, TextCatalog texts, AppointmentSubmission result)
    {
        List<string> lines = [];
        foreach (var e in result.Events)
        {
            string? line = e switch {
                AdviserAppointedEvent a => CommandText.Resolve(texts, "result.appoint-adviser", Person(draft, texts, a.OfficerId)),
                OfficeAssignedEvent a => $"{Person(draft, texts, a.OfficerId)}：{Office(draft, texts, a.PreviousId)} → {Office(draft, texts, a.OfficeId)}",
                OfficerRewardedEvent r => CommandText.Resolve(texts, "result.reward", Person(draft, texts, r.OfficerId), r.Gain),
                TreasureTransferredEvent t => $"{Treasure(draft, texts, t.TreasureId)}：{Person(draft, texts, t.PreviousHolderId)} → {Person(draft, texts, t.HolderId)}",
                PrisonerExecutedEvent p => CommandText.Resolve(texts, "result.execute", Person(draft, texts, p.OfficerId)),
                OfficerExiledEvent p => CommandText.Resolve(texts, "result.exile", Person(draft, texts, p.OfficerId)),
                PrisonerReleasedEvent p => CommandText.Resolve(texts, "result.release", Person(draft, texts, p.OfficerId)),
                OfficerLoyaltyChangedEvent p when draft.Kind != "reward" => CommandText.Resolve(texts, "result.loyalty", Person(draft, texts, p.OfficerId), p.Previous, p.Current),
                _ => null
            };
            if (line is not null) lines.Add(line);
        }
        if (result.Rejection != CommandRejection.None)
            lines.Add(CommandText.Resolve(texts, "result.partial", result.CompletedCommands) + (result.Rejection switch {
                CommandRejection.NoDestination => CommandText.Resolve(texts, "failure.accommodation"),
                CommandRejection.InsufficientFunds => CommandText.Resolve(texts, "failure.money"),
                _ => CommandText.Resolve(texts, "failure.conditions") }));
        if (lines.Count == 0) lines.Add(result.CompletedCommands > 0 ? CommandText.Resolve(texts, "result.submitted") : CommandText.Resolve(texts, "result.empty"));
        return string.Join("\n", lines);
    }
}
