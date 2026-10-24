using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

/// <summary>Converts the native placement families into authorable lines, not executable layout branches.</summary>
internal static class BattleFormationCompiler
{
    internal static BattleFormationLine[] Compile(int mode, int body, int facilityGroup, Func<int, int> read)
    {
        bool source = body < 17;
        if (facilityGroup == 2)
        {
            int address = source ? 0x665410 : 0x665428;
            List<BattleFormationLine> fort = [];
            for (int flank = 0; flank < 2; flank++)
                for (int row = 0; row < 2; row++)
                    fort.Add(new(read(address + flank * 4) + row * (source ? -16 : 16),
                        read(address + 12 + flank * 4) + row * (source ? 8 : -8), 12, 6, 3, flank * 2, 0));
            for (int row = 0; row < 3; row++)
                fort.Add(new(read(address + 8) + row * (source ? -28 : 28),
                    read(address + 20) + row * (source ? 14 : -14), 24, 12, 9, 1));
            return fort.ToArray();
        }

        BattleFormationLine[] Lines(int x, int y, int dx, int dy, int count, int rows, int rowX, int rowY) =>
            Enumerable.Range(0, rows).Select(row => new BattleFormationLine(x + row * rowX, y + row * rowY, dx, dy, count, row)).ToArray();
        BattleFormationLine[] Staggered(int x, int y, int dx, int dy, int offsetX, int offsetY, int first, int second) =>
            [new(x, y, dx, dy, first, 0), new(x + offsetX, y + offsetY, dx, dy, second, 1)];
        if (!source)
        {
            if (body == 19) return Staggered(480, 168, 48, 24, 104, -28, 7, 6);
            if (body == 20) return Staggered(480, 168, 72, 36, 116, -22, 5, 4);
            if (body == 21) return Lines(488, 136, 64, 32, 5, 1, 0, 0);
            if (body == 22) return Lines(488, 104, 112, 56, 3, 1, 0, 0);
            var (x, y) = facilityGroup switch { 0 => (464, 184), 1 => (480, 26), _ => (432, 200) };
            if (mode is 4 or 5) (x, y) = (384, 224);
            return Lines(x, y, 24, 12, 15, 3, 28, -14);
        }
        int offsetX = facilityGroup == 1 ? -120 : 0, offsetY = facilityGroup == 1 ? -90 : 0;
        return mode switch
        {
            0 or 2 => Lines(248 + offsetX, 292 + offsetY, 24, 12, 15, 2, -28, 14),
            1 or 9 => Lines(248 + offsetX, 292 + offsetY, 24, 12, 15, 3, -28, 14),
            3 or 5 => Lines(256, 280, 32, 16, 11, 2, -32, 16),
            4 => Lines(256, 280, 32, 16, 11, 3, -32, 16),
            6 => Staggered(192, 248, 32, 16, -7, 32, 15, 14),
            10 or 11 => Enumerable.Range(0, 3).SelectMany(group =>
                Lines(read(0x6653F8 + group * 4) + offsetX, read(0x665404 + group * 4) + offsetY,
                    24, 12, 6, 2, -28, 14).Select(line => line with { Group = group })).ToArray(),
            >= 12 when body == 13 => Staggered(152, 328, 48, 24, -56, 52, 7, 6),
            >= 12 when body == 14 => Staggered(152, 328, 72, 36, -44, 58, 5, 4),
            >= 12 when body == 15 => Lines(104, 328, 64, 32, 5, 1, 0, 0),
            >= 12 when body == 16 => Lines(88, 304, 112, 56, 3, 1, 0, 0),
            _ => []
        };
    }

    internal static BattleZoomAction Action(int mode) => mode switch
    {
        7 or 8 => new(BattleZoomActionKind.AssaultWave)
        {
            Rounds = mode == 7 ? 6 : 12, MovementFrameMilliseconds = mode == 7 ? 125 : 64,
            WaveEmission = new([[new(43,19),new(65,47)],[new(79,13),new(24,50)]],
                mode == 7 ? 2 : 3, 2, mode == 7 ? 30 : 45, mode == 7 ? 20 : 30)
        },
        11 => new(BattleZoomActionKind.ContinuousVolley)
        {
            Rounds = 24, IntervalMilliseconds = 128, RecoveryMilliseconds = 0,
            AlternateGroups = true, ProjectilesPerMember = 2, MovementFrameMilliseconds = 64
        },
        6 or 9 or 10 or 12 or 13 => new(BattleZoomActionKind.Volley)
        {
            AlternateGroups = mode == 10, ProjectilesPerMember = mode == 10 ? 3 : 1,
            ProjectileMultiplier = mode == 13 ? 3 : 1
        },
        _ => new(BattleZoomActionKind.Melee)
        {
            TravelX = mode >= 14 ? 272 : mode == 3 ? 146 : mode is 4 or 5 ? 78 : 168,
            TravelY = mode >= 14 ? -136 : mode == 3 ? -73 : mode is 4 or 5 ? -39 : -84,
            MinimumStep = mode switch { 0 => 8, 1 => 10, 2 => 12, 3 => 11, 4 => 13, 5 => 15, _ => 12 },
            MaximumStep = mode switch { 0 => 12, 1 => 12, 2 => 12, 3 => 15, 4 => 15, 5 => 15, _ => 18 },
            MovementFrameMilliseconds = mode is 2 or 5 ? 32 : mode is 1 or 4 or 15 ? 64 : 125,
            FrontFirst = mode is 0 or 2 or 3 or 5
        }
    };
}
