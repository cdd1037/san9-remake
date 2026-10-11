using System.Collections.Immutable;
using System.Globalization;
using San9.Content.Interface;

namespace San9.Rendering;

public sealed record BattleNumberGlyph(BattleNumberSheet Sheet, double X, double Y, double SourceY, float Opacity = 1);
public sealed record BattleRollingDigit(int Digit, int InitialFrame, double Start, double End, int Column);

/// <summary>Prepares rolling digits once; rendering and sound share the same stopping boundaries.</summary>
public sealed class BattleZoomNumberPlayback
{
    public BattleZoomNumbers Definition { get; }
    public ImmutableArray<BattleRollingDigit> Digits { get; }
    public ImmutableArray<BattleZoomSound> Sounds { get; }
    public double Start { get; }
    public double End => Digits[^1].End;

    public BattleZoomNumberPlayback(BattleZoomNumbers definition, uint amount, bool blocked, double start, Func<int, int> next)
    {
        Definition = definition; Start = start;
        string text = amount.ToString(CultureInfo.InvariantCulture);
        List<BattleRollingDigit> digits = []; List<BattleZoomSound> sounds = [];
        double cursor = start;
        // The native rolling sheet advances in quarter-glyph increments over 39 positions.
        for (int i = text.Length - 1; i >= 0; i--)
        {
            int digit = text[i] - '0', initial = (digit + 5) % 10 * 4 + next(4);
            int steps = (digit * 4 - initial + 39) % 39;
            if (initial == 39) steps = digit * 4 + 1;
            if (steps == 0) steps = 39;
            double end = cursor + steps * definition.RollFrameMilliseconds / 1000.0;
            digits.Add(new(digit, initial, cursor, end, i));
            sounds.Add(new(end, i == 0 ? blocked ? definition.BlockedSound : definition.CompletedSound : definition.DigitSound));
            cursor = end;
        }
        Digits = digits.ToImmutableArray(); Sounds = sounds.ToImmutableArray();
    }

    public bool IsRolling(double elapsed) => elapsed >= Start && elapsed < End;

    public IEnumerable<BattleNumberGlyph> SampleTotal(double elapsed)
    {
        var sheet = Definition.Total;
        foreach (var digit in Digits)
        {
            if (elapsed < digit.Start) continue;
            int steps = (int)((elapsed - digit.Start) * 1000 / Definition.RollFrameMilliseconds);
            int rollingFrame = digit.InitialFrame == 39 ? steps == 0 ? 39 : (steps - 1) % 39 : (digit.InitialFrame + steps) % 39;
            int frame = elapsed >= digit.End ? digit.Digit * 4 : rollingFrame;
            yield return new(sheet, Definition.Position.X - (Digits.Length - 1 - digit.Column) * sheet.Advance,
                Definition.Position.Y, frame * sheet.Height / 4.0);
        }
    }

    public IEnumerable<BattleNumberGlyph> SampleSmall(BattleZoomImpact impact, double elapsed)
    {
        double age = elapsed - impact.Time, interval = Definition.SmallFrameMilliseconds / 1000.0;
        if (age < 0 || age >= 16 * interval) yield break;
        int steps = (int)(age / interval);
        int offset = Enumerable.Range(0, Math.Min(steps, 8)).Sum(i => i % 2 == 0 ? 8 - i : i - 8);
        float opacity = steps <= 8 ? 1 : (128 - 16 * (steps - 9)) / 256f;
        var sheet = Definition.Small;
        string text = impact.Amount.ToString(CultureInfo.InvariantCulture);
        double right = sheet.Advance * text.Length / 2 + impact.X - sheet.Width;
        for (int i = 0; i < text.Length; i++)
            yield return new(sheet, right - (text.Length - 1 - i) * sheet.Advance,
                impact.Y - sheet.Height / 2 + offset, (text[i] - '0') * sheet.Height, opacity);
    }
}
