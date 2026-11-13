using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class FormationKnowledgeTests
{
    private static MilitaryRulebook Rules() => new(new MilitaryDefinition(1, 1, [0], [new(0, 1, null)],
        [new(6, 1, 0, 1000, 5, new(1, 1), TravelMode.Land) { IsCommon = true },
         new(28, 1, 0, 1000, 5, new(1, 1), TravelMode.Land)
         { IsCommon = true, MixedTroopsOnly = true, ResearchProjectId = "old-formation", Tactics = new(1, 1, [0], []) }], [])
    { ResearchProjects = [new("old-formation", 100, 30, [])] });

    [Fact]
    public void LegacyDefaultsVaryByOfficerAndPreserveCompletedResearch()
    {
        var rules = Rules();
        var weak = new OfficerState(1, 1, 1, new(10, 10, 10, 20), false);
        var strong = weak with { Id = 2, BaseAbilities = new(10, 10, 10, 90) };
        var without = new ForceState(1, 50);
        var withResearch = without with { CompletedResearch = ["old-formation"] };
        var first = FormationKnowledge.InitializeLegacy(weak, without, rules);
        var second = FormationKnowledge.InitializeLegacy(strong, without, rules);
        Assert.True(FormationKnowledge.Knows(first, 6));
        Assert.False(FormationKnowledge.Knows(first, 28));
        Assert.True(FormationKnowledge.Knows(second, 28));
        Assert.NotEqual(first.FormationProficiencies![6], second.FormationProficiencies![6]);
        Assert.True(FormationKnowledge.Knows(FormationKnowledge.InitializeLegacy(weak, withResearch, rules), 28));
        Assert.True(FormationKnowledge.Knows(FormationKnowledge.InitializeLegacy(weak, without, rules, 28), 28));
    }

    [Fact]
    public void ExplicitKnowledgeIsNotOverwritten()
    {
        var rules = Rules();
        var person = new OfficerState(1, 1, 1, new(10, 10, 10, 90), false)
        { FormationProficiencies = ImmutableSortedDictionary<int, int>.Empty.Add(6, 0) };
        Assert.Same(person, FormationKnowledge.InitializeLegacy(person, new ForceState(1, 50), rules));
        Assert.False(FormationKnowledge.Knows(person, 28));
    }

    [Fact]
    public void PracticeLearnsOnTwentiethDayWithoutChangingOtherKnowledge()
    {
        var officer = new OfficerState(1, 1, 1, new(10, 10, 10, 20), false)
        { FormationProficiencies = ImmutableSortedDictionary<int, int>.Empty.Add(6, 100) };
        for (int day = 0; day < 19; day++) officer = FormationKnowledge.Practice(officer, 28);
        Assert.False(FormationKnowledge.Knows(officer, 28));
        Assert.Equal(19, officer.FormationPractice![28]);
        officer = FormationKnowledge.Practice(officer, 28);
        Assert.Equal(0, officer.FormationProficiencies![28]);
        Assert.Equal(100, officer.FormationProficiencies[6]);
        Assert.Null(officer.FormationPractice);
        Assert.Same(officer, FormationKnowledge.Practice(officer, 28));
    }
}
