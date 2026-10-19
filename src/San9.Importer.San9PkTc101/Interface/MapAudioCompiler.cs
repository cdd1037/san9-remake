using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class MapAudioCompiler
{
    internal static int[] ActionVariants(int first)
    {
        if (first < 0) return [];
        // RE-0187: cover each complete variant family instead of reproducing the source's off-by-one.
        int count = first switch
        {
            30 or 33 or 36 or 39 or 42 or 45 or 56 or 59 or 64 or 67 => 3,
            _ => 1
        };
        return Enumerable.Range(first, count).ToArray();
    }

    internal static readonly (string Role, string File)[] MusicSources = Enumerable.Range(0, 8)
        .Select(i => ($"map-music-{i}", $"BGM/BGM_{i + 4:00}"))
        .Concat(new (string Role, string File)[]
        {
            ("execution-battle", "BGM/BGM_12"), ("execution-battle-ranked", "BGM/BGM_13"),
            ("execution-march", "BGM/BGM_14"), ("execution-march-ranked", "BGM/BGM_15"),
            ("execution-domestic", "BGM/BGM_16")
        }).ToArray();

    internal static (MapAudioDefinition Definition, (string Role, string File)[] Sources) Compile(byte[] exe)
    {
        using var pe = new PEReader(new MemoryStream(exe));
        byte[] Bytes(int address, int count) => pe.GetSectionData(address - 0x400000).GetContent(0, count).ToArray();
        int Int(int address) => BinaryPrimitives.ReadInt32LittleEndian(Bytes(address, 4));
        string Path(int index)
        {
            var data = Bytes(Int(0x64E9A8 + index * 4), 64);
            return Encoding.ASCII.GetString(data, 0, Array.IndexOf(data, (byte)0)).Replace('\\', '/');
        }
        List<(string Role, string File)> sources = [];
        string Sound(string role, int index) { sources.Add((role, Path(index))); return role; }
        string[] Action(int body, int action)
        {
            int first = Int(0x615820 + 8 * (body + 20 * action));
            return ActionVariants(first).Select(i => Sound($"map-body-{body}-{action}-{i - first}", i)).ToArray();
        }
        // RE-0186: action table columns are movement, melee and ranged; source IDs stay here.
        var bodies = Enumerable.Range(0, 20).ToDictionary(i => $"unit-body-{i}",
            i => new MapBodySounds(Action(i, 0), Action(i, 1), Action(i, 2)));
        string construction = Sound("map-construction", 21), fortification = Sound("map-fortification-construction", 22);
        var definition = new MapAudioDefinition(Sound("execution-title", 11),
            MusicSources.Take(4).Select(s => s.Role).ToArray(), MusicSources.Skip(4).Take(4).Select(s => s.Role).ToArray(),
            [0, 1, 2], bodies, Enumerable.Range(0, 17).ToDictionary(i => i, i => i is >= 7 and <= 10 ? fortification : construction),
            Enumerable.Range(159, 3).Select(i => Sound($"map-battle-ambience-{i - 159}", i)).ToArray())
        {
            ExecutionMusic = new("execution-battle", "execution-battle-ranked", "execution-march", "execution-march-ranked", "execution-domestic"),
            RecordNotificationSound = Sound("map-record-notification", 2),
            FacilityArrowSounds = Enumerable.Range(64, 3).Select(i => Sound($"map-facility-arrow-{i - 64}", i)).ToArray(),
            RockfallImpactSounds = [Sound("map-rockfall-impact", 75), Sound("map-rockfall-dust", 71)]
        };
        return (definition, sources.ToArray());
    }
}
