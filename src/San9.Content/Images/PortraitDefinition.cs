namespace San9.Content.Images;

public enum PortraitView { Full, Bust, Face }

/// <summary>One normal-color image and its normalized 160:180 crop origin.</summary>
public sealed record PortraitDefinition(int Id, ImageAsset Image, double AnchorX, double AnchorY)
{
    public void Validate()
    {
        Image.Validate();
        if (Id < 0 || !double.IsFinite(AnchorX) || !double.IsFinite(AnchorY) ||
            AnchorX < 0 || AnchorY < 0 || AnchorX + 160d / 240 > 1 + 1e-12 ||
            AnchorY + 180d / 240 > 1 + 1e-12 || Image.Width != Image.Height)
            throw new InvalidDataException("Invalid portrait image or normalized anchor.");
    }

    public ImageAsset GetView(PortraitView view)
    {
        if (view == PortraitView.Full) return Image;
        var (x, y, w, h) = view switch
        {
            PortraitView.Bust => (AnchorX, AnchorY, 160d / 240, 180d / 240),
            PortraitView.Face => (AnchorX + 32d / 240, AnchorY + 30d / 240, 96d / 240, 120d / 240),
            _ => throw new ArgumentOutOfRangeException(nameof(view))
        };
        int left = (int)Math.Round(x * Image.Width), top = (int)Math.Round(y * Image.Height);
        int right = (int)Math.Round((x + w) * Image.Width), bottom = (int)Math.Round((y + h) * Image.Height);
        return new($"{Image.Id}/{view.ToString().ToLowerInvariant()}", Image.File,
            Image.X + left, Image.Y + top, right - left, bottom - top);
    }
}
