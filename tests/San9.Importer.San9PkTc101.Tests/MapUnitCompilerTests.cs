using San9.Content.Maps;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class MapUnitCompilerTests
{
    private static MapUnitFormation[] Compile()
    {
        Dictionary<int, int> data = [];
        int[] thresholds = [6000, 12000, 20000, 30000, 40000, 50000];
        for (int i = 0; i < thresholds.Length; i++) data[0x656c78 + i * 4] = thresholds[i];
        for (int body = 0; body < 20; body++) data[0x6609d0 + body * 4] = body >= 10 ? 2 : body is >= 5 and <= 7 ? 1 : 0;
        int[] primaryBodies = [0, 0, 0, 0, 0, 0, 0, 0, 14, 12, 13, 10, 0, 0, 0, 4, 7, 6, 1, 2, 16, 17, 18, 19, 16, 16, 17, 0];
        for (int id = 0; id < primaryBodies.Length; id++) data[0x663d98 + id * 4] = primaryBodies[id];
        for (int body = 0; body < 20; body++) data[0x66596c + body * 8] = -50 - body;
        int[] center = [1, 5, 5, 9, 9, 9, 9], outer = [1, 3, 4, 5, 7, 9, 11];
        int[] secondaryCenter = [1, 1, 1, 1, 1, 5, 5], secondaryOuter = [1, 1, 2, 2, 2, 3, 3];
        int[] largeOuter = [1, 1, 2, 5, 7, 9, 11];
        int[] effect = [0, 1, 1, 2, 2, 3, 3];
        for (int scale = 0; scale < 7; scale++)
        {
            data[0x616078 + scale * 12] = 5646 + effect[scale];
            data[0x61607c + scale * 12] = -40 - effect[scale];
            data[0x616080 + scale * 12] = -50 - effect[scale];
            for (int category = 0; category < 3; category++)
            {
                int row = scale * 3 + category;
                data[0x660a20 + row * 8] = category switch { 0 => center[scale], 1 => secondaryCenter[scale], _ => 1 };
                data[0x660a24 + row * 8] = category switch { 0 => outer[scale], 1 => secondaryOuter[scale], _ => largeOuter[scale] };
                int groups = 0x700000 + row * 32;
                data[0x660c70 + row * 4] = groups;
                for (int group = 0; group < 5; group++)
                {
                    int points = 0x800000 + row * 1024 + group * 128;
                    data[groups + group * 4] = points;
                    for (int i = 0; i < 12; i++)
                    {
                        data[points + i * 8] = category * 100 + group * 10 + i;
                        data[points + i * 8 + 4] = -scale - i;
                    }
                }
            }
        }
        var bodies = Enumerable.Range(0, 20).Select(i => new MapUnitBody($"body-{i}", [], 0, 0)).ToArray();
        return MapUnitCompiler.CompileFormations(a => data[a], bodies, ["small", "medium", "large", "largest"]);
    }

    [Fact]
    public void OrdinaryLayoutsKeepAllSevenSourceScalesAndSharedEffects()
    {
        var formations = Compile();
        var layouts = formations[0].Layouts;
        Assert.Equal(new uint[] { 1, 6000, 12000, 20000, 30000, 40000, 50000 }, layouts.Select(l => l.MinimumSoldiers));
        Assert.Equal(new[] { 5, 17, 21, 29, 37, 45, 53 }, layouts.Select(l => l.Members.Length));
        Assert.Equal(new[] { "small", "medium", "medium", "large", "large", "largest", "largest" }, layouts.Select(l => l.Influence.ClipId));
        Assert.Equal(new MapUnitMember("body-0", 24, -7), layouts[3].Members[18]);
        for (int id = 1; id < 8; id++)
            for (int scale = 0; scale < 7; scale++)
                Assert.Equal(layouts[scale].Members, formations[id].Layouts[scale].Members);
    }

    [Fact]
    public void SiegeEscortsUseLargeBodyLayoutAndShipsOnlyHaveTheCentralGroup()
    {
        var formations = Compile();
        var siege = formations[8].Layouts[2];
        Assert.Equal(9, siege.Members.Length);
        Assert.Equal(new MapUnitMember("body-14", 200, -2), siege.Members[0]);
        Assert.Equal(new MapUnitMember("body-0", 210, -2), siege.Members[1]);
        foreach (int id in new[] { 8, 9, 10, 11 })
        {
            Assert.Equal(new[] { 4, 4, 8, 20, 28, 36, 44 },
                formations[id].Layouts.Select(l => l.Members.Count(m => m.BodyId == "body-0")));
            Assert.All(formations[id].Layouts, l => Assert.Single(l.Members, m => m.BodyId != "body-0"));
        }
        var ship = Assert.Single(formations[22].Layouts);
        Assert.Equal(1u, ship.MinimumSoldiers);
        Assert.Single(ship.Members);
        Assert.Equal("large", ship.Influence.ClipId);
        Assert.Equal(-4, ship.Members[0].Y);
        foreach (int id in Enumerable.Range(20, 7))
            Assert.Single(Assert.Single(formations[id].Layouts).Members);
    }

    [Fact]
    public void SecondaryCategoryBodiesUseTheirOwnCountsAndOffsetsAcrossAllScales()
    {
        var formations = Compile();
        foreach (int id in new[] { 16, 17 })
        {
            var layouts = formations[id].Layouts;
            Assert.Equal(new[] { 5, 5, 9, 9, 9, 17, 17 }, layouts.Select(l => l.Members.Length));
            string body = id == 16 ? "body-7" : "body-6";
            Assert.All(layouts.SelectMany(l => l.Members), m => Assert.Equal(body, m.BodyId));
            Assert.Equal(new MapUnitMember(body, 100, -2), layouts[2].Members[0]);
            Assert.Equal(new MapUnitMember(body, 110, -2), layouts[2].Members[1]);
            Assert.Equal(new MapUnitMember(body, 104, -9), layouts[5].Members[4]);
        }
    }
}
