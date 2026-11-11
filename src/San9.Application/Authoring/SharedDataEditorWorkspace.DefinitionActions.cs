using San9.Content.Packages;
using San9.Content.Scenarios;

namespace San9.Application.Authoring;

public sealed partial class SharedDataEditorWorkspace
{
    private enum DefinitionKind { Formation, Tactic, Office, Rank, Treasure }

    private async Task<int> AllocateDefinitionIdentityAsync(DefinitionKind kind, CancellationToken token)
    {
        IEnumerable<int> Ids(ScenarioDefinitions definitions) => kind switch
        {
            DefinitionKind.Formation => (definitions.Military?.Formations ?? []).Select(f => f.Id)
                .Concat(definitions.Information.Formations.Select(f => f.Id)),
            DefinitionKind.Tactic => (definitions.Military?.Tactics ?? []).Select(t => t.Id)
                .Concat(definitions.Information.Tactics.Select(t => t.Id)),
            DefinitionKind.Office => definitions.Information.Offices.Select(o => o.Id),
            DefinitionKind.Rank => definitions.Information.Ranks.Select(r => r.Id)
                .Concat(definitions.Information.Offices.Select(o => o.RequiredRankId).OfType<int>()),
            DefinitionKind.Treasure => definitions.Information.Treasures.Select(t => t.Id).Concat(TreasureReferenceIds(definitions)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        long Watermark(ScenarioDefinitions definitions) => kind switch
        {
            DefinitionKind.Formation => definitions.NextFormationId,
            DefinitionKind.Tactic => definitions.NextTacticId,
            DefinitionKind.Office => definitions.NextOfficeId,
            DefinitionKind.Rank => definitions.NextRankId,
            DefinitionKind.Treasure => definitions.NextTreasureId,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        long next = Math.Max(Watermark(Definitions), Watermark(_baseDefinitions));
        int maximum = Ids(Definitions).Concat(Ids(_baseDefinitions)).DefaultIfEmpty(-1).Max();
        var files = new ContentReadSession();
        foreach (var entry in _scenarios.Where(e => PackageFiles.Resolve(e.Root, e.Manifest.Definitions).Equals(DefinitionPath, StringComparison.OrdinalIgnoreCase)))
        {
            var document = await files.ReadAsync<ScenarioDocument>(PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(entry.Root, entry.Manifest.Scenario)), token);
            maximum = Math.Max(maximum, Ids(document.DefinitionOverrides.Apply(Definitions)).DefaultIfEmpty(-1).Max());
            if (kind == DefinitionKind.Office)
                maximum = Math.Max(maximum, document.Officers.Select(o => o.OfficeId).OfType<int>().DefaultIfEmpty(-1).Max());
            if (kind == DefinitionKind.Rank)
                maximum = Math.Max(maximum, document.Forces.Select(f => f.RankId).OfType<int>().DefaultIfEmpty(-1).Max());
            if (kind == DefinitionKind.Treasure)
                maximum = Math.Max(maximum, document.Information.TreasureStates.Select(t => t.Id).DefaultIfEmpty(-1).Max());
        }
        next = Math.Max(next, (long)maximum + 1);
        if (next > int.MaxValue) throw new InvalidDataException("无法分配新的共享资料身份。");
        return (int)next;
    }

    private async Task ValidateDefinitionDeletionAsync(ScenarioDefinitions draft, ScenarioDefinitions candidate, string subject, CancellationToken token)
    {
        var people = OfficerCatalog;
        try { await ValidateDraftAsync(candidate, token); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        { throw new InvalidDataException($"无法删除{subject}：请先解除引用并修正资料。\n{error.Message}", error); }
        EnsureDefinitionDraft(draft, token);
        if (!ReferenceEquals(people, OfficerCatalog)) throw new InvalidDataException("检查期间人物草稿已改变，请重试。");
        Definitions = candidate;
    }

    private void EnsureDefinitionDraft(ScenarioDefinitions draft, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(draft, Definitions)) throw new InvalidDataException("检查期间共享资料草稿已改变，请重试。");
    }
}
