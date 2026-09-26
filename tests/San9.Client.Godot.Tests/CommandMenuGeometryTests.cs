using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class CommandMenuGeometryTests
{
    [Theory]
    [InlineData(20, 20, 268, 2)]
    [InlineData(1000, 760, 934, 458)]
    [InlineData(600, 400, 587, 382)]
    public void PrimaryKeepsSummaryAndDescriptionSpace(float x, float y, float expectedX, float expectedY)
    {
        Assert.Equal((expectedX, expectedY), CommandMenuGeometry.Primary(x, y, 6));
        Assert.Equal((expectedX, expectedY), CommandMenuGeometry.Primary(x, y, 2));
    }

    [Theory]
    [InlineData(400, 300, 468, 300)]
    [InlineData(400, 620, 468, 424)]
    [InlineData(945, 300, 877, 300)]
    [InlineData(945, 620, 877, 424)]
    public void SubmenuUsesNearestFittingCorner(float x, float y, float expectedX, float expectedY) =>
        Assert.Equal((expectedX, expectedY), CommandMenuGeometry.Secondary(x, y, 8));

    [Fact]
    public void BothColumnsAndIllustrationStayOnScreenAtEveryMapEdge()
    {
        foreach (int x in new[] { 0, 268, 512, 800, 1024 })
        foreach (int y in new[] { 0, 400, 736 })
        {
            var primary = CommandMenuGeometry.Primary(x, y, 6);
            var art = CommandMenuGeometry.Illustration(primary.X, primary.Y);
            Assert.InRange(art.X, 268, 1024 - 188);
            Assert.InRange(art.Y + 278, 278, 736);
            for (int row = 0; row < 6; row++)
            {
                var child = CommandMenuGeometry.Secondary(primary.X + 11, primary.Y + 16 + row * 28, 8);
                Assert.InRange(child.X, 0, 1024 - 68);
                Assert.InRange(child.Y, 0, 736 - 224);
                Assert.True(child.X + 68 <= primary.X + 11 || child.X >= primary.X + 79);
            }
        }
    }
}
