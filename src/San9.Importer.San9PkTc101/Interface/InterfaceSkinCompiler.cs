using San9.Importer.San9PkTc101.Images;
using System.Buffers.Binary;
using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

public static class InterfaceSkinCompiler
{
    public static InterfaceSkin Compile(byte[] exe, string dataDirectory, Func<string, byte[]> read, Func<string,string,PngImage,ImageAsset> save)
    {
        using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(exe));
        List<ImageAsset> assets = [];
        var archive = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_Main.s9")), 21, 98);
        Dictionary<int, int> bindings = [];
        foreach (var (start, count) in new[] { (0x24ea90, 1425), (0x2601d0, 1145), (0x25cfd0, 1070) })
            for (int i = 0; i < count; i++)
            {
                int id = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(start + 8 * i)) & 65535;
                int palette = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(start + 8 * i + 4));
                bindings[id] = palette;
            }
        Dictionary<string, string> ui = [];
        var ending=IndexedImageArchive.ReadArchive(read(Path.Combine(dataDirectory,"G_Ending.s9")));
        int endingPalette=BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x264698+47*8+4));
        if ((BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x264698+47*8))&65535)!=3047)
            throw new InvalidDataException("Unconfirmed ending opening image.");
        const string endingRole="decision-ending-opening";
        assets.Add(save("ui-"+endingRole,TextureSourcePath.Image("G_Ending",47,endingPalette,true,false),
            IndexedImageArchive.Decode(ending.Images[47],ending.Palettes[endingPalette],true,false)));
        ui.Add(endingRole,"ui-"+endingRole);
        foreach(var (role,index) in new[]{("expansion",0),("domestic",3),("army",22),("west",23),("trade",24),("trade-failure",25),
            ("empire",26),("building",27),("government",28),("building-failure",29),("defense",30),("culture",31),("culture-failure",32),
            ("military-failure",33),("empire-failure",34),("government-failure",36),("stable",48),("unstable",49)})
        {
            int palette=BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x264698+index*8+4));
            string key="decision-ending-"+role;
            assets.Add(save("ui-"+key,TextureSourcePath.Image("G_Ending",index,palette,true,false),
                IndexedImageArchive.Decode(ending.Images[index],ending.Palettes[palette],true,false)));
            ui.Add(key,"ui-"+key);
        }
        void Export(string semantic, int index, int expectedPalette, int columns = 1, bool paletteAlpha = false)
        {
            if (bindings[5975 + index] != expectedPalette + 60) throw new InvalidDataException("Unconfirmed UI palette binding.");
            var source = archive.Images[index];
            for (int frame = 0; frame < columns; frame++)
            {
                string key = frame == 0 ? semantic : $"{semantic}-{frame}";
                string asset = $"ui-{key}";
                var pixels = columns == 1 ? source : IndexedImageArchive.Frame(source, columns, 1, frame, 0);
                assets.Add(save(asset, TextureSourcePath.Image("G_Main", index, expectedPalette, true, paletteAlpha, columns: columns, frame: frame), IndexedImageArchive.Decode(pixels, archive.Palettes[expectedPalette], true, paletteAlpha)));
                ui.Add(key, asset);
            }
        }
        foreach (var (part, index) in new[] { ("top-left", 32), ("top", 33), ("top-right", 34), ("left", 35), ("center", 36), ("right", 37), ("bottom-left", 38), ("bottom", 39), ("bottom-right", 40) })
            Export("information-menu-" + part, index, 11, paletteAlpha: true);
        foreach (var (part, index) in new[] { ("left", 23), ("middle", 24), ("right", 25) })
            Export("information-menu-button-" + part, index, 10, 4, paletteAlpha: true);
        foreach (var (level, start) in new[] { ("primary", 0), ("secondary", 9) })
        foreach (var (tone, offset) in new[] { ("blue", 0), ("green", 3), ("brown", 6) })
        foreach (var (part, piece) in new[] { ("left", 0), ("middle", 1), ("right", 2) })
            Export($"command-{level}-{tone}-{part}", start + offset + piece, 0, 4, paletteAlpha: true);
        Export("command-top", 18, 7, paletteAlpha: true);
        Export("command-middle", 19, 7, paletteAlpha: true);
        Export("command-bottom", 20, 7, paletteAlpha: true);
        Export("command-illustration-frame", 21, 8, paletteAlpha: true);
        Export("command-condition", 22, 9, paletteAlpha: true);
        Export("minimap-command-pulse", 95, 18, 5, paletteAlpha: true);
        var still = IndexedImageArchive.ReadArchive(read(Path.Combine(dataDirectory, "G_Still.s9")));
        foreach (var entry in CommandMenuSources.Entries)
        {
            string role = "command-illustration-" + entry.Id, id = "ui-" + role;
            int index = entry.Illustration;
            assets.Add(save(id, TextureSourcePath.Image("G_Still", index, index, false, false),
                IndexedImageArchive.Decode(still.Images[index], still.Palettes[index], false, false)));
            ui.Add(role, id);
        }
        Export("top-frame", 88, 17, paletteAlpha: true); Export("radar-frame", 82, 16, paletteAlpha: true); Export("sidebar-frame", 84, 16, paletteAlpha: true); Export("roster-scroll-decoration", 85, 16, paletteAlpha: true);
        Export("button-scroll-up", 51, 6, 4); Export("button-scroll-down", 52, 6, 4);
        foreach (var (part, index) in new[] { ("top", 53), ("middle", 54), ("bottom", 55) })
            Export("scroll-track-" + part, index, 6, paletteAlpha: true);
        foreach (var (part, index) in new[] { ("top", 56), ("middle", 57), ("bottom", 58) })
            Export("scroll-thumb-" + part, index, 6, paletteAlpha: true);
        string[] seasons = ["spring", "summer", "autumn", "winter"];
        for (int i = 0; i < seasons.Length; i++) Export("season-" + seasons[i], 64 + i, 1);
        var seasonCovers = IndexedImageArchive.ReadImages(read(Path.Combine(dataDirectory, "G_MapCov.s9")));
        var coverPalettes = IndexedImageArchive.ReadPalettes(read(Path.Combine(dataDirectory, "P_MapEtc.s9")), 2);
        for (int i = 0; i < 4; i++)
        {
            int palette = bindings[10968 + i] - 112;
            if (palette != 1) throw new InvalidDataException("Unconfirmed season cover palette.");
            string role = $"season-cover-{i}";
            assets.Add(save("ui-" + role, TextureSourcePath.Image("G_MapCov", i, palette, false, false, "P_MapEtc.s9"),
                IndexedImageArchive.Decode(seasonCovers[i], coverPalettes[palette], false, false)));
            ui.Add(role, "ui-" + role);
        }
        Export("button-territory", 68, 2, 6); Export("button-facilities", 69, 2, 4);
        Export("button-officers", 70, 2, 4); Export("button-units", 71, 2, 4);
        Export("button-information", 74, 3, 4); Export("button-functions", 73, 3, 4); Export("button-help", 75, 3, 4);
        Export("button-map-mode", 86, 4, 4); Export("button-advance", 87, 4, 4);
        Export("button-pause", 72, 2, 4);
        Export("turn-progress-frame", 89, 5, paletteAlpha: true);
        Export("turn-progress-fill", 90, 5, paletteAlpha: true);
        Export("turn-record-panel", 59, 12, paletteAlpha: true);
        Export("minimap-record-pulse", 96, 19, 5, paletteAlpha: true);
        Export("turn-command-panel", 61, 13, paletteAlpha: true);
        // RE-0183: native three-part frames retain their transparent margins and tails.
        var speech = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_Effect.s9")), 20, 52);
        foreach (var placement in Enum.GetValues<MapSpeechPlacement>())
        foreach (var (part, offset) in new[] { ("left", 0), ("middle", 1), ("right", 2) })
        {
            int index = 39 + 3 * (int)placement + offset;
            if (bindings[5611 + index] != 39) throw new InvalidDataException("Unconfirmed map speech palette binding.");
            string role = MapSpeechSkin.Role(placement, part), id = "ui-" + role;
            assets.Add(save(id, TextureSourcePath.Image("G_Effect", index, 18, true, false),
                IndexedImageArchive.Decode(speech.Images[index], speech.Palettes[18], true, false)));
            ui.Add(role, id);
        }
        // This palette supplies RGB colors; its reserved fourth bytes are all zero.
        // The original copies the indexed pixels and palette into an opaque bar surface.
        Export("turn-command-remaining-fill", 63, 14);
        Export("field-left", 91, 5); Export("field-middle", 92, 5); Export("field-right", 93, 5);
        Export("ruler-frame", 94, 5); Export("region-frame", 97, 20, paletteAlpha: true);
        Export("target-title-left", 79, 15, paletteAlpha: true);
        Export("target-title-middle", 80, 15, paletteAlpha: true);
        Export("target-title-right", 81, 15, paletteAlpha: true);
        Export("target-footer", 78, 15, paletteAlpha: true);
        foreach (var (role, index) in new[] { ("cancel", 76), ("list", 77) })
        {
            Export("target-" + role, index, 15, 4, paletteAlpha: true);
            ui.Add($"target-{role}-0", ui["target-" + role]);
            ui.Remove("target-" + role);
        }
        var common = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_Common.s9")), 67, 491);
        if(bindings[390]!=251) throw new InvalidDataException("Unconfirmed remaining-command frame palette.");
        assets.Add(save("ui-turn-command-remaining-frame",TextureSourcePath.Image("G_Common",390,51,true,true),
            IndexedImageArchive.Decode(common.Images[390],common.Palettes[51],true,true)));
        ui.Add("turn-command-remaining-frame","ui-turn-command-remaining-frame");
        // RE-0181: vertical choices use the distinct three-part configuration 3, four state frames.
        foreach (var (part, index) in new[] { ("left",386), ("middle",387), ("right",388) })
        for (int frame = 0; frame < 4; frame++)
        {
            if (bindings[index] != 250) throw new InvalidDataException("Unconfirmed vertical choice palette.");
            string role = $"decision-choice-{part}" + (frame == 0 ? "" : $"-{frame}"), id = "ui-" + role;
            assets.Add(save(id, TextureSourcePath.Image("G_Common",index,50,true,true,columns:4,frame:frame),
                IndexedImageArchive.Decode(IndexedImageArchive.Frame(common.Images[index],4,1,frame,0),common.Palettes[50],true,true)));
            ui.Add(role,id);
        }
        foreach (var (role,index) in new[] { ("tongque",45),("commemoration",46),("training-land",47),("training-water",48),
            ("bandits-land",58),("bandits-water",59),("beast",60),("festival",61),("dippers",62),("unification",63),("alliance",66) })
        {
            string key = "decision-" + role, id = "ui-" + key;
            assets.Add(save(id,TextureSourcePath.Image("G_Still",index,index,false,false),
                IndexedImageArchive.Decode(still.Images[index],still.Palettes[index],false,false)));
            ui.Add(key,id);
        }
        string[] tradeCategories=["salt","textiles","distribution","trade","livestock","iron","farmland","orchard"];
        for(int category=0;category<tradeCategories.Length;category++)
        {
            int index=49+category; string key="decision-trade-"+tradeCategories[category],id="ui-"+key;
            assets.Add(save(id,TextureSourcePath.Image("G_Still",index,index,false,false),
                IndexedImageArchive.Decode(still.Images[index],still.Palettes[index],false,false)));
            ui.Add(key,id);
        }
        var transition = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_LocalM.s9")), 2, 4);
        // RE-0158: three 125 ms additive frames, then the palette-alpha title.
        foreach (var (phase, first) in new[] { ("strategy", 0), ("execution", 1) })
        for (int frame = 0; frame < 4; frame++)
        {
            int index = first + (frame == 3 ? 2 : 0), palette = frame == 3 ? 1 : 0;
            if (bindings[6073 + index] != 81 + palette) throw new InvalidDataException("Unconfirmed transition palette binding.");
            var pixels = frame == 3 ? transition.Images[index] : IndexedImageArchive.Frame(transition.Images[index], 3, 1, frame, 0);
            string key = $"turn-title-{phase}-{frame}", id = "ui-" + key;
            assets.Add(save(id, TextureSourcePath.Image("G_LocalM", index, palette, true, frame == 3, columns: frame == 3 ? 1 : 3, frame: frame == 3 ? 0 : frame),
                IndexedImageArchive.Decode(pixels, transition.Palettes[palette], true, frame == 3)));
            ui.Add(key, id);
        }
        if (Enumerable.Range(172, 9).Any(index => bindings[index] != 217))
            throw new InvalidDataException("Unconfirmed confirmation frame palette.");
        var dialogFrame = NineSliceArt.Assemble(common.Images.Skip(172).Take(9).ToArray());
        assets.Add(save("ui-dialog-frame", "ui/dialog-frame.png", IndexedImageArchive.Decode(dialogFrame, common.Palettes[17], true, true)));
        ui.Add("dialog-frame", "ui-dialog-frame");
        foreach (var (kind, index) in new[] { ("yes", 389), ("no", 385) })
        for (int frame = 0; frame < 4; frame++)
        {
            if (bindings[index] != 250) throw new InvalidDataException("Unconfirmed confirmation button palette.");
            string key = $"dialog-{kind}-{frame}", id = "ui-" + key;
            assets.Add(save(id, TextureSourcePath.Image("G_Common", index, 50, true, true, columns: 4, frame: frame),
                IndexedImageArchive.Decode(IndexedImageArchive.Frame(common.Images[index], 4, 1, frame, 0), common.Palettes[50], true, true)));
            ui.Add(key, id);
        }
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
                assets.Add(save(id, TextureSourcePath.Image("G_MMap", resource - 999, palette, true, alpha, columns: frames, frame: frame), IndexedImageArchive.Decode(pixels, colors, true, alpha)));
                ui.Add(key, id);
            }
        }
        Overview("overview-background", 1156, 5);
        Overview("overview-terrain", 1163, 9);
        Overview("overview-ranking", 1160, 7, alpha: true);
        for (int color = 0; color < 51; color++) Overview($"information-map-force-{color}", 1103 + color, 4, 4, alpha: true);
        Overview("information-map-city", 1154, 4, 4, alpha: true);
        Overview("information-map-selected", 1155, 4, alpha: true);
        Overview("button-overview-forces", 1158, 6, 4, true);
        Overview("button-overview-facilities", 1159, 6, 4, true);
        Overview("button-overview-units", 1157, 6, 4, true);
        for (int color = 0; color < 51; color++) Overview($"overview-marker-{color}", 1050 + color, 1);
        for (int color = 0; color < 51; color++) Overview($"overview-facility-{color}", 999 + color, 0);
        for (int color = 0; color < 50; color++) Overview($"overview-unit-{color}", 1170 + color, 15);
        var waypoint = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_MpMkPK.s9")), 1, 2);
        for (int variant = 0; variant < 2; variant++)
        {
            if (bindings[11068 + variant] != 114 ||
                waypoint.Images[variant].Width != 160 || waypoint.Images[variant].Height != 68)
                throw new InvalidDataException("Unconfirmed waypoint flag resource.");
            for (int frame = 0; frame < 4; frame++)
            {
                string role = $"target-waypoint-{(variant == 0 ? "spring-summer" : "autumn-winter")}-{frame}";
                var source = IndexedImageArchive.Decode(IndexedImageArchive.Frame(waypoint.Images[variant], 4, 1, frame, 0), waypoint.Palettes[0], true, false);
                // Normalize both source pole anchors (38,67)/(2,67) to the bottom center.
                byte[] pixels = new byte[76 * 68 * 4];
                for (int row = 0; row < 68; row++)
                    source.Pixels.AsSpan(row * 40 * 4, 40 * 4).CopyTo(pixels.AsSpan((row * 76 + variant * 36) * 4));
                assets.Add(save("ui-" + role, $"ui/{role}.png", new(76, 68, 4, pixels)));
                ui.Add(role, "ui-" + role);
            }
        }
        var marker = IndexedImageArchive.ReadCombined(read(Path.Combine(dataDirectory, "G_Marker.s9")), 5, 34);
        if (bindings[1251] != 315) throw new InvalidDataException("Unconfirmed route marker binding.");
        for (int frame = 0; frame < 8; frame++)
        {
            string role = $"target-route-{frame}", id = "ui-" + role;
            assets.Add(save(id, TextureSourcePath.Image("G_Marker", 31, 4, true, false, columns: 8, frame: frame),
                IndexedImageArchive.Decode(IndexedImageArchive.Frame(marker.Images[31], 8, 1, frame, 0), marker.Palettes[4], true, false)));
            ui.Add(role, id);
        }
        foreach (var (kind, index, palette) in new[] { ("attack", 18, 2), ("enter", 32, 4), ("hostile", 0, 0), ("friendly", 1, 0) })
        for (int frame = 0; frame < 4; frame++)
        {
            if (bindings[1220 + index] != 311 + palette) throw new InvalidDataException("Unconfirmed target marker binding.");
            string role = $"target-marker-{kind}-{frame}", id = "ui-" + role;
            assets.Add(save(id, TextureSourcePath.Image("G_Marker", index, palette, true, false, columns: 4, frame: frame),
                IndexedImageArchive.Decode(IndexedImageArchive.Frame(marker.Images[index], 4, 1, frame, 0), marker.Palettes[palette], true, false)));
            ui.Add(role, id);
        }
        void Extra(string role, int resource, int palette, bool paletteAlpha = false)
        {
            int start = resource >= 1220 ? 1220 : 0;
            int paletteBase = start == 0 ? 200 : 311;
            if (bindings[resource] != palette + paletteBase) throw new InvalidDataException("Unconfirmed interface resource binding.");
            var pack = start == 0 ? common : marker;
            string id = "ui-" + role;
            assets.Add(save(id, TextureSourcePath.Image(start == 0 ? "G_Common" : "G_Marker", resource - start, palette, true, paletteAlpha), IndexedImageArchive.Decode(pack.Images[resource - start], pack.Palettes[palette], true, paletteAlpha)));
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
                assets.Add(save(id, TextureSourcePath.Image("G_Common", resource, 34, true, true, columns: 4, frame: frame), IndexedImageArchive.Decode(pixels, common.Palettes[34], true, true)));
                ui.Add(key, id);
            }
        }
        foreach (var (role, resource, columns, palette) in new[]
        {
            ("catalog-frame-top-left",314,1,39), ("catalog-frame-top",315,1,39), ("catalog-frame-top-right",316,1,39),
            ("catalog-frame-bottom-left",320,1,39), ("catalog-frame-bottom",321,1,39), ("catalog-frame-bottom-right",322,1,39),
            ("catalog-header-left",332,4,44), ("catalog-header-middle",333,4,44), ("catalog-header-right",334,4,44),
            ("catalog-sort-down",335,1,44), ("catalog-sort-up",336,1,44),
            ("catalog-row-left",293,4,35), ("catalog-row-middle",294,4,35), ("catalog-row-right",295,4,35),
            ("catalog-checkbox",289,4,33),
            ("catalog-accept-left",355,4,42), ("catalog-accept-middle",356,4,42), ("catalog-accept-right",357,4,42),
            ("catalog-bulk-left",347,4,42), ("catalog-bulk-middle",348,4,42), ("catalog-bulk-right",349,4,42),
            ("catalog-tab",380,6,45), ("catalog-portrait-frame",297,1,37), ("catalog-help",354,4,42),
            // The page arrows include a fifth, disabled-hover frame (RE-0112, 4FBAF0).
            ("deployment-page-previous",181,5,bindings[181]-200), ("deployment-page-next",182,5,bindings[182]-200),
            ("catalog-scroll-down",372,4,48), ("catalog-scroll-up",373,4,48),
            ("catalog-scroll-track-top",374,1,48), ("catalog-scroll-track-middle",375,1,48), ("catalog-scroll-track-bottom",376,1,48),
            ("catalog-scroll-thumb-top",377,1,48), ("catalog-scroll-thumb-middle",378,1,48), ("catalog-scroll-thumb-bottom",379,1,48),
            ("catalog-tabs-top-left",364,1,43), ("catalog-tabs-top",365,1,43), ("catalog-tabs-top-right",366,1,43),
            ("catalog-tabs-left",367,1,43), ("catalog-tabs-right",368,1,43),
            ("catalog-tabs-bottom-left",369,1,43), ("catalog-tabs-bottom",370,1,43), ("catalog-tabs-bottom-right",371,1,43)
        })
        {
            if (bindings[resource] != palette + 200) throw new InvalidDataException("Unconfirmed catalog palette binding.");
            for (int frame = 0; frame < columns; frame++)
            {
                string key = role + (frame == 0 ? "" : $"-{frame}");
                var pixels = IndexedImageArchive.Frame(common.Images[resource], columns, 1, frame, 0);
                assets.Add(save("ui-" + key, TextureSourcePath.Image("G_Common", resource, palette, true, true, columns: columns, frame: frame), IndexedImageArchive.Decode(pixels, common.Palettes[palette], true, true)));
                ui.Add(key, "ui-" + key);
            }
        }
        foreach (var (role, resource, palette) in new[]
        {
            ("unit-list-top",55,6), ("unit-list-bottom",56,6),
            ("treasure-list-top",57,6), ("treasure-list-bottom",58,6), ("treasure-description",98,8),
            ("leader-label-left",72,7), ("leader-label-middle",73,7), ("leader-label-right",74,7),
            ("admin-label-left",75,7), ("admin-label-middle",76,7), ("admin-label-right",77,7),
            ("leader-radar-grid",31,4),
            ("status-badge",114,11), ("training-badge",115,11),
            ("background",51,6), ("list-top",53,6), ("list-bottom",54,6),
            ("panel-center",59,7), ("panel-top-left",60,7), ("panel-top",61,7), ("panel-top-right",62,7),
            ("panel-left",63,7), ("panel-right",64,7), ("panel-bottom-left",65,7), ("panel-bottom",66,7), ("panel-bottom-right",67,7),
            ("label-left",69,7), ("label-middle",70,7), ("label-right",71,7),
            ("title-left",84,7), ("title-middle",85,7), ("title-right",86,7),
            ("biography-field",99,8), ("tactics-field",93,8), ("portrait-frame",88,7), ("radar-background",89,7), ("radar-grid",94,8),
            ("name-left",90,8), ("name-middle",91,8), ("name-right",92,8),
            ("field-left",95,8), ("field-middle",96,8), ("field-right",97,8),
            ("tactic-category-left",101,9), ("tactic-category-middle",102,9), ("tactic-category-right",103,9), ("button-shadow",309,38)
        }) Extra("detail-" + role, resource, palette, true);
        foreach (var (role, resource, palette, frames) in new[]
        {
            ("leader-background",30,3,2), ("tab",303,38,4), ("treasure",100,9,4), ("close",308,38,4), ("close-icon",305,38,4), ("help",306,38,4),
            ("tactic-0",104,9,2), ("tactic-1",105,9,2), ("tactic-2",106,9,2), ("tactic-3",112,9,2),
            ("tactic-4",107,9,2), ("tactic-5",108,9,2), ("tactic-6",109,9,2), ("tactic-7",110,9,2), ("tactic-8",111,9,2)
        })
        {
            if (bindings[resource] != palette + 200) throw new InvalidDataException("Unconfirmed detail palette binding.");
            for (int frame = 0; frame < frames; frame++)
            {
                string key = "detail-" + role + (frame == 0 ? "" : $"-{frame}");
                var pixels = IndexedImageArchive.Frame(common.Images[resource], frames, 1, frame, 0);
                assets.Add(save("ui-" + key, TextureSourcePath.Image("G_Common", resource, palette, true, true, columns: frames, frame: frame), IndexedImageArchive.Decode(pixels, common.Palettes[palette], true, true)));
                ui.Add(key, "ui-" + key);
            }
        }
        foreach (var (theme, first, palette) in new[] { ("own", 398, 53), ("other", 407, 54), ("friendly", 416, 55) })
        {
            if (Enumerable.Range(first, 9).Any(id => bindings[id] != palette + 200))
                throw new InvalidDataException("Unconfirmed map hover frame palette.");
            foreach (var (kind, height) in new[] { ("city", 316), ("facility", 248), ("unit", 240) })
            {
                string role = $"hover-{theme}-{kind}";
                var indexed = MapHoverArt.Compose(common.Images.Skip(first).Take(9).ToArray(), 160, height);
                assets.Add(save("ui-" + role, $"ui/{role}.png", IndexedImageArchive.Decode(indexed, common.Palettes[palette], true, true)));
                ui.Add(role, "ui-" + role);
            }
        }
        foreach (var (kind, resource) in new[] { ("unit", 488), ("facility", 489), ("city", 490) })
            Extra("command-summary-" + kind, resource, bindings[resource] - 200, true);
        Extra("command-summary-merchant", 1239, bindings[1239] - 311, true);
        Extra("command-summary-training", 1240, bindings[1240] - 311, true);
        Extra("hover-divider", 425, 55, true);
        Extra("information-background", 337, 40);
        Extra("information-summary", 381, 46);
        foreach (var (part, resource) in new[] { ("bottom", 338), ("left", 339), ("right", 340), ("top", 341), ("bottom-left", 342), ("top-left", 343), ("bottom-right", 344), ("top-right", 345), ("ornament", 346) })
            Extra("information-frame-" + part, resource, 41, true);
        foreach (var (part, resource) in new[] { ("left", 361), ("middle", 362), ("right", 363) }) Extra("information-title-" + part, resource, 42, true);
        foreach (var (part, resource) in new[] { ("left", 382), ("middle", 383), ("right", 384) }) Extra("information-footer-" + part, resource, 47, true);
        foreach (var (role, resource) in new[] { ("left", 350), ("middle", 351), ("right", 352), ("close", 353), ("map-left", 358), ("map-middle", 359), ("map-right", 360) })
        {
            if (bindings[resource] != 242) throw new InvalidDataException("Unconfirmed information button palette.");
            for (int frame = 0; frame < 4; frame++)
            {
                string key = "information-action-" + role + (frame == 0 ? "" : $"-{frame}");
                string id = "ui-" + key;
                var pixels = IndexedImageArchive.Frame(common.Images[resource], 4, 1, frame, 0);
                // Composite three-part buttons use palette alpha; the standalone close icon uses color key.
                bool alpha = role != "close";
                assets.Add(save(id, TextureSourcePath.Image("G_Common", resource, 42, true, alpha, columns: 4, frame: frame), IndexedImageArchive.Decode(pixels, common.Palettes[42], true, alpha)));
                ui.Add(key, id);
            }
        }
        // RE-0159: 48 entries per size row; no 12px binding and no 24px city glyph.
        foreach (var (code, name) in Text.OriginalMessageParser.Icons)
        foreach (int size in new[] { 16, 24 })
        {
            int index = code - 0xB0;
            int resource = BinaryPrimitives.ReadInt32LittleEndian(pe.GetSectionData(0x253318).GetContent(((size == 24 ? 0 : 1) * 48 + index) * 4, 4).AsSpan());
            if (resource < 0) continue;
            int palette = bindings[resource] - 200;
            Extra($"text-icon-{name}-{size}", resource, palette);
        }
        // RE-0095: CBushouMessageDlg artwork and the two narration continuation animations.
        // RE-0181: all image-frame parts bind to the same palette, retained after composition.
        foreach (var (part, resource) in new[] { ("top-left",426), ("top",427), ("top-right",428),
            ("left",429), ("right",430), ("bottom-left",431), ("bottom",432), ("bottom-right",433) })
        {
            if (bindings[resource] != 257)
                throw new InvalidDataException("Unconfirmed illustration frame binding.");
            Extra("narration-image-frame-"+part,resource,57,true);
        }
        foreach (var (role, resource) in new (string, int)[] {
            ("portrait-frame", 168), ("middle", 169), ("right", 170), ("affiliation", 171) })
            Extra("narration-" + role, resource, bindings[resource] - 200, true);
        for (int frame = 0; frame < 6; frame++) Extra($"narration-portrait-next-{frame}", 447 + frame, bindings[447 + frame] - 200, true);
        for (int frame = 0; frame < 7; frame++) Extra($"narration-result-next-{frame}", 453 + frame, bindings[453 + frame] - 200, true);
        Extra("message-left", 165, 15, true); Extra("message-middle", 166, 15, true); Extra("message-right", 167, 15, true);
        Extra("bar-morale-frame", 390, 51); Extra("bar-durability-frame", 391, 51);
        Extra("bar-morale-normal", 392, 51); Extra("bar-durability-normal", 393, 51);
        Extra("bar-morale-critical", 394, 52); Extra("bar-morale-warning", 395, 52);
        Extra("bar-durability-critical", 396, 52); Extra("bar-durability-warning", 397, 52);
        Extra("icon-construction", 1241, 3); Extra("icon-transport", 1242, 3);
        Extra("icon-emperor", 1243, 3); Extra("icon-training", 1244, 3); Extra("icon-merchant", 1245, 3);
        Dictionary<string, string> colors = [];
        for (int i = 0; i < 51; i++) colors.Add($"overview-force-{i}", Convert.ToHexString(overview.Palettes[8].AsSpan((i + 1) * 4, 3)));
        for (int i = 0; i < 52; i++) colors.Add($"city-marker-{i}", Convert.ToHexString(common.Palettes[49].AsSpan((i + 1) * 4, 3)));
        // P_Font contains interleaved 16-entry coverage ramps; use the opaque endpoint.
        var fontPalette = read(Path.Combine(dataDirectory, "P_Font.s9"));
        string FontColor(int style) => Convert.ToHexString(fontPalette.AsSpan((style / 2 * 32 + 16 + style % 2 * 8 + 7) * 4, 3));
        colors.Add("roster-available", FontColor(7)); colors.Add("roster-unavailable", FontColor(26));
        colors.Add("map-record-normal", FontColor(7)); colors.Add("map-record-hover", FontColor(6));
        colors.Add("map-record-pressed", FontColor(5)); colors.Add("map-speech-text", FontColor(0));
        colors.Add("map-speech-name-shadow", FontColor(1));
        var cursors = ExecutableCursorCompiler.Compile(pe, assets, save);
        foreach (var (role, resource) in new (string, int)[] {
            ("top-edge",234), ("left-edge",183), ("right-edge",184),
            ("choice-normal-left",190), ("choice-normal-middle",191), ("choice-normal-right",192),
            ("choice-hover-left",196), ("choice-hover-middle",197), ("choice-hover-right",198),
            ("choice-selected-left",193), ("choice-selected-middle",194), ("choice-selected-right",195),
            ("background",270), ("top-left",201), ("top-right",202), ("bottom-left",203), ("bottom-right",204), ("edge",226),
            ("title-left",231), ("title-middle",232), ("title-right",233),
            ("footer-left",223), ("footer-middle",224), ("footer-right",225),
            ("label-left",256), ("label-middle",257), ("label-right",258),
            ("facility-left",239), ("facility-middle",240), ("facility-right",241),
            ("mode-left",276), ("mode-middle",277), ("mode-right",278), ("field-rim",189),
            ("number-background",3), ("number-field-left",0), ("number-field-middle",1), ("number-field-right",2) })
            Extra("deployment-" + role, resource, bindings[resource] - 200, !role.StartsWith("mode-", StringComparison.Ordinal));
        foreach (var (role, resource) in new (string, int)[] {
            ("top-left",205), ("top-right",206), ("bottom-left",207), ("bottom-right",208),
            ("edge",230), ("top-edge",238), ("title-left",235), ("title-middle",236), ("title-right",237),
            ("footer-left",227), ("footer-middle",228), ("footer-right",229) })
            Extra("deployment-small-" + role, resource, bindings[resource] - 200, true);
        foreach (var (part, resource) in new[] { ("left",186), ("middle",187), ("right",188) })
            Extra("deployment-arrival-rim-" + part, resource, bindings[resource] - 200);
        // Nonzero source mask pixels remove the command background at decorated corners.
        foreach (var (role, resource) in new (string, int)[] { ("top-left", 209), ("top-right", 210), ("bottom-left", 211),
            ("small-top-left", 213), ("small-top-right", 214), ("small-bottom-left", 215) })
        {
            var mask = common.Images[resource];
            byte[] pixels = new byte[mask.Width * mask.Height * 4];
            for (int i = 0; i < mask.Pixels.Length; i++)
                if (mask.Pixels[i] != 0) pixels.AsSpan(i * 4, 4).Fill(255);
            string key = role.StartsWith("small-", StringComparison.Ordinal) ? "deployment-small-cutout-" + role[6..] : "deployment-cutout-" + role;
            assets.Add(save("ui-" + key, "ui/" + key + ".png", new(mask.Width, mask.Height, 4, pixels)));
            ui.Add(key, "ui-" + key);
        }
        var deploymentField = CompileDeploymentField(common.Images.Skip(242).Take(9).ToArray());
        // RE-0095: header caps discard eight outside pixels; column rules discard
        // three pixels on each side and eight at the outside vertical ends.
        foreach (var (role, resource, left, top, right, bottom) in new (string, int, int, int, int, int)[] {
            ("roster-header-left",273,8,0,0,0), ("roster-header-middle",274,0,0,0,0), ("roster-header-right",275,0,0,8,0),
            ("column-top",279,3,8,3,0), ("column-middle",280,3,0,3,0), ("column-bottom",281,3,0,3,8),
            ("divider",185,0,0,0,0) })
        {
            var source = common.Images[resource];
            int width = source.Width - left - right, height = source.Height - top - bottom;
            byte[] pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
                source.Pixels.AsSpan((y + top) * source.Width + left, width).CopyTo(pixels.AsSpan(y * width, width));
            string key = "deployment-" + role;
            assets.Add(save("ui-" + key, "ui/" + key + ".png",
                IndexedImageArchive.Decode(new(width, height, pixels), common.Palettes[bindings[resource] - 200], true, false)));
            ui.Add(key, "ui-" + key);
        }
        foreach (var (role, resource) in new (string, int)[] {
            ("title-left", 5), ("title-middle", 6), ("title-right", 7), ("slider-fill", 23), ("slider-thumb", 4) })
            Extra("deployment-number-" + role, resource, bindings[resource] - 200);
        Extra("deployment-policy-background",460,bindings[460]-200);
        foreach (var (role, resource) in new (string, int)[] {
            ("frame-top-left",461), ("frame-top",462), ("frame-top-right",463),
            ("frame-left",464), ("frame-right",465), ("frame-bottom-left",466),
            ("frame-bottom",467), ("frame-bottom-right",468),
            ("title-left",472), ("title-middle",473), ("title-right",474) })
            Extra("deployment-policy-"+role,resource,bindings[resource]-200,true);
        int fieldPalette = bindings[242] - 200;
        if (Enumerable.Range(242, 9).Any(index => bindings[index] != bindings[242]))
            throw new InvalidDataException("Inconsistent deployment field palette.");
        assets.Add(save("ui-deployment-field", "ui/deployment-field.png",
            IndexedImageArchive.Decode(deploymentField, common.Palettes[fieldPalette], true, true)));
        ui.Add("deployment-field", "ui-deployment-field");
        foreach (var (role, resource) in new (string, int)[] {
            ("edit-left",253), ("edit-middle",254), ("edit-right",255),
            ("accept-left",220), ("accept-middle",221), ("accept-right",222),
            ("cancel-left",217), ("cancel-middle",218), ("cancel-right",219),
            ("close",251), ("help",252), ("policy-choice-left",469), ("policy-choice-middle",470), ("policy-choice-right",471), ("number-accept",27), ("number-cancel",24),
            ("number-back",20), ("number-clear",19), ("number-min",29), ("number-max",28), ("number-minus",21), ("number-plus",22),
            ("number-close",25), ("number-help",26), ("number-00",8), ("number-0",9),
            ("number-1",10), ("number-2",11), ("number-3",12), ("number-4",13), ("number-5",14), ("number-6",15),
            ("number-7",16), ("number-8",17), ("number-9",18) })
            for (int frame = 0; frame < 4; frame++)
            {
                int palette = bindings[resource] - 200;
                string roleKey = "deployment-" + role + (frame == 0 ? "" : $"-{frame}");
                var pixels = IndexedImageArchive.Frame(common.Images[resource], 4, 1, frame, 0);
                assets.Add(save("ui-" + roleKey, TextureSourcePath.Image("G_Common", resource, palette, true, true, columns: 4, frame: frame),
                    IndexedImageArchive.Decode(pixels, common.Palettes[palette], true, true)));
                ui.Add(roleKey, "ui-" + roleKey);
            }
        foreach (var (role, resource, palette) in new[] {
            ("background",116,12), ("footer",130,13), ("pages-background",143,13),
            ("frame-top-left",121,13), ("frame-top",122,13), ("frame-top-right",123,13),
            ("frame-note-top-right",124,13), ("frame-left",125,13), ("frame-right",126,13),
            ("frame-bottom-left",127,13), ("frame-bottom",128,13), ("frame-bottom-right",129,13),
            ("title-left",144,13), ("title-middle",145,13), ("title-right",146,13),
            ("historical",287,32), ("fictional",286,32), ("difficulty",288,32) })
            Extra("save-load-" + role, resource, palette, role != "background");
        foreach (var (role, resource, palette) in new[] {
            ("save-row",284,31), ("load-row",282,29), ("page",285,32),
            ("yes",117,13), ("no",444,60), ("close",251,24), ("help",252,24) })
        {
            int actualPalette = bindings[resource] - 200;
            if (actualPalette != palette)
                throw new InvalidDataException("Unconfirmed save/load palette binding.");
            for (int frame = 0; frame < 4; frame++)
            {
                string key = "save-load-" + role + (frame == 0 ? "" : $"-{frame}");
                var pixels = IndexedImageArchive.Frame(common.Images[resource], 4, 1, frame, 0);
                assets.Add(save("ui-" + key, TextureSourcePath.Image("G_Common", resource, actualPalette, true, true, columns: 4, frame: frame),
                    IndexedImageArchive.Decode(pixels, common.Palettes[actualPalette], true, true)));
                ui.Add(key, "ui-" + key);
            }
        }
        foreach (var (part, resource) in new[] { ("left", 131), ("middle", 132), ("right", 133) })
            Extra("environment-label-" + part, resource, 13);
        foreach (var (part, resource) in new[] { ("left", 118), ("middle", 119), ("right", 120) })
        for (int frame = 0; frame < 4; frame++)
        {
            if (bindings[resource] != 213) throw new InvalidDataException("Unconfirmed environment choice palette.");
            string role = "environment-choice-" + part + (frame == 0 ? "" : "-" + frame), id = "ui-" + role;
            assets.Add(save(id, TextureSourcePath.Image("G_Common", resource, 13, true, true, columns: 4, frame: frame),
                IndexedImageArchive.Decode(IndexedImageArchive.Frame(common.Images[resource], 4, 1, frame, 0), common.Palettes[13], true, true)));
            ui.Add(role, id);
        }
        // RE-0178: external display types, independent of settlement identities and domain rules.
        TargetMarkerOffset[] offsets = [new(-16,-58), new(-20,-38), new(-4,-34), new(-30,-34), new(2,-52),
            new(-33,-52), new(-16,-22), new(-16,-32), new(-16,-28), new(-16,-41), new(-16,-30),
            new(-24,-32), new(-10,-34), new(-16,-34), new(-16,-32), new(-13,-32), new(-16,-34)];
        TargetMarkerOffset[] arrowOffsets = [new(-28,-65), new(-38,-41), new(-10,-54), new(-45,-54), new(-22,-35),
            new(-49,-42), new(-31,-39), new(-28,-52), new(-28,-50), new(-34,-45), new(-31,-42),
            new(-35,-39), new(-25,-30), new(-21,-43), new(-40,-35), new(-21,-34), new(-26,-41)];
        return new(InterfaceSkin.CurrentSchemaVersion, assets.ToArray(), ui, colors, cursors)
        {
            BuildingTargetOffsets = offsets.Select((offset, type) => (offset, type)).ToDictionary(p => p.type, p => p.offset),
            BuildingArrowOffsets = arrowOffsets.Select((offset, type) => (offset, type)).ToDictionary(p => p.type, p => p.offset)
        };
    }

    private static IndexedImage CompileDeploymentField(IReadOnlyList<IndexedImage> sources)
    {
        // The source frame discards eight pixels at each outside edge. Its center
        // tile is narrower than the horizontal edges; repeat it in the exported grid.
        var parts = new IndexedImage[9];
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
        {
            var source = sources[row * 3 + column];
            int x = column == 0 ? 8 : 0, y = row == 0 ? 8 : 0;
            int width = source.Width - (column == 1 ? 0 : 8);
            int height = source.Height - (row == 1 ? 0 : 8);
            int outputWidth = column == 1 ? sources[1].Width : width;
            byte[] pixels = new byte[checked(outputWidth * height)];
            for (int py = 0; py < height; py++)
            for (int px = 0; px < outputWidth; px++)
                pixels[py * outputWidth + px] = source.Pixels[(y + py) * source.Width + x + px % width];
            parts[row * 3 + column] = new(outputWidth, height, pixels);
        }
        return NineSliceArt.Assemble(parts);
    }
}
