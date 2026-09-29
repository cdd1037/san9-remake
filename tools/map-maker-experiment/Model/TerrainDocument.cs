using System.Text.Json;
using System.Text.Json.Serialization;

namespace San9.TerrainEditor.Model;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public readonly record struct TerrainCell(int Type, int Height, int Variant);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TerrainDocument(string Format, int Version, int Width, int Height, TerrainCell[] Cells)
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectRequiredConstructorParameters = true
    };

    public static TerrainDocument Create(int width = 32, int height = 32) =>
        new("san9-terrain-editor", 1, width, height, Enumerable.Repeat(new TerrainCell(0, 1, 0), checked(width * height)).ToArray());

    public TerrainDocument Snapshot() => this with { Cells = (TerrainCell[])Cells.Clone() };

    public void Validate()
    {
        if (Format != "san9-terrain-editor" || Version != 1 || Width is < 4 or > 64 || Height is < 4 or > 64 ||
            Cells is null || Cells.Length != Width * Height) throw new InvalidDataException("不是有效的地貌工程（尺寸须为 4–64 格）。");
        foreach (TerrainCell cell in Cells)
            if (!TerrainRecipe.IsSupported(cell)) throw new InvalidDataException($"素材不支持地形 {cell.Type}、高度 {cell.Height}、变体 {cell.Variant}。");
    }

    public static TerrainDocument Load(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("工程文件超过 2 MiB。");
        TerrainDocument doc = JsonSerializer.Deserialize<TerrainDocument>(File.ReadAllText(path, System.Text.Encoding.UTF8), JsonOptions)
            ?? throw new InvalidDataException("工程为空。");
        doc.Validate(); return doc;
    }

    public void Save(string path)
    {
        Validate();
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions) + "\n", new System.Text.UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}

// 实验素材配方：对应现有 TC101 转换包，非通用地形语义或玩法类型。
public sealed record TerrainRecipe(int Base, int Variants, int MinHeight, int MaxHeight, int Atlas, bool Transitions)
{
    public static TerrainRecipe[] All { get; } =
    [
        new(10331,3,0,7,12,true), new(10006,3,0,7,14,true), new(10525,3,0,7,7,true),
        new(10198,3,0,2,9,true), new(10717,3,0,2,8,true), new(10255,3,0,2,11,true),
        new(10523,2,1,1,5,false), new(10774,3,1,1,6,false), new(10312,3,1,1,10,false),
        new(10315,4,1,1,4,false), new(10319,4,1,1,3,false), new(10315,1,1,1,4,false),
        new(10315,1,1,1,4,false), new(10006,3,0,7,15,true), new(10006,3,0,7,13,true),
        new(10323,4,1,1,2,false), new(10327,4,1,1,1,false)
    ];
    // 桥/岸的地表层也使用水面资源；桥对象不属于这个编辑器。
    public static int[] VariantsFor(int type) => type is >= 9 and <= 12 or 15 or 16 ? [0, 1, 2, 3, 4, 5] :
        Enumerable.Range(0, All[type].Variants).ToArray();
    public static bool IsSupported(TerrainCell cell) => cell.Type >= 0 && cell.Type < All.Length &&
        cell.Height >= All[cell.Type].MinHeight && cell.Height <= All[cell.Type].MaxHeight && VariantsFor(cell.Type).Contains(cell.Variant);
}

public sealed class EditHistory
{
    private readonly List<Dictionary<int, (TerrainCell Before, TerrainCell After)>> undo = [], redo = [];
    private Dictionary<int, (TerrainCell Before, TerrainCell After)>? stroke;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Begin() => stroke = [];
    public bool Paint(TerrainDocument doc, int fromX, int fromY, int toX, int toY, int radius, Func<TerrainCell, TerrainCell> change)
    {
        if (stroke is null) throw new InvalidOperationException("Begin a stroke first.");
        bool changed = false;
        int steps = Math.Max(1, Math.Max(Math.Abs(toX - fromX), Math.Abs(toY - fromY)));
        for (int step = 0; step <= steps; step++)
        {
            int cx = (int)Math.Round(fromX + (toX - fromX) * (double)step / steps);
            int cy = (int)Math.Round(fromY + (toY - fromY) * (double)step / steps);
            for (int y = Math.Max(0, cy - radius); y <= Math.Min(doc.Height - 1, cy + radius); y++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(doc.Width - 1, cx + radius); x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
                    int i = y * doc.Width + x; TerrainCell old = doc.Cells[i], next = change(old);
                    if (old == next || !TerrainRecipe.IsSupported(next)) continue;
                    stroke[i] = (stroke.TryGetValue(i, out var prior) ? prior.Before : old, next);
                    doc.Cells[i] = next; changed = true;
                }
        }
        return changed;
    }
    public void End()
    {
        if (stroke is { Count: > 0 }) { undo.Add(stroke); if (undo.Count > 50) undo.RemoveAt(0); redo.Clear(); }
        stroke = null;
    }
    public void Undo(TerrainDocument doc) => Travel(doc, undo, redo, false);
    public void Redo(TerrainDocument doc) => Travel(doc, redo, undo, true);
    private static void Travel(TerrainDocument doc, List<Dictionary<int, (TerrainCell Before, TerrainCell After)>> from,
        List<Dictionary<int, (TerrainCell Before, TerrainCell After)>> to, bool forward)
    {
        if (from.Count == 0) return;
        var edit = from[^1]; from.RemoveAt(from.Count - 1);
        foreach (var (index, value) in edit) doc.Cells[index] = forward ? value.After : value.Before;
        to.Add(edit);
    }
}
