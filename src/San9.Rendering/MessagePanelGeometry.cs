using System.Text;

namespace San9.Rendering;

public static class MessagePanelGeometry
{
    public static (int X, int Width) Bounds(string text)
    {
        int columns = text.EnumerateRunes().Sum(r => r.IsAscii ? 1 : 2);
        int middleWidth = Math.Min(912, columns * 8 + 8);
        return (456 - middleWidth / 2, middleWidth + 112);
    }
}
