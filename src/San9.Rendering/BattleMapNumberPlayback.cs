using System.Globalization;
using San9.Content.Interface;

namespace San9.Rendering;

public sealed record BattleMapNumberFrame(string Text, int Row, double X, double Y);

public static class BattleMapNumberPlayback
{
    public static double Duration(BattleMapNumberDefinition definition) => definition.Steps * definition.FrameMilliseconds / 1000.0;

    public static BattleMapNumberFrame? Sample(BattleMapNumberDefinition definition, long amount, double elapsed)
    {
        if (amount == 0 || elapsed < 0 || elapsed >= Duration(definition)) return null;
        string text = amount.ToString("+0;-0", CultureInfo.InvariantCulture);
        int direction = amount < 0 ? definition.StepY : -definition.StepY;
        int step = (int)(elapsed * 1000 / definition.FrameMilliseconds);
        return new(text, amount < 0 ? 1 : 0, -text.Length * definition.GlyphWidth / 2.0,
            -direction * definition.Steps / 2.0 - definition.GlyphHeight / 2.0 + step * direction);
    }

    public static int Column(char glyph) => glyph switch { '+' => 10, '-' => 11, >= '0' and <= '9' => glyph - '0',
        _ => throw new ArgumentOutOfRangeException(nameof(glyph)) };
}
