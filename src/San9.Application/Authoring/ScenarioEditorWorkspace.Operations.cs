using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Authoring;

public sealed partial class ScenarioEditorWorkspace
{
    public IEnumerable<SettlementInitialState> DeploymentCities => Document.Settlements.Where(s =>
        !s.IsDormant && s.CanHostPersons && s.Kind == SettlementKind.City);

    public IEnumerable<ScenarioOfficerState> WithdrawableOfficers => Document.Officers.IntersectBy(
        _shared.Officers.Officers.Select(p => p.Id), o => o.Id);

    public IEnumerable<OfficerDefaults> Founders
    {
        get
        {
            var eligible = Document.Officers.Where(o => o.Status == OfficerStatus.Free && !o.IsDormant &&
                o.OwnerId is null && o.UnitId is null).ToDictionary(o => o.Id);
            return UnplacedOfficers.Concat(_shared.Officers.Officers.Concat(Document.AdditionalOfficerDefaults)
                .Where(p => eligible.ContainsKey(p.Id)).Select(p => eligible[p.Id].Overrides.Apply(p)));
        }
    }

    public void FoundForce(int founderId, int cityId)
    {
        var founder = Founders.SingleOrDefault(p => p.Id == founderId)
            ?? throw new InvalidDataException("请选择尚未安置或当前在野的武将。");
        var city = DeploymentCities.SingleOrDefault(s => s.Id == cityId && s.OwnerId is null)
            ?? throw new InvalidDataException("建立势力须选择空白都市。");
        int forceId = checked(Document.Forces.Select(f => f.Id).DefaultIfEmpty(-1).Max() + 1);
        int corpsId = checked(Document.Information.Corps.Select(c => c.Id).DefaultIfEmpty(-1).Max() + 1);
        int color = Enumerable.Range(0, 50).MinBy(c => Document.Forces.Count(f => f.ColorCode == c));
        var force = new ForceInitialState(forceId, founder.NameKey, color, 100)
        {
            LeaderId = founderId, BaseTrust = 100, FlagGlyph = founder.FlagGlyph,
            RankId = Definitions.Information.Ranks.SingleOrDefault(r => r.IsUnranked)?.Id
        };
        var officer = Document.Officers.SingleOrDefault(o => o.Id == founderId) ?? ScenarioOfficerState.Unplaced(founderId);
        var ruler = officer with { OwnerId = forceId, SettlementId = cityId, RegionId = null, Status = OfficerStatus.Ruler,
            StatusStartYear = Document.Year, IsCommander = true, IsDormant = false, OfficeId = null,
            ForceLoyalties = officer.ForceLoyalties.SetItem(forceId, 100) };
        ApplyOperation(Document with
        {
            Forces = [.. Document.Forces, force],
            Officers = [.. Document.Officers.Where(o => o.Id != founderId), ruler],
            Settlements = Document.Settlements.Select(s => s.Id == cityId ? s with { OwnerId = forceId, CorpsId = corpsId } : s).ToArray(),
            Information = Document.Information with
            {
                Corps = [.. Document.Information.Corps, new(corpsId, forceId, founderId, new(0, 0)) { IsRoot = true }],
                Relations = [.. Document.Information.Relations, .. Document.Forces.Select(f => new ForceRelation(f.Id, forceId, 50))]
            }
        });
    }

    public void DeleteForce(int id)
    {
        var doc = Document;
        if (!doc.Forces.Any(f => f.Id == id)) throw new InvalidDataException("势力不存在。");
        if (doc.Units.Any(u => u.OwnerId == id))
            throw new InvalidDataException("该势力仍有初始部队，请先在「完整初始状态」中调整部队及其成员。");
        if (Definitions.SearchEvents.OfType<ForceActivationSearchEventDefinition>().Any(e => e.ForceId == id))
            throw new InvalidDataException("势力登场事件仍引用该势力，请先调整剧本的搜索事件配置。");
        var places = doc.Settlements.Where(s => s.OwnerId == id).Select(s => s.Id).ToHashSet();
        ScenarioOfficerState Release(ScenarioOfficerState officer)
        {
            var o = officer with { ForceLoyalties = officer.ForceLoyalties.Remove(id) };
            if (o.OwnerId == id)
                return o with { OwnerId = null, OfficeId = null, IsCommander = false,
                    Status = o.Status <= OfficerStatus.Serving ? OfficerStatus.Free : o.Status,
                    StatusStartYear = o.Status <= OfficerStatus.Serving ? doc.Year : o.StatusStartYear };
            if (o.Status != OfficerStatus.Captive || o.SettlementId is not int prison || !places.Contains(prison)) return o;
            var home = o.OwnerId is null ? null : doc.Settlements.FirstOrDefault(s => s.OwnerId == o.OwnerId && s.CanHostPersons && !s.IsDormant);
            return o with { OwnerId = home?.OwnerId, SettlementId = home?.Id ?? prison,
                Status = home is null ? OfficerStatus.Free : OfficerStatus.Serving, StatusStartYear = doc.Year };
        }
        var alliance = doc.Alliance;
        if (alliance?.TargetForceId == id || alliance?.LeaderForceId == id) alliance = null;
        else if (alliance is not null && alliance.MemberForceIds.Contains(id))
            alliance = alliance.MemberForceIds.Length == 2 ? null : alliance with { MemberForceIds = alliance.MemberForceIds.Remove(id) };
        ApplyOperation(doc with
        {
            Forces = doc.Forces.Where(f => f.Id != id).ToArray(),
            PlayerForceId = doc.PlayerForceId == id ? null : doc.PlayerForceId,
            Settlements = doc.Settlements.Select(s => s.OwnerId == id ? s with { OwnerId = null, CorpsId = null } : s).ToArray(),
            Officers = doc.Officers.Select(Release).ToArray(), Alliance = alliance,
            AttackRequests = [.. doc.AttackRequests.Where(r => r.RequestedForceId != id && r.RequesterForceId != id && !places.Contains(r.TargetSettlementId))],
            Information = doc.Information with
            {
                Relations = doc.Information.Relations.Where(r => r.FirstId != id && r.SecondId != id).ToArray(),
                Corps = doc.Information.Corps.Where(c => c.OwnerId != id).Select(c => c.Delegation is DefeatForceDelegation d && d.ForceId == id
                    ? c with { Delegation = new GeneralDelegation() } : c).ToArray()
            }
        });
    }

    public void SetForceOffice(int forceId, int? personId, bool ruler)
    {
        var force = Document.Forces.Single(f => f.Id == forceId);
        int? previous = ruler ? force.LeaderId : force.AdviserId;
        if (previous == personId) return;
        if (personId is int id && (!Document.Officers.Any(o => o.Id == id && o.OwnerId == forceId &&
            o.Status <= OfficerStatus.Serving && o.IsDormant == force.IsDormant) ||
            Document.Information.Corps.Any(c => !c.IsRoot && c.LeaderId == id) ||
            (ruler ? force.AdviserId : force.LeaderId) == id))
            throw new InvalidDataException("请选择本势力可任职的武将，先解除冲突职务。");
        var revised = ruler ? force with { LeaderId = personId } : force with { AdviserId = personId };
        var corps = Document.Information.Corps.Select(c => ruler && c.OwnerId == forceId && c.IsRoot
            ? c with { LeaderId = personId } : c).ToArray();
        ApplyOperation(Document with
        {
            Forces = Document.Forces.Select(f => f.Id == forceId ? revised : f).ToArray(),
            Information = Document.Information with { Corps = corps },
            Officers = Document.Officers.Select(o => o.Id == previous ? o with { Status = OfficerStatus.Serving,
                IsCommander = corps.Any(c => c.LeaderId == o.Id) } : o.Id == personId ? o with
                { Status = ruler ? OfficerStatus.Ruler : OfficerStatus.Strategist, IsCommander = ruler || o.IsCommander } : o).ToArray()
        });
    }

    public void TransferSettlement(int placeId, int? owner, int? retreatId)
    {
        var place = Document.Settlements.Single(s => s.Id == placeId);
        if (place.IsDormant || owner == place.OwnerId) throw new InvalidDataException("请选择新的归属势力或无主。");
        if (owner is int force && !Document.Forces.Any(f => f.Id == force && !f.IsDormant))
            throw new InvalidDataException("接收势力必须已启用。");
        var corps = owner is null ? null : Document.Information.Corps.SingleOrDefault(c => c.OwnerId == owner && c.IsRoot)
            ?? throw new InvalidDataException("接收势力缺少直属军团。");
        if (retreatId is int retreat && !Document.Settlements.Any(s => s.Id == retreat && s.Id != placeId &&
            s.OwnerId == place.OwnerId && s.CanHostPersons && !s.IsDormant))
            throw new InvalidDataException("撤往地点须属于原势力，并可安置武将。");
        ScenarioOfficerState Move(ScenarioOfficerState o)
        {
            if (o.SettlementId != placeId || o.Status > OfficerStatus.Serving) return o;
            if (retreatId is not null) return o with { SettlementId = retreatId };
            if (Document.Forces.Any(f => f.LeaderId == o.Id || f.AdviserId == o.Id) ||
                Document.Information.Corps.Any(c => c.LeaderId == o.Id))
                throw new InvalidDataException($"{OfficerName(o.Id)}仍担任君主、军师或军团长，请选择撤往原势力据点或先调整任职。");
            return o with { OwnerId = owner, Status = owner is null ? OfficerStatus.Free : OfficerStatus.Serving,
                OfficeId = null, IsCommander = false, StatusStartYear = Document.Year };
        }
        ApplyOperation(Document with
        {
            Settlements = Document.Settlements.Select(s => s.Id == placeId ? s with { OwnerId = owner, CorpsId = corps?.Id } : s).ToArray(),
            Officers = Document.Officers.Select(Move).ToArray()
        });
    }

    public void DeployOfficers(IReadOnlyCollection<int> ids, int cityId, bool serve)
    {
        var city = DeploymentCities.SingleOrDefault(s => s.Id == cityId) ?? throw new InvalidDataException("请选择可安置的都市。");
        if (serve && city.OwnerId is null) throw new InvalidDataException("空白都市仅能在野。");
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count || ids.Except(UnplacedOfficers.Select(o => o.Id)).Any())
            throw new InvalidDataException("请选择尚未安置的共享武将。");
        var people = ids.Select(id => ScenarioOfficerState.Unplaced(id) with { SettlementId = cityId, IsDormant = false,
            Status = serve ? OfficerStatus.Serving : OfficerStatus.Free, OwnerId = serve ? city.OwnerId : null,
            StatusStartYear = serve ? Document.Year : null });
        ApplyOperation(Document with { Officers = [.. Document.Officers, .. people] });
    }

    public void WithdrawOfficers(IReadOnlyCollection<int> ids)
    {
        var selected = ids.ToHashSet();
        if (selected.Count == 0 || selected.Except(Document.Officers.Select(o => o.Id)).Any() ||
            selected.Except(_shared.Officers.Officers.Select(p => p.Id)).Any())
            throw new InvalidDataException("请选择已安置的共享武将。");
        foreach (int id in selected)
        {
            var force = Document.Forces.FirstOrDefault(f => f.LeaderId == id || f.AdviserId == id);
            if (force is not null) throw new InvalidDataException($"{OfficerName(id)}是{Name(force)}的君主或军师。");
            var corps = Document.Information.Corps.FirstOrDefault(c => c.LeaderId == id);
            if (corps is not null) throw new InvalidDataException($"{OfficerName(id)}是军团 {corps.Id} 的军团长。");
            var treasure = Document.Information.TreasureStates.FirstOrDefault(t => t.HolderId == id);
            if (treasure is not null) throw new InvalidDataException($"{OfficerName(id)}持有{Definitions.Information.Treasures.Single(t => t.Id == treasure.Id).Name}，请先调整宝物安置。");
            var officer = Document.Officers.Single(o => o.Id == id);
            if (officer.UnitId is not null || Document.Units.Any(u => u.CommanderId == id || u.MemberPositions.ContainsKey(id)))
                throw new InvalidDataException($"{OfficerName(id)}属于初始部队，请先调整部队成员。");
        }
        ApplyOperation(Document with { Officers = Document.Officers.Where(o => !selected.Contains(o.Id)).ToArray() });
    }

    public void SetRelation(int a, int b, int value)
    {
        if (a == b || value is < 0 or > 100 || !Document.Forces.Any(f => f.Id == a) || !Document.Forces.Any(f => f.Id == b))
            throw new InvalidDataException("请选择两个不同势力，关系值为 0 至 100。");
        int first = Math.Min(a, b), second = Math.Max(a, b);
        var others = Document.Information.Relations.Where(r => r.FirstId != first || r.SecondId != second);
        Update(Document with { Information = Document.Information with { Relations = [.. others, new(first, second, value)] } });
    }

    public void SetAlliance(AllianceState? alliance)
    {
        alliance?.Validate(Document.Forces.Where(f => !f.IsDormant && !f.IsBarbarian).Select(f => f.Id).ToHashSet());
        ApplyOperation(Document with { Alliance = alliance });
    }

    public void SetAttackRequest(int requested, AttackRequestInitialState? request)
    {
        var others = Document.AttackRequests.Where(r => r.RequestedForceId != requested);
        if (request is not null && (requested != request.RequestedForceId || request.RequesterForceId == requested ||
            request.RemainingXun <= 0 || !Document.Forces.Any(f => f.Id == request.RequesterForceId) ||
            !Document.Forces.Any(f => f.Id == requested) || !Document.Settlements.Any(s => s.Id == request.TargetSettlementId &&
            s.OwnerId is int owner && owner != requested && owner != request.RequesterForceId)))
            throw new InvalidDataException("目标须属于双方以外的势力，委托期限至少一旬。");
        ApplyOperation(Document with { AttackRequests = (request is null ? others : others.Append(request)).ToImmutableArray() });
    }

    private void ApplyOperation(ScenarioDocument candidate)
    {
        Validate(candidate);
        Update(candidate);
    }
}
