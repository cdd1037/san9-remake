using San9.Content.Scenarios;

namespace San9.Client.Godot.Tests;

public sealed class OpeningOfficerCatalogTests
{
    [Fact]
    public void SummaryAndTacticsPagesKeepLearningMasteryAndProficiencyIndependent()
    {
        var person = OfficerDefaults.Create(117) with
        {
            Name = "新甲", Surname = "新", LearnedTactics = [23, 27],
            MasteredCategories = [7], TacticProficiencies = [10, 20, 30, 40, 50, 60, 70, 80, 90]
        };
        Assert.Equal("新甲", OpeningOfficerCatalog.Cell(person, "name").Text);
        Assert.Equal(2, OpeningOfficerCatalog.Cell(person, "learned").Number);
        Assert.Equal(80, OpeningOfficerCatalog.Cell(person, "proficiency.7").Number);
        Assert.Equal("☆", OpeningOfficerCatalog.Cell(person, "mastery.7").Text);
        Assert.Equal("○", OpeningOfficerCatalog.Cell(person, "tactic.23").Text);
        Assert.Equal("", OpeningOfficerCatalog.Cell(person, "tactic.24").Text);
        Assert.Equal("", OpeningOfficerCatalog.Cell(person, "mastery.8").Text);
        Assert.Equal("○", OpeningOfficerCatalog.Cell(person, "tactic.27").Text);
    }
}
