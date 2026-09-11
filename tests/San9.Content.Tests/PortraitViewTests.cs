using San9.Content.Images;

namespace San9.Content.Tests;

public sealed class PortraitViewTests
{
    [Theory]
    [InlineData(240, 38, 0, 160, 180, 70, 30, 96, 120)]
    [InlineData(960, 152, 0, 640, 720, 280, 120, 384, 480)]
    public void RelativeAnchorScalesBothCropsAndSharesTheMotherImage(int size,
        int bx, int by, int bw, int bh, int fx, int fy, int fw, int fh)
    {
        var image = new ImageAsset("portrait/9", "images/portraits/9.png", new string('A', 64), size, size, 3);
        var portrait = new PortraitDefinition(9, image, 38d / 240, 0);
        portrait.Validate();
        Assert.Same(image, portrait.GetView(PortraitView.Full));
        var bust = portrait.GetView(PortraitView.Bust);
        var face = portrait.GetView(PortraitView.Face);
        Assert.Equal((bx, by, bw, bh), (bust.X, bust.Y, bust.Width, bust.Height));
        Assert.Equal((fx, fy, fw, fh), (face.X, face.Y, face.Width, face.Height));
        Assert.Same(image.File, bust.File);
        Assert.Same(image.File, face.File);
        Assert.Equal(64d / 80, (double)face.Width / face.Height);
        Assert.Equal(32d / 40, (double)face.Width / face.Height);
    }

    [Fact]
    public void InvalidAnchorsCannotProduceOutOfBoundsViews()
    {
        var image = new ImageAsset("portrait/0", "images/portraits/0.png", new string('A', 64), 240, 240, 3);
        foreach (double x in new[] { -0.1, 0.4, double.NaN, double.PositiveInfinity })
            Assert.Throws<InvalidDataException>(() => new PortraitDefinition(0, image, x, 0).Validate());
        Assert.Throws<InvalidDataException>(() => new PortraitDefinition(0, image, 0, 0.3).Validate());
    }
}
