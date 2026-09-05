using System.Numerics;
using San9.Content.Maps;

namespace San9.Importer.San9PkTc101;

// Independent implementation of the resource/ordering contract in BEH-RENDER-0002.
internal static class MapTerrainCompiler
{
    private static readonly int[] Bases = [10331, 10006, 10525, 10198, 10717, 10255, 10523, 10774, 10312, 10315, 10319, 10315, 10315, 10006, 10006, 10323, 10327];
    private static readonly int[] Variants = [3, 3, 3, 3, 3, 3, 2, 3, 3, 4, 4, 1, 1, 3, 3, 4, 4];
    private static readonly int[] Low = [0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1];
    private static readonly int[] High = [7, 7, 7, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 7, 7, 1, 1];
    private static readonly int[] Edge = [0, 5, 8, 0, 7, 2, 4, 7, 6, 1, 3, 6, 0, 5, 8, 0];
    private static readonly int[] Atlas = [12, 14, 7, 9, 8, 11, 5, 6, 10, 4, 3, 4, 4, 15, 13, 2, 1];
    private static bool Transitions(int t) => t is 0 or 1 or 2 or 3 or 4 or 5 or 13 or 14;
    public static int? Palette(int id) => id switch
    {
        >= 10006 and <= 10197 => 0,
        >= 10198 and <= 10254 => 1,
        >= 10255 and <= 10311 => 2,
        >= 10312 and <= 10314 => 3,
        >= 10315 and <= 10330 => 4 + (id - 10315) % 4,
        >= 10331 and <= 10522 => 8,
        >= 10523 and <= 10524 => 9,
        >= 10525 and <= 10716 => 10,
        >= 10717 and <= 10773 => 11,
        >= 10774 and <= 10776 => 12,
        _ => null
    };
    public static (int X, int Y) Center(int x, int y) => (32 * (x + 200 - y) + 96, 16 * (x + y + 1) + 112);
    public static TerrainStamp[] Compile(IReadOnlyList<San9ScenarioSquareRecord> squares, int[] positions, int[] heightAdjust)
    {
        SortedDictionary<long, LinkedList<TerrainStamp>> groups = [];
        long min = long.MaxValue, max = long.MinValue;
        void Add(int x, int y, int type, int flags)
        {
            var s = squares[y * 200 + x]; int t = s.TerrainCode, h = s.OffsetCode, e = s.ElevationTransitionMask, v = s.OpaqueField0B;
            if (t > 16 || h > 7 || e > 15 || v > 5) throw new InvalidDataException($"Unsupported terrain fields at {x},{y}.");
            int variant = v switch { 1 => 1, 2 => 2, 3 => 3, 4 => 2, 5 => 1, _ => 0 };
            int resource = Bases[t];
            if (h >= Low[t] && h <= High[t]) resource += (Transitions(t) && h != High[t] ? Edge[e] : 0) + (h == High[t] ? variant : 9 * variant) + Variants[t] * (Transitions(t) ? 9 : 1) * (h - Low[t]);
            if (Palette(resource) is null) throw new InvalidDataException($"Unconfirmed terrain resource {resource} at {x},{y}.");
            int edgeVariant = 0;
            if (Transitions(t)) edgeVariant = BitOperations.PopCount((uint)e) switch { 1 or 3 => 4, 2 => e is 5 or 9 ? 12 : 8, _ => 0 };
            int av = t is 9 or 10 or 15 or 16 ? v switch { 0 => 3, 3 => 0, 4 => 1, 5 => 2, _ => v } : v;
            int atlas = Atlas[t] * 16 + 272 * h + av + edgeVariant;
            long key = x + 200L * (y + atlas * 200L);
            int mask = e != 0 ? 10806 + 9 * e + type : (flags & 1) != 0 ? 10950 + type : (flags & 2) != 0 ? 10959 + type : 10806 + type;
            var center = Center(x, y); int px = positions[(e * 9 + type) * 2], py = positions[(e * 9 + type) * 2 + 1];
            int tx = center.X + px - 64, ty = center.Y + py - heightAdjust[e] - 10 * h - 54;
            var stamp = new TerrainStamp(resource, mask, tx, ty, tx - 96, ty - 112, type == 0);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
            if (min == long.MaxValue || key >= (min + max) / 2) list.AddLast(stamp); else list.AddFirst(stamp);
            min = Math.Min(min, key); max = Math.Max(max, key);
        }
        (int Bit, int Type, int X, int Y)[] directions = [(16, 3, 1, 0), (32, 2, -1, 0), (64, 4, 0, 1), (128, 1, 0, -1), (1, 5, -1, -1), (2, 6, 1, -1), (4, 8, 1, 1), (8, 7, -1, 1)];
        for (int y = 0; y < 200; y++) for (int x = 0; x < 200; x++)
        {
            var s = squares[y * 200 + x];
            foreach (var d in directions)
            {
                int nx = x + d.X, ny = y + d.Y;
                if ((s.TerrainTransitionMask & d.Bit) == 0 || nx < 0 || ny < 0 || nx >= 200 || ny >= 200) continue;
                var n = squares[ny * 200 + nx];
                int flag = s.TerrainCode is 9 or 10 or 11 or 12 or 15 or 16 ? 1 : n.OffsetCode == 1 && n.TerrainCode is 0 or 5 ? 2 : 0;
                Add(nx, ny, d.Type, flag);
            }
            Add(x, y, 0, 0);
        }
        return groups.Values.SelectMany(v => v).ToArray();
    }
}
