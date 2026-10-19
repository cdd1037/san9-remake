using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class DecisionPresentationCompilerTests
{
    [Fact]
    public void DecisionNumbersBindToTheirDomainMeaning()
    {
        var people = new ushort[] { 671, 670, 666, 667 }.Select(biography => new San9ScenarioPersonRecord(
            0, 6000, 0, 0, 0, 0, 0, biography, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            [], 0, 0, [], 0)).ToArray();
        var decisions = DecisionPresentationCompiler.Compile(people);
        var volunteers = decisions.Events["volunteers"];

        Assert.Equal("messages.8013", volunteers.Prompt.Key);
        Assert.Equal("soldiers", volunteers.Prompt.Bindings["number.0"]);
        var offer = volunteers.Introduction.Single(m => m.Key == "messages.7925");
        Assert.Equal("soldiers", offer.Bindings["number.0"]);
        var result = volunteers.Results["Accept"].Single(m => m.Key == "messages.7926");
        Assert.Equal("soldiers", result.Bindings["number.0"]);
        Assert.Equal("soldiers", decisions.Events["bandit-mediation"].Results["success"]
            .Single(m => m.Key == "messages.7855").Bindings["number.0"]);
        Assert.Equal("dynasty-years", decisions.Events["emperor-transfer"].Results["decline"][0].Bindings["number.0"]);
        Assert.Equal("amount", decisions.Events["bribe"].Prompt.Bindings["number.0"]);
    }
}
