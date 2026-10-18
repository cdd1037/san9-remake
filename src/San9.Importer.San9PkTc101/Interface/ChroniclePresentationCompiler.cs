using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Interface;

internal static class ChroniclePresentationCompiler
{
    // RE-0084: year-table message family, independent of map report messages.
    internal static void AddMessages(Dictionary<string, MessageDefinition> messages)
    {
        static MessageNode Person(string slot) => new MessageStyled("blue-outline",
            [new MessageValue(new("property", Binding: "person.name", Arguments: [new("binding", Binding: slot)]))]);
        messages.Add("presentation.chronicle.emperor-succession", new([new MessageLiteral("因"), Person("person.1"),
            new MessageLiteral("的崩殂，"), Person("person.2"), new MessageLiteral("继位。") ]));
    }
    internal static ChroniclePresentationDefinition Compile()
    {
        static DecisionMessageDefinition M(int id) => new($"messages.{id}", new()
        {
            ["person.0"] = "ruler", ["person.1"] = "actor", ["person.2"] = "other",
            ["building.0"] = "place", ["string.0"] = "name", ["string.1"] = "other-name"
        });
        return new(new()
        {
            ["emperor-succession"] = M(11426) with { Key = "presentation.chronicle.emperor-succession" },
            ["rule"] = M(11357),
            ["rule-ranked"] = M(11358),
            ["ruler-hosted"] = M(11359),
            ["ruler-hosted-ranked"] = M(11360),
            ["adviser"] = M(11361),
            ["adviser-appointed"] = M(11362),
            ["demolition"] = M(11363) with { Bindings = new() { ["building.0"] = "place", ["string.0"] = "facility-name" } },
            ["city-surrendered"] = M(11364),
            ["cities-surrendered"] = M(11365),
            ["facility-surrendered"] = M(11366),
            ["facilities-surrendered"] = M(11367),
            ["city-defected"] = M(11368),
            ["cities-defected"] = M(11369),
            ["facility-defected"] = M(11370),
            ["facilities-defected"] = M(11371),
            ["unified"] = M(11372),
            ["natural-death"] = M(11373),
            ["force-absorbed"] = M(11374),
            ["force-surrendered"] = M(11375),
            ["city-enticed"] = M(11376),
            ["city-enticed-lost"] = M(11377),
            ["facility-enticed"] = M(11378),
            ["facility-enticed-lost"] = M(11379),
            ["execution"] = M(11380),
            ["execution-local"] = M(11381),
            ["construction"] = M(11382) with { Bindings = new() { ["building.0"] = "place", ["string.0"] = "facility-name" } },
            ["request-accepted"] = M(11383),
            ["request-promised"] = M(11384),
            ["city-captured"] = M(11385),
            ["city-lost"] = M(11386),
            ["facility-captured"] = M(11387),
            ["facility-lost"] = M(11388),
            ["succession"] = M(11389),
            ["independence"] = M(11390),
            ["founding"] = M(11391),
            ["story-ended"] = M(11392),
            ["eliminated"] = M(11393),
            ["eliminated-named"] = M(11394),
            ["request-fulfilled"] = M(11395),
            ["request-fulfilled-peer"] = M(11396),
            ["request-expired"] = M(11397),
            ["request-expired-peer"] = M(11398),
            ["request-broken"] = M(11399),
            ["request-broken-peer"] = M(11400),
            ["request-place-destroyed"] = M(11401),
            ["request-place-destroyed-peer"] = M(11402),
            ["request-owner-changed"] = M(11403),
            ["request-owner-changed-peer"] = M(11404),
            ["request-force-eliminated"] = M(11405),
            ["request-force-eliminated-peer"] = M(11406),
            ["chained-prelude"] = M(11407),
            ["chained-coup"] = M(11408),
            ["visits-recommendation"] = M(11409),
            ["visits-first"] = M(11410),
            ["visits-recruited"] = M(11411),
            ["wa-discovered"] = M(11412),
            ["ceremony"] = M(11413),
            ["abdication-han"] = M(11414),
            ["emperor-killed"] = M(11415),
            ["memorial"] = M(11416),
            ["five-tigers"] = M(11417),
            ["qiao-marriage"] = M(11418),
            ["sun-marriage"] = M(11419),
            ["guandu-won"] = M(11420),
            ["guandu-lost"] = M(11421),
            ["red-cliffs-won"] = M(11422),
            ["red-cliffs-lost"] = M(11423),
            ["emperor-supported"] = M(11424),
            ["emperor-deposed"] = M(11425),
            ["ling-succession"] = M(11426),
            ["shao-succession"] = M(11427),
            ["emperor-abdication"] = M(11428),
            ["emperor-proclamation"] = M(11429),
            ["emperor-enthroned"] = M(11430),
            ["rank-appointed"] = M(11431),
            ["rank-proclaimed"] = M(11432),
            ["alliance-led"] = M(11433),
            ["alliance-joined"] = M(11434),
            ["alliance-formed"] = M(11435),
            ["alliance-victory"] = M(11436),
            ["alliance-leader-eliminated"] = M(11437),
            ["alliance-leader-died"] = M(11438),
            ["alliance-empty"] = M(11439),
            ["alliance-target-eliminated"] = M(11440),
            ["alliance-target-died"] = M(11441),
            ["alliance-expired"] = M(11442),
            ["alliance-betrayed"] = M(11443),
            ["alliance-member-expelled"] = M(11444),
            ["alliance-member-left"] = M(11445),
            ["consolidation"] = M(11446),
            ["duel-death"] = M(11447),
            ["sniping-death"] = M(11448),
            ["melee-death"] = M(11449),
            ["soldier-promoted"] = M(11450),
            ["city-attacked"] = M(11451),
            ["facility-attacked"] = M(11452),
            ["emperor-abdication-untitled"] = M(11453),
            ["emperor-proclamation-untitled"] = M(11454),
        }, new()
        {
            ["records"] = M(8404), ["summary"] = M(8543), ["detailed"] = M(8542),
            ["close"] = M(8354), ["chronicle"] = M(8540), ["input"] = M(8541),
            ["records-help"] = M(9235), ["records-help-title"] = M(9234), ["records-help-detail"] = M(9212),
            ["records-help-detail-title"] = M(9211), ["chronicle-help"] = M(9285), ["chronicle-help-title"] = M(9284)
        });
    }
}
