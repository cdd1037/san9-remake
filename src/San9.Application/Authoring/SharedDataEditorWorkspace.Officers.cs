using System.Runtime.CompilerServices;
using San9.Content.Packages;
using San9.Content.Scenarios;

namespace San9.Application.Authoring;

public sealed partial class SharedDataEditorWorkspace
{
    private readonly record struct OfficerReference(int Id, string Scope, string Field, int? OwnerId = null, string? OwnerName = null)
    {
        public string Location => OwnerId is int owner ? $"{Scope}：{OwnerName ?? "武将"}（{owner}）的{Field}" : $"{Scope}：{Field}";
    }

    public OfficerDefaults GetOfficer(int id) => Officers.Single(p => p.Id == id);
    public bool IsOfficerChanged(int id) => _changedOfficers.Contains(id);
    public bool IsCreatedOfficer(int id) => !_baseOfficerIds.Contains(id) && Officers.Any(p => p.Id == id);

    private void UpdateOfficerChange(int id)
    {
        if (ContentComparison.Equal(Officers.SingleOrDefault(p => p.Id == id), _savedOfficers.Officers.SingleOrDefault(p => p.Id == id)))
            _changedOfficers.Remove(id);
        else _changedOfficers.Add(id);
    }

    public void SetOfficer(OfficerDefaults value)
    {
        ContentStructure.Validate(value);
        var entries = Officers.ToArray();
        int index = Array.FindIndex(entries, p => p.Id == value.Id);
        if (index < 0) throw new InvalidDataException("找不到武将。");
        if (value.TacticProficiencies.Where((v, i) => v > Definitions.OfficerGrowth.TacticProficiencyCap && v != entries[index].TacticProficiencies[i]).Any())
            throw new InvalidDataException("修改后的兵法熟练度不能超过基础规则上限；已有超限值可保留。");
        var original = _officerBaselines[value.Id];
        if (value.NameKey != original.NameKey || value.CourtesyNameKey != original.CourtesyNameKey || value.BiographyKey != original.BiographyKey)
            throw new InvalidDataException("请保留人物文本引用身份。");
        entries[index] = value;
        OfficerCatalog = OfficerCatalog with { Officers = entries };
        UpdateOfficerChange(value.Id);
    }

    public void RestoreOfficer(int id) => SetOfficer(_officerBaselines[id]);

    public async Task<OfficerDefaults> CreateOfficerAsync(int? copyFrom = null, CancellationToken token = default)
    {
        var source = copyFrom is int original ? GetOfficer(original) : null;
        int maximum = Officers.Select(p => p.Id).DefaultIfEmpty(-1).Max();
        await foreach (var reference in ReadOfficerReferencesAsync(token: token)) maximum = Math.Max(maximum, reference.Id);
        long next = Math.Max(_baseOfficerIdentityFloor, Math.Max(OfficerCatalog.NextOfficerId, (long)maximum + 1));
        if (next > int.MaxValue) throw new InvalidDataException("无法分配新的武将身份。");
        token.ThrowIfCancellationRequested();
        int id = (int)next;
        var person = source is null ? OfficerDefaults.Create(id) : source with
        {
            Id = id, Name = source.Name + "副本", NameKey = $"authored.officer.{id}",
            CourtesyNameKey = source.CourtesyName is null ? null : $"authored.officer.{id}.courtesy",
            BiographyKey = source.Biography is null ? null : $"authored.officer.{id}.biography"
        };
        OfficerCatalog = OfficerCatalog with { Officers = [.. Officers, person], NextOfficerId = next + 1 };
        _officerBaselines.Add(id, person); UpdateOfficerChange(id);
        return person;
    }

    public async Task DeleteOfficerAsync(int id, CancellationToken token = default)
    {
        if (!IsCreatedOfficer(id)) throw new InvalidDataException("仅支持删除自建武将，原有武将不能删除。");
        var references = await FindOfficerReferencesAsync(id, token);
        if (references.Count > 0) throw new InvalidDataException("该武将仍被引用，请先解除以下引用：\n" + string.Join('\n', references));
        token.ThrowIfCancellationRequested();
        OfficerCatalog = OfficerCatalog with { Officers = Officers.Where(p => p.Id != id).ToArray() };
        _officerBaselines.Remove(id); UpdateOfficerChange(id);
    }

    public async Task<IReadOnlyList<string>> FindOfficerReferencesAsync(int id, CancellationToken token = default)
    {
        var locations = new SortedSet<string>(StringComparer.Ordinal);
        await foreach (var reference in ReadOfficerReferencesAsync(id, token))
            if (reference.Id == id) locations.Add(reference.Location);
        return locations.ToArray();
    }

    private async IAsyncEnumerable<OfficerReference> ReadOfficerReferencesAsync(int? excludedOfficer = null,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        foreach (var reference in PersonReferences(Officers.Where(p => p.Id != excludedOfficer), "共享武将")
            .Concat(RuleReferences(Definitions, "共享规则"))) yield return reference;
        var files = new ContentReadSession();
        foreach (var entry in OfficerScenarios())
        {
            var document = await files.ReadScenarioAsync(entry.Root, entry.Manifest, token);
            var definitions = await OfficerScenarioDefinitions(entry, document, files, token);
            foreach (var reference in ScenarioReferences(document, definitions, $"剧本 {entry.Id}")) yield return reference;
        }
    }

    private IEnumerable<ScenarioReference> OfficerScenarios() => _scenarios.Where(entry =>
        PackageFiles.Resolve(entry.Root, entry.Manifest.OfficerDefaults).Equals(OfficerPath, StringComparison.OrdinalIgnoreCase));

    private async Task<ScenarioDefinitions> OfficerScenarioDefinitions(ScenarioReference entry, ScenarioDocument document,
        ContentReadSession files, CancellationToken token)
    {
        string path = PackageFiles.Resolve(entry.Root, entry.Manifest.Definitions);
        var definitions = path.Equals(DefinitionPath, StringComparison.OrdinalIgnoreCase) ? Definitions
            : await files.ReadAsync<ScenarioDefinitions>(PlayerDefinitionFiles.ActivePath(path), token);
        return document.DefinitionOverrides.Apply(definitions);
    }

    private static IEnumerable<OfficerReference> PersonReferences(IEnumerable<OfficerDefaults> people, string scope)
    {
        foreach (var p in people)
        {
            if (p.ParentId is int parent) yield return new(parent, scope, "亲代", p.Id, p.Name);
            if (p.BloodlineId is int family) yield return new(family, scope, "血缘", p.Id, p.Name);
        }
    }

    private static IEnumerable<OfficerReference> RuleReferences(ScenarioDefinitions definitions, string scope)
    {
        foreach (int id in definitions.OriginalAi.PreferredStrategyRulerIds) yield return new(id, scope, "AI优先君主");
        foreach (int id in definitions.OriginalAi.FamilyRankBonuses.Keys) yield return new(id, scope, "AI家族排序加成");
        foreach (var title in definitions.Information.CountryTitles)
        {
            foreach (int id in title.Rule.PreferredRulerIds) yield return new(id, scope, "君主偏好", title.Rule.Id, "国号");
            foreach (int id in title.Rule.PreferredBloodlineIds) yield return new(id, scope, "血缘偏好", title.Rule.Id, "国号");
        }
        foreach (var treasure in definitions.Information.Treasures)
            foreach (int id in treasure.FavoredOwnerIds) yield return new(id, scope, "持有人偏好", treasure.Id, "宝物");
        if (definitions.SunMarriage is { } marriage) yield return new(marriage.DonorBloodlineId, scope, "婚姻事件来源血缘");
        if (definitions.Military is { } military)
            foreach (var pair in military.LinkRules.Pairs)
            {
                yield return new(pair.FirstId, scope, "兵法连携关系");
                yield return new(pair.SecondId, scope, "兵法连携关系");
            }
    }

    private static IEnumerable<OfficerReference> ScenarioReferences(ScenarioDocument document, ScenarioDefinitions definitions, string scope)
    {
        foreach (var p in document.Officers)
        {
            yield return new(p.Id, scope, "初始状态", p.Id);
            if (p.Overrides.ParentId?.Value is int parent) yield return new(parent, scope, "亲代覆盖", p.Id);
            if (p.Overrides.BloodlineId?.Value is int family) yield return new(family, scope, "血缘覆盖", p.Id);
        }
        foreach (var p in document.AdditionalOfficerDefaults) yield return new(p.Id, scope, "定义", p.Id, "额外武将");
        foreach (var reference in PersonReferences(document.AdditionalOfficerDefaults, scope + "额外武将")) yield return reference;
        foreach (var reference in RuleReferences(definitions, scope + "规则")) yield return reference;
    }

    private static void ValidateDeletedOfficerReferences(IEnumerable<OfficerReference> references, IReadOnlySet<int> deleted)
    {
        var blocked = references.Where(r => deleted.Contains(r.Id)).Select(r => $"武将 {r.Id}：{r.Location}").Distinct().ToArray();
        if (blocked.Length > 0) throw new InvalidDataException("已删除的武将仍被引用：\n" + string.Join('\n', blocked));
    }
}
