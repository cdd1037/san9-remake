using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class AppointmentPresentation
{
    public static string Person(AppointmentDraft draft, TextCatalog texts, int id) => texts.Resolve(draft.View.Officers.Single(p => p.Id == id).NameKey);
    public static string Office(AppointmentDraft draft, TextCatalog texts, int? id) => id is int key
        ? texts.Resolve(draft.View.Information.Offices.Single(o => o.Id == key).NameKey) : "无";
    public static string Treasure(AppointmentDraft draft, TextCatalog texts, int id) => texts.Resolve(draft.View.Information.Treasures.Single(t => t.Id == id).NameKey);

    public static string Result(AppointmentDraft draft, TextCatalog texts, AppointmentSubmission result)
    {
        List<string> lines = [];
        foreach (var e in result.Events)
        {
            string? line = e switch {
                AdviserAppointedEvent a => $"已任命{Person(draft, texts, a.OfficerId)}为军师。",
                OfficeAssignedEvent a => $"{Person(draft, texts, a.OfficerId)}：{Office(draft, texts, a.PreviousId)} → {Office(draft, texts, a.OfficeId)}",
                OfficerRewardedEvent r => $"已褒奖{Person(draft, texts, r.OfficerId)}，忠诚上升{r.Gain}。",
                TreasureTransferredEvent t => $"{Treasure(draft, texts, t.TreasureId)}：{Person(draft, texts, t.PreviousHolderId)} → {Person(draft, texts, t.HolderId)}",
                PrisonerExecutedEvent p => $"已处斩{Person(draft, texts, p.OfficerId)}。",
                OfficerExiledEvent p => $"{Person(draft, texts, p.OfficerId)}已离开本势力。",
                PrisonerReleasedEvent p => $"已释放{Person(draft, texts, p.OfficerId)}。",
                OfficerLoyaltyChangedEvent p when draft.Kind != "reward" => $"{Person(draft, texts, p.OfficerId)}忠诚：{p.Previous} → {p.Current}",
                _ => null
            };
            if (line is not null) lines.Add(line);
        }
        if (result.Rejection != CommandRejection.None)
            lines.Add($"本次已完成{result.CompletedCommands}项，剩余指令未执行。请重新选择对象。" + (result.Rejection switch {
                CommandRejection.NoDestination => "没有可安置的目的地。",
                CommandRejection.InsufficientFunds => "资金不足。",
                _ => "对象或执行条件已不满足。" }));
        if (lines.Count == 0) lines.Add(result.CompletedCommands > 0 ? "指令已提交。" : "没有执行指令。");
        return string.Join("\n", lines);
    }
}
