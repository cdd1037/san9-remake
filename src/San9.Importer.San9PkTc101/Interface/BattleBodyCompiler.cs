using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class BattleBodyCompiler
{
    internal static BattleBodyMotion Motion(int mode, int body) => new(mode switch
    {
        1 => [new(-6, 3), new(-6, 3), new(-6, 3), new(-6, 3), new(18, -9), new(18, -9), new(-6, 3), new(-6, 3)],
        2 => Enumerable.Range(0, 8).Select(i => new BattlePoint(i % 2 == 0 ? 2 : 0, i % 2 == 0 ? -1 : 0)).ToArray(),
        4 => Enumerable.Repeat(new BattlePoint(2, -1), 8).ToArray(),
        5 => Enumerable.Range(0, 8).Select(i => new BattlePoint(i % 4 == 0 ? 2 : 0, i % 4 == 0 ? -1 : 0)).ToArray(),
        15 => [new(16, -8), new(16, -8), new(16, -8), new(16, -8), new(-16, 8), new(-16, 8), new(-16, 8), new(-16, 8)],
        _ => []
    }, new(0, 0), 125, mode == 15 ? 187 : 0,
        mode is 2 or 5 ? body is 7 or 8 ? 8 : 16 : mode == 8 ? 4 : 0,
        mode == 8 || body is 7 or 8 ? 96 / 256f : .5f)
    {
        ReactingSteps = mode == 4 ? Enumerable.Repeat(new BattlePoint(2, -1), 8).ToArray() : mode == 5 ?
            Enumerable.Range(0, 8).Select(i => new BattlePoint(i % 2 == 0 ? 2 : 0, i % 2 == 0 ? -1 : 0)).ToArray() : [],
        ExitTravel = mode == 5 ? new(98, -49) : mode == 15 ? new(128, -64) : new(0, 0),
        ExitSpeed = mode == 15 ? 18 : 15,
        ExitFrameMilliseconds = 32
    };

    internal static BattleLocalEffect[] Effects(int type, int mode, Func<int, int, int, int, bool, BattleSprite> sprite, Func<int, string> sound)
    {
        List<BattleLocalEffect> effects = [];
        BattleLocalEffect Effect(int image, int palette, int columns, int interval, BattlePoint[] origins, bool moving, bool fighting,
            int chance = 50, int trigger = -1, int life = 0, int fade = 0, BattlePoint? step = null) =>
            new(sprite(image, palette, columns, interval, image is 122 or 124 or 125 or 137) with
            { Opacity = image == 137 ? 153 / 256f : image is 122 or 124 or 125 or 135 or 136 ? 166 / 256f : 1 }, origins, chance, moving, fighting, trigger)
            { LifetimeFrames = life, FadePerFrame = fade, Step = step ?? new(0, 0) };
        if (type is >= 2 and <= 12)
        {
            BattlePoint[] dust = type is 7 or 8 ? [new(27, 28)] : [new(10, 24)];
            effects.Add(Effect(123, 80, 4, 100, dust, true, false, life: 22, fade: 12, step: new(0, -2)) with { Sprite = sprite(123, 80, 4, 100, false) with { Centered = false, Opacity = 255 / 256f } });
            if (type is >= 2 and <= 8)
            {
                var points = type is 7 or 8 ? new BattlePoint[] { new(49, 6), new(34, -18), new(17, 21), new(5, 5), new(19, -12), new(38, -14), new(31, 1) } :
                    [new(13, 20), new(9, -9), new(-2, 4), new(27, 13), new(34, -5)];
                int image = mode switch { 1 or 4 => 127, 2 or 5 => 128, _ => 126 };
                effects.Add(Effect(image, image - 43, 4, 100, points, false, true, 100, 2, 22, 12, new(0, -2)) with
                { RotatedCoverage = true, Styles = [new(128 / 256f, 6), new(255 / 256f, 12)] });
            }
        }
        if (type is >= 13 and <= 16)
        {
            int image = 122;
            BattlePoint[] left = type <= 14 ? [new(type == 13 ? -9 : -8, 3)] : type == 15 ? [new(-39, 62), new(42, 42)] : [new(-47, 89), new(94, 61)];
            BattlePoint[] right = type <= 14 ? left : type == 15 ? [new(42, 42), new(-8, 64)] : [new(50, 90), new(-8, 111)];
            effects.Add(Effect(image, image - 43, 4, 250, left, true, false, life: 5, fade: 36, step: new(-12, -2)));
            effects.Add(Effect(image, image - 43, 4, 250, right, true, false, life: 5, fade: 36, step: new(-4, 16)));
            if (type is 14 or 16)
            {
                var points = type == 14 ? mode == 14 ? new BattlePoint[] { new(17, 44), new(-13, 50), new(33, 24), new(6, 32) } : [new(-70, 2)] :
                    mode == 14 ? [new(-9, 108), new(-10, 128), new(118, 45), new(131, 81), new(119, 93), new(83, 110), new(27, 132), new(58, 126), new(100, 116), new(83, 133), new(124, 113)] : [new(0, 53), new(-43, 70), new(-8, 76)];
                int combatImage = mode == 14 ? 124 : 125;
                effects.Add(Effect(combatImage, combatImage - 43, 4, 62, points, false, true, 100, 2, 17, 10));
            }
        }
        if (type is >= 2 and <= 16 && mode is not (6 or 7 or 8 or 9 or 10 or 11 or 12 or 13))
        {
            var smoke = type switch { 13 => new BattlePoint(-13, -64), 14 => new(-17, -57), 15 => new(18, -42), 16 => new(86, -37), _ => new(5, -66) };
            var fire = type switch { 13 => new BattlePoint(53, 20), 14 => new(50, 28), 15 => new(89, 37), 16 => new(152, 46), _ => new(40, -11) };
            var spark = type switch { 13 => new BattlePoint(-14, -26), 14 => new(-17, -18), 15 => new(28, -5), 16 => new(36, 3), _ => new(5, -29) };
            int smokeImage = mode is 2 or 5 ? 136 : 135;
            effects.Add(Effect(smokeImage, smokeImage - 43, 8, 100, [smoke], false, true, type is 14 or 16 ? 50 : 0, 2, 21, 8, new(0, -2)) with { Jitter = 24, Arrival = true });
            effects.Add(Effect(137, 94, 8, 25, [fire], false, true, type is 14 or 16 ? 75 : 25, 2) with { Jitter = 32, Sounds = [sound(115), sound(116), sound(117)] });
            effects.Add(Effect(138, 95, 8, 125, [spark], false, true, type is 14 or 16 ? 75 : 25, 2) with { Jitter = 8, Arrival = true });
        }
        return effects.ToArray();
    }
}
