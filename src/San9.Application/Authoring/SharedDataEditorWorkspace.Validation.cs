using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Authoring;

public sealed partial class SharedDataEditorWorkspace
{
    // These fields have no world/reference dependencies. Every other change takes the scenario path.
    private bool RequiresScenarioValidation(ScenarioDefinitions draft)
    {
        if (_changedOfficers.Count != 0 || OfficerCatalog.NextOfficerId != _savedOfficers.NextOfficerId) return true;
        ScenarioDefinitions Dependencies(ScenarioDefinitions value) => value with
        {
            DefaultLoyalty = _saved.DefaultLoyalty,
            OfficerGrowth = _saved.OfficerGrowth,
            Information = value.Information with
            {
                Ranks = value.Information.Ranks.Select(r => r with { Name = "", Description = "" }).ToArray(),
                Offices = value.Information.Offices.Select(o => o with { Name = "", Description = "" }).ToArray(),
                Formations = value.Information.Formations.Select(f => f with { Name = "", Description = "" }).ToArray(),
                Tactics = value.Information.Tactics.Select(t => t with { Name = "", Description = "" }).ToArray(),
                Treasures = value.Information.Treasures.Select(t => t with { Name = "", Description = "", ImageKey = null }).ToArray()
            },
            Military = value.Military is { } military && _saved.Military is { } saved ? military with
            {
                DefensiveTacticChance = saved.DefensiveTacticChance,
                LinkRules = military.LinkRules with { KinshipBonus = saved.LinkRules.KinshipBonus,
                    Pairs = military.LinkRules.Pairs.Select(p => p with { Bonus = 0, SuppressesSiblingBonus = false }).ToImmutableArray() },
                CityInfluence = saved.CityInfluence
            } : value.Military
        };
        return !ContentComparison.Equal(Dependencies(draft), Dependencies(_saved));
    }

    private bool CatalogIdentitiesChanged(ScenarioDefinitions draft) =>
        draft.NextTreasureId != _saved.NextTreasureId ||
        !draft.Information.Treasures.Select(t => t.Id).Order().SequenceEqual(_saved.Information.Treasures.Select(t => t.Id).Order()) ||
        !draft.Information.Ranks.Select(r => r.Id).Order().SequenceEqual(_saved.Information.Ranks.Select(r => r.Id).Order()) ||
        !draft.Information.Offices.Select(o => o.Id).Order().SequenceEqual(_saved.Information.Offices.Select(o => o.Id).Order()) ||
        !draft.Information.Formations.Select(f => f.Id).Order().SequenceEqual(_saved.Information.Formations.Select(f => f.Id).Order()) ||
        !draft.Information.Tactics.Select(t => t.Id).Order().SequenceEqual(_saved.Information.Tactics.Select(t => t.Id).Order());

    private static void ValidateLocalRules(ScenarioDefinitions draft)
    {
        foreach (var treasure in draft.Information.Treasures) treasure.Validate();
        var treasureIds = draft.Information.Treasures.Select(t => t.Id).ToHashSet();
        var missingTreasures = TreasureReferenceIds(draft).Where(id => !treasureIds.Contains(id)).Distinct().ToArray();
        if (missingTreasures.Length > 0)
            throw new InvalidDataException($"共享规则引用不存在的宝物 {string.Join('、', missingTreasures)}，请先解除引用。");
        var tacticIds = draft.Information.Tactics.Select(t => t.Id)
            .Concat((draft.Military?.Tactics ?? []).Select(t => t.Id)).ToHashSet();
        if (draft.Information.Treasures.Any(t => t.TacticId is int id && !tacticIds.Contains(id)))
            throw new InvalidDataException("宝物持有兵法引用不存在的兵法。");
        if (draft.DefaultLoyalty is < 0 or > 255) throw new InvalidDataException("默认忠诚必须介于0与255之间。");
        draft.OfficerGrowth.Validate();
        if (draft.Information.Ranks.Any(r => string.IsNullOrWhiteSpace(r.Name)) ||
            draft.Information.Offices.Any(o => string.IsNullOrWhiteSpace(o.Name)) ||
            draft.Information.Formations.Any(f => string.IsNullOrWhiteSpace(f.Name)) ||
            draft.Information.Tactics.Any(t => string.IsNullOrWhiteSpace(t.Name)))
            throw new InvalidDataException("资料名称不能为空。");
        if (draft.Military is not { } military) return;
        MilitaryRulebook.ValidateDefensiveTacticChance(military.DefensiveTacticChance);
        military.LinkRules.Validate();
        military.CityInfluence?.Validate();
    }
}
