using San9.Content.Images;
using San9.Content.Interface;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Interface;

public static partial class InterfaceSkinCompiler
{
    private static void CompileEditors(Dictionary<int, int> bindings, string directory, Func<string, byte[]> read,
        Func<string, string, TextureSourceImage, ImageAsset> save, List<ImageAsset> assets, Dictionary<string, string> roles)
    {
        var archive = IndexedImageArchive.ReadCombined(read(Path.Combine(directory, "G_EditPK.s9")), 16, 113);
        void Export(string role, int resource, int columns = 1)
        {
            int image = resource - 1254, palette = bindings[resource] - 267;
            if ((uint)image >= archive.Images.Length || (uint)palette >= archive.Palettes.Length)
                throw new InvalidDataException("Invalid editor resource binding.");
            for (int frame = 0; frame < columns; frame++)
            {
                string key = "editor-" + role + (frame == 0 ? "" : $"-{frame}"), id = "ui-" + key;
                var pixels = columns == 1 ? archive.Images[image] : IndexedImageArchive.Frame(archive.Images[image], columns, 1, frame, 0);
                assets.Add(save(id, TextureSourcePath.Image("G_EditPK", image, palette, true, true, columns: columns, frame: frame),
                    IndexedImageArchive.Decode(pixels, archive.Palettes[palette], true, true)));
                roles.Add(key, id);
            }
        }
        // RE-0199: loaded resource IDs belong to the G_EditPK bank, not G_Common.
        Export("background", 1296); Export("panel", 1360); Export("map-frame", 1361); Export("portrait", 1365); Export("radar", 1366); Export("names", 1311);
        int[] frameResources = [1295, 1294, 1282, 1283, 1286, 1287, 1288, 1289, 1290, 1291, 1292, 1285];
        for (int i = 0; i < frameResources.Length; i++) Export("frame-" + WorldEditorSkin.FrameParts[i], frameResources[i]);
        string[] panel = ["top-left", "top", "top-right", "left", "right", "bottom-left", "bottom", "bottom-right"];
        for (int i = 0; i < panel.Length; i++) Export("panel-" + panel[i], 1303 + i);
        // 0x66D880: mode-zero control families 49..59; close/cancel use 0x623468/0x62345C.
        int[] first = [1333, 1345, 1336, 1342, 1357, 1330, 1354, 1348, 1327, 1339, 1351, 1297, 1300];
        for (int i = 0; i < first.Length; i++)
        for (int p = 0; p < 3; p++) Export(WorldEditorSkin.ButtonStyles[i] + "-" + new[] { "left", "middle", "right" }[p], first[i] + p, 4);
        Export("close-icon", 1284, 4); Export("help", 1293, 4);
        Export("scroll-down", 1314, 4); Export("scroll-up", 1315, 4);
        foreach (var (part, resource) in new[] { ("track-top", 1317), ("track-middle", 1316), ("track-bottom", 1317), ("thumb-top", 1318), ("thumb-middle", 1319), ("thumb-bottom", 1320) }) Export("scroll-" + part, resource);
        // RE-0199: numeric readouts use 1325 twice; static captions use 1362..1364.
        Export("field-value-left", 1325); Export("field-value-middle", 1325); Export("field-value-right", 1326);
        Export("caption-left", 1362); Export("caption-middle", 1363); Export("caption-right", 1364);
        Export("label-left", 1321); Export("label-middle", 1322); Export("label-right", 1323);
    }
}
