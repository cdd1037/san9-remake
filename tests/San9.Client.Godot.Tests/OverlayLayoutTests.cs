using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class OverlayLayoutTests
{
    [Theory]
    [InlineData(1024)]
    [InlineData(1365.333)]
    [InlineData(1792)]
    public void OverlayClearsAnchoredSidebarAndBottomBand(double width)
    {
        var viewport = new OverlayBounds(0, 0, width, 768);
        OverlayBounds[] occupied = [new(width - 188, 167, 188, 570), new(0, 736, width, 32)];
        var placed = OverlayLayout.Place(new(width - 180, 650, 160, 316), viewport, occupied);
        Assert.All(occupied, obstacle => Assert.Equal(0, placed.Overlap(obstacle)));
        Assert.InRange(placed.X, 0, width - placed.Width);
        Assert.InRange(placed.Y, 0, 768 - placed.Height);
        Assert.Equal(placed, OverlayLayout.Place(placed, viewport, occupied));
    }

    [Fact]
    public void RecedingPanelReleasesSpaceWithoutChangingPreferredAnchor()
    {
        var viewport = new OverlayBounds(0, 0, 1365, 768);
        var desired = new OverlayBounds(50, 550, 280, 108);
        var expanded = OverlayLayout.Place(desired, viewport, [new(13, 564, 424, 208)]);
        Assert.NotEqual(desired, expanded);
        Assert.Equal(desired, OverlayLayout.Place(desired, viewport, [new(13, 778, 424, 208)]));
    }

    [Fact]
    public void SpeechCanMoveBeyondItsFourAnchorsToClearHud()
    {
        var viewport = new OverlayBounds(0, 0, 1365, 768);
        OverlayBounds[] occupied = [new(900, 0, 465, 210), new(1177, 167, 188, 570), new(0, 736, 1365, 32)];
        var speech = MapSpeechLayout.Place(1280, 160, 280, MapSpeechPlacement.RightBelow, viewport, occupied);
        Assert.All(occupied, obstacle => Assert.Equal(0, speech.Bounds.Overlap(obstacle)));
    }

    [Fact]
    public void FullyOccupiedViewportKeepsSizeAndDeterministicPlacement()
    {
        var viewport = new OverlayBounds(0, 0, 400, 300);
        var desired = new OverlayBounds(390, 290, 160, 100);
        var placed = OverlayLayout.Place(desired, viewport, [viewport]);
        Assert.Equal(new OverlayBounds(240, 200, 160, 100), placed);
        Assert.Equal(placed, OverlayLayout.Place(desired, viewport, [viewport]));
    }
}
