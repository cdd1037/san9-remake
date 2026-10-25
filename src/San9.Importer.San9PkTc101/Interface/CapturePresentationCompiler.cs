using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class CapturePresentationCompiler
{
    internal static CapturePresentationDefinition Compile() => new(new()
    {
        ["won"] = new("messages.7387", new()
            { ["person.0"] = "ruler", ["building.0"] = "place", ["force.0"] = "force" }),
        ["lost"] = new("messages.7389", new()
            { ["building.0"] = "place", ["person.0"] = "actor", ["force.0"] = "previous-force", ["force.1"] = "force" }),
        ["won-transfer"] = new("messages.7388", new()
            { ["person.0"] = "ruler", ["building.0"] = "place", ["force.0"] = "force" }),
        ["lost-transfer"] = new("messages.7390", new()
            { ["person.0"] = "ruler", ["building.0"] = "place", ["force.0"] = "force" })
    }, Enumerable.Range(0, 17).ToDictionary(type => type,
        type => new CapturePlacePresentation(type is 0 or 2 or 6 ? "capture-settlement" : "capture-fortification", type == 0)),
        "capture-victory-music", "capture-loss-music", "settlement-capture");
}
