using San9.Importer.San9PkTc101.Images;
using System.Buffers.Binary;
using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

public static class InterfaceSkinCompiler
{
    public static InterfaceSkin Compile(byte[] exe, string dataDirectory, Func<string, byte[]> read, Func<string,string,PngImage,ImageAsset> save)
    {
        List<ImageAsset> assets = [];
        var archive = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_Main.s9")), 21, 98);
        Dictionary<int, int> bindings = [];
        foreach (var (start, count) in new[] { (0x24ea90, 1425), (0x2601d0, 1145) })
            for (int i = 0; i < count; i++)
            {
                int id = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(start + 8 * i)) & 65535;
                int palette = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(start + 8 * i + 4));
                bindings[id] = palette;
            }
        Dictionary<string, string> ui = [];
        void Export(string semantic, int index, int expectedPalette, int columns = 1, bool paletteAlpha = false)
        {
            if (bindings[5975 + index] != expectedPalette + 60) throw new InvalidDataException("Unconfirmed UI palette binding.");
            var source = archive.Images[index];
            for (int frame = 0; frame < columns; frame++)
            {
                string key = frame == 0 ? semantic : $"{semantic}-{frame}";
                string asset = $"ui-{key}";
                var pixels = columns == 1 ? source : IndexedImageArchive.Frame(source, columns, 1, frame, 0);
                assets.Add(save(asset, $"ui/{key}.png", IndexedImageArchive.Decode(pixels, archive.Palettes[expectedPalette], true, paletteAlpha)));
                ui.Add(key, asset);
            }
        }
        Export("top-frame", 88, 17); Export("radar-frame", 82, 16); Export("sidebar-frame", 84, 16); Export("roster-scroll-decoration", 85, 16);
        Export("button-scroll-up", 51, 6, 4); Export("button-scroll-down", 52, 6, 4);
        foreach (var (part, index) in new[] { ("top", 53), ("middle", 54), ("bottom", 55) })
            Export("scroll-track-" + part, index, 6);
        foreach (var (part, index) in new[] { ("top", 56), ("middle", 57), ("bottom", 58) })
            Export("scroll-thumb-" + part, index, 6);
        string[] seasons = ["spring", "summer", "autumn", "winter"];
        for (int i = 0; i < seasons.Length; i++) Export("season-" + seasons[i], 64 + i, 1);
        Export("button-territory", 68, 2, 6); Export("button-facilities", 69, 2, 4);
        Export("button-officers", 70, 2, 4); Export("button-units", 71, 2, 4);
        Export("button-information", 74, 3, 4); Export("button-functions", 73, 3, 4); Export("button-help", 75, 3, 4);
        Export("button-map-mode", 86, 4, 4); Export("button-advance", 87, 4, 4);
        Export("field-left", 91, 5); Export("field-middle", 92, 5); Export("field-right", 93, 5);
        Export("ruler-frame", 94, 5); Export("region-frame", 97, 20, paletteAlpha: true);
        var common = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_Common.s9")), 67, 491);
        var overview = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_MMap.s9")), 16, 221);
        void Overview(string role, int resource, int palette, int frames = 1, bool alpha = false)
        {
            if (bindings[resource] != palette + 295) throw new InvalidDataException("Unconfirmed overview palette binding.");
            for (int frame = 0; frame < frames; frame++)
            {
                string key = frame == 0 ? role : $"{role}-{frame}";
                var source = overview.Images[resource - 999];
                var pixels = frames == 1 ? source : IndexedImageArchive.Frame(source, frames, 1, frame, 0);
                string id = "ui-" + key;
                byte[] colors = overview.Palettes[palette];
                if (role == "overview-borders")
                {
                    colors = colors.ToArray();
                    colors[52 * 4 + 3] = 72;
                }
                assets.Add(save(id, $"ui/{key}.png", IndexedImageArchive.Decode(pixels, colors, true, alpha)));
                ui.Add(key, id);
            }
        }
        Overview("overview-background", 1156, 5);
        Overview("overview-terrain", 1163, 9);
        Overview("overview-borders", 1162, 8, alpha: true);
        Overview("overview-ranking", 1160, 7, alpha: true);
        Overview("button-overview-forces", 1158, 6, 4, true);
        Overview("button-overview-facilities", 1159, 6, 4, true);
        Overview("button-overview-units", 1157, 6, 4, true);
        for (int city = 0; city < 50; city++)
        {
            var source = overview.Images[162];
            byte[] rgba = new byte[source.Width * source.Height * 4];
            for (int pixel = 0; pixel < source.Pixels.Length; pixel++)
            {
                rgba[pixel * 4] = rgba[pixel * 4 + 1] = rgba[pixel * 4 + 2] = 255;
                rgba[pixel * 4 + 3] = source.Pixels[pixel] == city + 1 ? (byte)143 : (byte)0;
            }
            string key = $"overview-city-{city}";
            assets.Add(save("ui-" + key, $"ui/{key}.png", new(source.Width, source.Height, 4, rgba)));
            ui.Add(key, "ui-" + key);
        }
        for (int color = 0; color < 51; color++) Overview($"overview-marker-{color}", 1050 + color, 1);
        for (int color = 0; color < 51; color++) Overview($"overview-facility-{color}", 999 + color, 0);
        for (int color = 0; color < 50; color++) Overview($"overview-unit-{color}", 1170 + color, 15);
        var marker = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_Marker.s9")), 5, 34);
        void Extra(string role, int resource, int palette, bool paletteAlpha = false)
        {
            int start = resource >= 1220 ? 1220 : 0;
            int paletteBase = start == 0 ? 200 : 311;
            if (bindings[resource] != palette + paletteBase) throw new InvalidDataException("Unconfirmed interface resource binding.");
            var pack = start == 0 ? common : marker;
            string id = "ui-" + role;
            assets.Add(save(id, $"ui/{role}.png", IndexedImageArchive.Decode(pack.Images[resource - start], pack.Palettes[palette], true, paletteAlpha)));
            ui.Add(role, id);
        }
        foreach (var (role, resource) in new[] { ("left", 290), ("middle", 291), ("right", 292) })
        {
            if (bindings[resource] != 234) throw new InvalidDataException("Unconfirmed facility row palette.");
            for (int frame = 0; frame < 4; frame++)
            {
                string key = "roster-row-" + role + (frame == 0 ? "" : $"-{frame}");
                string id = "ui-" + key;
                var pixels = IndexedImageArchive.Frame(common.Images[resource], 4, 1, frame, 0);
                assets.Add(save(id, $"ui/{key}.png", IndexedImageArchive.Decode(pixels, common.Palettes[34], true, true)));
                ui.Add(key, id);
            }
        }
        Extra("city-prefix", 43, 5);
        Extra("message-left", 165, 15, true); Extra("message-middle", 166, 15, true); Extra("message-right", 167, 15, true);
        Extra("bar-morale-frame", 390, 51); Extra("bar-durability-frame", 391, 51);
        Extra("bar-morale-normal", 392, 51); Extra("bar-durability-normal", 393, 51);
        Extra("bar-morale-critical", 394, 52); Extra("bar-morale-warning", 395, 52);
        Extra("bar-durability-critical", 396, 52); Extra("bar-durability-warning", 397, 52);
        Extra("icon-emperor", 1243, 3); Extra("icon-training", 1244, 3); Extra("icon-merchant", 1245, 3);
        Dictionary<string, string> colors = [];
        for (int i = 0; i < 51; i++) colors.Add($"overview-force-{i}", Convert.ToHexString(overview.Palettes[8].AsSpan((i + 1) * 4, 3)));
        for (int i = 0; i < 52; i++) colors.Add($"city-marker-{i}", Convert.ToHexString(common.Palettes[49].AsSpan((i + 1) * 4, 3)));
        // P_Font contains interleaved 16-entry coverage ramps; use the opaque endpoint.
        var fontPalette = read(Path.Combine(dataDirectory, "P_Font.s9"));
        string FontColor(int style) => Convert.ToHexString(fontPalette.AsSpan((style / 2 * 32 + 16 + style % 2 * 8 + 7) * 4, 3));
        colors.Add("roster-available", FontColor(7)); colors.Add("roster-unavailable", FontColor(26));
        return new(7, assets.ToArray(), ui, colors);
    }
}
