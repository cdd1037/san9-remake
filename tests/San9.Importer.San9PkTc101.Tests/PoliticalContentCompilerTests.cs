using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class PoliticalContentCompilerTests
{
    private static San9ScenarioPersonRecord Person(ushort biography) => new(
        0, 6000, 0, 0, 0, 0, 0, biography, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        [], 0, 0, [], 0);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(9, false)]
    public void HistoricalAndVirtualModesSharePoliticsAndSpecialModesDisableIt(byte mode, bool enabled)
    {
        var events = PoliticalContentCompiler.Events(mode);
        Assert.Equal(enabled, events is not null);
        if (events is not null) Assert.Equal((1000, "shanyang-gong"), (events.ProclamationTrust, events.CourtEndedConsumptionKey.Value));
    }

    [Fact]
    public void AllTitlesRetainNamesGlyphsAndExplicitSemanticEligibilityWithoutRulerMetadataConfusion()
    {
        var source = Enumerable.Range(0, 20).Select(id => new San9ScenarioCountryTitleRecord(id, 0, 0, 0, 999, ushort.MaxValue, ushort.MaxValue));
        var titles = PoliticalContentCompiler.Titles(source, [Person(32), Person(32), Person(60)], [1, 2], id => $"title.{id}", id => 100 + id);
        Assert.Equal(Enumerable.Range(0, 20), titles.Select(t => t.Id));
        Assert.All(titles, t => { Assert.Equal($"title.{t.Id}", t.NameKey); Assert.Equal(100 + t.Id, t.FlagGlyph); });
        Assert.Equal(new[] { 0, 1, 2, 3, 10, 11, 12, 13, 14, 15, 16, 17, 18 }, titles.Where(t => t.Rule.ManualSelectionAllowed).Select(t => t.Id));
        Assert.Equal(Enumerable.Range(4, 5), titles.Where(t => !t.Rule.AutomaticSelectionAllowed).Select(t => t.Id));
        Assert.Equal(9, Assert.Single(titles, t => t.Rule.PreventsInvestiture).Id);
        Assert.Equal(1, Assert.Single(titles.Single(t => t.Id == 3).Rule.PreferredRulerIds));
        Assert.Equal(326, Assert.Single(titles.Single(t => t.Id == 0).Rule.PreferredBloodlineIds));
        Assert.Equal(621, Assert.Single(titles.Single(t => t.Id == 1).Rule.PreferredBloodlineIds));
        Assert.Equal(347, Assert.Single(titles.Single(t => t.Id == 2).Rule.PreferredBloodlineIds));
        Assert.Equal(213, Assert.Single(titles.Single(t => t.Id == 19).Rule.PreferredBloodlineIds));
        Assert.Throws<InvalidDataException>(() => PoliticalContentCompiler.Titles([], [Person(32)], [2], _ => "x", _ => 0));
    }

    [Fact]
    public void ReverseProvinceLinksFollowLaterSourceRecordRegardlessOfInputEnumeration()
    {
        San9ScenarioCountryTitleRecord[] records = [new(18, 0, 0, 0, 0, 3, ushort.MaxValue), new(10, 0, 0, 0, 0, 3, 5)];
        var map = PoliticalContentCompiler.ProvinceTitles(records);
        Assert.Equal(2, map.Count); Assert.Equal(18, map[3]); Assert.Equal(10, map[5]);
        Assert.Equal(map.OrderBy(p => p.Key), PoliticalContentCompiler.ProvinceTitles(records.Reverse()).OrderBy(p => p.Key));
    }
}
