using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Authoring;

public sealed record ScenarioEditorIssue(string Page, int ObjectId, string Message);

public sealed partial class ScenarioEditorWorkspace
{
    public void CheckDraft() => PreparePreview(Document, Preview);
    public ScenarioEditorIssue? FindIssue() => FindIssue(Document);

    private ScenarioEditorIssue? FindIssue(ScenarioDocument document)
    {
        foreach (var place in document.Settlements)
        {
            if (place.CorpsId is int corps && !document.Information.Corps.Any(c => c.Id == corps && c.OwnerId == place.OwnerId))
                return new("据点", place.Id, $"{Name(place)} [{place.Id}]：所属军团 {corps} 与据点归属不符。");
            if (place.Soldiers == 0 && place.Morale != 0)
                return new("据点", place.Id, $"{Name(place)} [{place.Id}]：兵力为零时士气须为零。");
        }
        foreach (var officer in document.Officers)
        {
            if (officer.RegionId is not null && officer.SettlementId is not null)
                return new("武将安置", officer.Id, $"{OfficerName(officer.Id)} [{officer.Id}]：所在据点与所在地域只能填写一项。");
            if (officer.Status <= OfficerStatus.Serving && officer.SettlementId is int home &&
                !document.Settlements.Any(s => s.Id == home && s.OwnerId == officer.OwnerId))
                return new("武将安置", officer.Id, $"{OfficerName(officer.Id)} [{officer.Id}]：所在据点与武将所属势力不一致。");
        }
        foreach (var force in document.Forces)
        {
            if (!force.IsDormant && !force.IsBarbarian && force.LeaderId is null)
                return new("势力", force.Id, $"势力 [{force.Id}]：请选择君主。");
            if (force.LeaderId is int leader && !document.Officers.Any(o => o.Id == leader && o.OwnerId == force.Id))
                return new("势力", force.Id, $"{Name(force)} [{force.Id}]：君主 {leader} 未归属本势力。");
            if (force.AdviserId is int adviser && !document.Officers.Any(o => o.Id == adviser && o.OwnerId == force.Id && o.Status == OfficerStatus.Strategist))
                return new("势力", force.Id, $"{Name(force)} [{force.Id}]：军师 {adviser} 的身份或归属不符。");
        }
        foreach (var request in document.AttackRequests)
        {
            if (!document.Settlements.Any(s => s.Id == request.TargetSettlementId && s.OwnerId is int owner &&
                owner != request.RequestedForceId && owner != request.RequesterForceId))
                return new("外交", request.RequestedForceId, $"受托势力 [{request.RequestedForceId}]：攻打委托的目标据点 [{request.TargetSettlementId}] 须属于第三方势力。");
        }
        foreach (var item in document.Information.TreasureStates)
            if (item.HolderId is int holder && !document.Officers.Any(p => p.Id == holder && p.Status != OfficerStatus.Dead))
                return new("宝物", item.Id, $"宝物 [{item.Id}]：持有人 {holder} 须为剧本已安置的存活武将。");
        return null;
    }
}
