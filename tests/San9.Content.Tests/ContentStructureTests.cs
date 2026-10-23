using System.Collections.Immutable;
using San9.Content.Packages;

namespace San9.Content.Tests;

public sealed class ContentStructureTests
{
    private sealed record Example(string? Optional, Dictionary<string, string[]> Groups, ImmutableArray<string> Values);

    [Fact]
    public void RequiredCollectionElementsRetainTheirPreciseErrorLocation()
    {
        var content = new Example(null, new() { ["first"] = ["valid"], ["second"] = ["valid", null!] }, []);
        var error = Assert.Throws<InvalidDataException>(() => ContentStructure.Validate(content));
        Assert.Contains("$.Groups[second][1]", error.Message);
    }

    [Fact]
    public void NullableValuesAndSharedCollectionsAreAcceptedButUninitializedCollectionsAreRejected()
    {
        string[] shared = ["valid"];
        var content = new Example(null, new() { ["first"] = shared, ["second"] = shared }, []);
        ContentStructure.Validate(content);
        var error = Assert.Throws<InvalidDataException>(() => ContentStructure.Validate(content with { Values = default }));
        Assert.Contains("$.Values", error.Message);
    }
}
