using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class TargetOverlayGeometryTests
{
    [Fact]
    public void GeneratedMasksHaveSolidColumnsAndExpectedSurfaceArea()
    {
        for (int mask = 0; mask < 16; mask++)
        {
            var pixels = TargetOverlayGeometry.Coverage(mask);
            int area = 1024 + ((mask & 1) != 0 ? 320 : 0) - ((mask & 2) != 0 ? 320 : 0);
            Assert.Equal(area, pixels.Count(p => p != 0));
            for (int x = 0; x < 64; x++)
            {
                var rows = Enumerable.Range(0, 44).Where(y => pixels[y * 64 + x] != 0).ToArray();
                Assert.Equal(rows[^1] - rows[0] + 1, rows.Length);
            }
        }
    }

    [Fact]
    public void CompatibleSlopesShareEdgesWithoutGapsOrDoubleCoverage()
    {
        for (int a = 0; a < 16; a++)
        for (int b = 0; b < 16; b++)
        for (int elevationDelta = -1; elevationDelta <= 1; elevationDelta++)
        {
            int right = (a >> 3) & 1, bottom = (a >> 1) & 1;
            if ((b & 1) + elevationDelta != right || ((b >> 2) & 1) + elevationDelta != bottom) continue;
            var first = TargetOverlayGeometry.Coverage(a);
            var second = TargetOverlayGeometry.Coverage(b);
            int offsetY = 16 - 10 * elevationDelta;
            bool Covered(byte[] pixels, int x, int y) => x is >= 0 and < 64 && y is >= 0 and < 44 && pixels[y * 64 + x] != 0;
            for (int x = 32; x < 64; x++)
            for (int y = 0; y < 44; y++)
                Assert.False(Covered(first, x, y) && Covered(second, x - 32, y - offsetY));
            // Sample both sides of the shared edge, away from the exterior vertices.
            for (int x = 40; x < 56; x++)
            {
                double edge = 27 - 10 * right + (16 + 10 * (right - bottom)) * (64 - x - 0.5) / 32;
                for (int y = (int)Math.Floor(edge); y <= (int)Math.Ceiling(edge); y++)
                    Assert.True(Covered(first, x, y) ^ Covered(second, x - 32, y - offsetY));
            }
        }
    }

    [Fact]
    public void InfluenceReplacesDimmingAndCandidateFootprintsRemainClear()
    {
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "fixture", "spring", 3, 1, 13056, 6656,
            [new(0, 1, 0, 0), new(0, 1, 0, 0), new(0, 3, 5, 0)], [], [], [], [], [], [], [], [], [], [], [], new([], []), []);
        var meshes = TargetOverlayGeometry.Meshes(map, new HashSet<MapCell> { new(0, 0), new(2, 0) },
            new HashSet<MapCell> { new(2, 0) }).ToArray();
        Assert.Equal(2, meshes.Length);
        Assert.Equal(0, meshes[0].ElevationMask);
        Assert.Equal(0.5f, meshes[0].Opacity);
        Assert.Equal(5, meshes[1].ElevationMask);
        Assert.Equal(51f / 256, meshes[1].Opacity);
        var center = MapGeometry.Center(2, 0);
        Assert.Equal((center.X - 32, center.Y - 46), (meshes[1].X, meshes[1].Y));
        var overlapping = TargetOverlayGeometry.Meshes(map, new HashSet<MapCell>(), new HashSet<MapCell> { new(2, 0) }).Last();
        Assert.InRange(overlapping.Opacity, 0.358f, 0.36f);
    }
}
