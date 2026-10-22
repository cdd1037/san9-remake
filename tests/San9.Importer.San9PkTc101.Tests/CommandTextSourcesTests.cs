using San9.Content.Text;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class CommandTextSourcesTests
{
    [Fact]
    public void EveryRequiredRoleHasExactlyOneSource()
    {
        var entries = new Dictionary<string, string>();
        CommandText.AddRemakeEntries(entries);
        var keys = entries.Keys.Concat(CommandTextSources.Labels.Select(x => CommandText.Key(x.Role)))
            .Concat(CommandTextSources.Messages.Select(x => CommandText.Key(x.Role))).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal(CommandText.Parameters.Keys.Select(CommandText.Key).Order(), keys.Order());
    }

    [Fact]
    public void RefusingAnIncomingAttackRequestDoesNotDescribeCancellingAnOutgoingRequest()
    {
        var sources = CommandTextSources.Messages.ToDictionary(source => source.Role, source => source.Message);
        Assert.Equal(8338, sources["hint.reply-request-decline"]);
        Assert.NotEqual(sources["hint.decide-request-decline"], sources["hint.reply-request-decline"]);
    }

    [Theory]
    [InlineData("%s: %3d / %-2d (%%)", "{0}: {1,3} / {2,-2} (%)")]
    [InlineData("unchanged", "unchanged")]
    public void ConvertsSourceArgumentsAndPreservesAlignment(string source, string expected) =>
        Assert.Equal(expected, CommandTextSources.Format(source));
}
