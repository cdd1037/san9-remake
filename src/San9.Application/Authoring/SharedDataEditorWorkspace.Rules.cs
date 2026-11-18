using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Application.Authoring;

public sealed partial class SharedDataEditorWorkspace
{
    public IReadOnlyList<TacticLinkPair> TacticLinkPairs => Definitions.Military?.LinkRules.Pairs ?? [];

    public void AddTacticLinkPair(int firstId, int secondId, int bonus, bool suppressesSiblingBonus = false)
    {
        var pair = ValidateTacticLinkPair(firstId, secondId, bonus, suppressesSiblingBonus);
        if (TacticLinkPairs.Any(p => p.FirstId == pair.FirstId && p.SecondId == pair.SecondId))
            throw new InvalidDataException("这两名武将已有联携关系，请选择现有条目修改。");
        ReplaceTacticLinkPairs(TacticLinkPairs.Append(pair));
    }

    public void SetTacticLinkPair(TacticLinkPair value)
    {
        var pair = ValidateTacticLinkPair(value.FirstId, value.SecondId, value.Bonus, value.SuppressesSiblingBonus);
        if (!TacticLinkPairs.Any(p => p.FirstId == pair.FirstId && p.SecondId == pair.SecondId))
            throw new InvalidDataException("找不到这条联携关系。");
        ReplaceTacticLinkPairs(TacticLinkPairs.Select(p => p.FirstId == pair.FirstId && p.SecondId == pair.SecondId ? pair : p));
    }

    public void RemoveTacticLinkPair(int firstId, int secondId) => ReplaceTacticLinkPairs(TacticLinkPairs.Where(p =>
        p.FirstId != Math.Min(firstId, secondId) || p.SecondId != Math.Max(firstId, secondId)));

    private TacticLinkPair ValidateTacticLinkPair(int firstId, int secondId, int bonus, bool suppressesSiblingBonus)
    {
        if (firstId == secondId) throw new InvalidDataException("请选择两名不同武将。");
        if (!Officers.Any(o => o.Id == firstId) || !Officers.Any(o => o.Id == secondId))
            throw new InvalidDataException("联携关系必须引用共享人物目录中的武将。");
        var pair = new TacticLinkPair(Math.Min(firstId, secondId), Math.Max(firstId, secondId), bonus, suppressesSiblingBonus);
        pair.Validate();
        return pair;
    }

    private void ReplaceTacticLinkPairs(IEnumerable<TacticLinkPair> pairs)
    {
        var military = Definitions.Military ?? throw new InvalidOperationException("内容库没有军事规则。");
        Definitions = Definitions with { Military = military with { LinkRules = military.LinkRules with
        { Pairs = pairs.OrderBy(p => p.FirstId).ThenBy(p => p.SecondId).ToImmutableArray() } } };
    }

    public void SetTacticProficiencyCap(int value)
    {
        var growth = Definitions.OfficerGrowth with { TacticProficiencyCap = value };
        growth.Validate();
        Definitions = Definitions with { OfficerGrowth = growth };
    }

    public void SetDefensiveTacticChance(int value)
    {
        MilitaryRulebook.ValidateDefensiveTacticChance(value);
        var military = Definitions.Military ?? throw new InvalidOperationException("内容库没有军事规则。");
        Definitions = Definitions with { Military = military with { DefensiveTacticChance = value } };
    }

    public void SetKinshipLinkBonus(int value)
    {
        var military = Definitions.Military ?? throw new InvalidOperationException("内容库没有军事规则。");
        var links = military.LinkRules with { KinshipBonus = value };
        links.Validate();
        Definitions = Definitions with { Military = military with { LinkRules = links } };
    }

    public void SetFacilityHarbor(int id, bool isHarbor)
    {
        var military = Definitions.Military ?? throw new InvalidOperationException("内容库没有军事规则。");
        var facility = military.Facilities.Single(f => f.Id == id);
        Definitions = Definitions with { Military = military with
        { Facilities = military.Facilities.Replace(facility, facility with { IsHarbor = isHarbor }) } };
    }

    public void SetCityInfluence(CityInfluenceRule value)
    {
        value.Validate();
        var military = Definitions.Military ?? throw new InvalidOperationException("内容库没有军事规则。");
        Definitions = Definitions with { Military = military with { CityInfluence = value } };
    }
}
