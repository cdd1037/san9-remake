using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class OfficerDetailTests
{
    [Fact]
    public void PersonalPeerageAppearsSeparatelyFromOffice()
    {
        var definition = G01ScenarioTests.Fixture();
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            definition.Officers.Select(o => o.NameKey).Concat(definition.Settlements.Select(s => s.NameKey))
                .Distinct().ToDictionary(key => key, key => key));
        var view = DeploymentDraftTests.View(world => world with
        {
            Officers = world.Officers.Select(person => person.Id == 1
                ? person with { PersonalPeerage = new(PersonalPeerageGrade.VillageMarquis, "汉寿亭侯") }
                : person).ToArray()
        });
        var fields = OfficerDetails.Read(view, texts, 1).Fields.SelectMany(column => column).ToArray();
        Assert.Contains(fields, field => field.Label == "官职");
        Assert.Contains(fields, field => field.Label == "爵位" && field.Value == "汉寿亭侯");
        var table = OfficerCatalog.Read(view, texts, 3, null, null, 1);
        Assert.Equal("汉寿亭侯", Assert.Single(table.Rows).Cells[Array.IndexOf(table.Columns, "爵位")].Text);
    }

    [Fact]
    public void FormationPageShowsPersonalKnowledgeAndDistinguishesZeroFromUnknown()
    {
        var definition = G01ScenarioTests.Fixture();
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            definition.Officers.Select(o => o.NameKey).Concat(definition.Settlements.Select(s => s.NameKey))
                .Distinct().ToDictionary(key => key, key => key));
        var view = DeploymentDraftTests.View(world => world with
        {
            Simulation = world.Simulation with { NewGame = world.Simulation.NewGame with { MixedTroopsEnabled = true } },
            Officers = world.Officers.Select(person => person.Id == 1
                ? person with { FormationProficiencies = ImmutableSortedDictionary<int, int>.Empty.Add(100, 0) }
                : person).ToArray()
        });
        var detail = OfficerDetails.Read(view, texts, 1);
        Assert.True(detail.FormationRulesEnabled);
        Assert.Equal(0, Assert.Single(detail.Formations, f => f.Name == "fish").Proficiency);
        Assert.Null(Assert.Single(detail.Formations, f => f.Name == "snake").Proficiency);
        var state = new OfficerDetailState();
        state.Open([1, 2], 1); state.SelectPage(OfficerDetailPage.Formations); state.Activate(2);
        Assert.Equal(OfficerDetailPage.Formations, state.Page);
    }

    [Fact]
    public void CallerOrderFocusDisplayedPersonAndPageAreIndependent()
    {
        var ids = Enumerable.Range(0, 24).Reverse().ToArray();
        var state = new OfficerDetailState(); state.Open(ids, 22);
        Assert.Equal(ids, state.Candidates);
        state.SelectPage(OfficerDetailPage.Tactics);
        state.PointAt(16);
        Assert.Equal(22, state.CurrentId);
        state.Activate(16);
        Assert.Equal(16, state.CurrentId);
        Assert.Equal(OfficerDetailPage.Tactics, state.Page);
        Assert.Equal(ids, state.Candidates);
    }
    [Theory]
    [InlineData(18, 0)]
    [InlineData(19, 1)]
    [InlineData(32, 14)]
    public void WholeListScrollsWithoutAFixedRulerRow(int count, int maximum)
    {
        var state = new OfficerDetailState(); state.Open(Enumerable.Range(0,count), 0);
        state.Scroll(int.MaxValue); Assert.Equal(maximum, state.Offset);
        state.Scroll(-1); Assert.Equal(0,state.Offset);
        state.PointAt(17); state.MoveFocus(1);
        Assert.Equal(count>18?18:17,state.FocusedId);
        Assert.Equal(count>18?1:0,state.Offset);
        Assert.Equal(0,state.CurrentId);
    }
}
