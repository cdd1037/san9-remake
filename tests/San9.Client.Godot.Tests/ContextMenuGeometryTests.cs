using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class ContextMenuGeometryTests
{
    [Theory]
    [InlineData(16, 56, true, false)]
    [InlineData(16, 680, true, true)]
    [InlineData(968, 56, false, false)]
    [InlineData(968, 680, false, true)]
    public void SubmenuKeepsItsParentEdgeAndFitsAtEveryCorner(float x, float y, bool right, bool bottom)
    {
        var menu = ContextMenuGeometry.Submenu(x, y, 40, 72, 240, 1024, 736);
        Assert.InRange(menu.X, 0, 1024 - 72);
        Assert.InRange(menu.Y, 0, 736 - 240);
        Assert.Equal(right ? x + 40 : x - 72, menu.X);
        Assert.Equal(bottom ? y + 24 : y, bottom ? menu.Y + 240 : menu.Y);
    }

    [Fact]
    public void WidescreenSubmenuUsesSpaceBeyondTheOriginalViewport()
    {
        var menu = ContextMenuGeometry.Submenu(1200, 100, 40, 72, 240, 1792, 736);
        Assert.Equal(1240, menu.X);
        Assert.Equal(100, menu.Y);
    }
}
