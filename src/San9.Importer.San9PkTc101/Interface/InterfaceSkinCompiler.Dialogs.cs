using San9.Content.Images;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Interface;

public static partial class InterfaceSkinCompiler
{
    private static void CompileMapObjectPicker(SkinSources sources)
    {
        string[] parts = ["top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right"];
        for (int i = 0; i < parts.Length; i++) sources.Export("map-object-picker-" + parts[i], 475 + i, 65, true);
        foreach (var (part, resource) in new[] { ("left", 484), ("middle", 485), ("right", 486) })
        {
            if (sources.Bindings[resource] != 265) throw new InvalidDataException("Unconfirmed map-object row palette.");
            for (int frame = 0; frame < 4; frame++)
            {
                string role = "map-object-row-" + part + (frame == 0 ? "" : $"-{frame}");
                string id = "ui-" + role;
                var image = IndexedImageArchive.Frame(sources.Common.Images[resource], 4, 1, frame, 0);
                sources.Assets.Add(sources.Save(id, TextureSourcePath.Image("G_Common", resource, 65, true, true, columns: 4, frame: frame),
                    IndexedImageArchive.Decode(image, sources.Common.Palettes[65], true, true)));
                sources.Roles.Add(role, id);
            }
        }
    }

    private sealed record SkinSources((byte[][] Palettes, IndexedImage[] Images) Common,
        (byte[][] Palettes, IndexedImage[] Images) Marker, Dictionary<int, int> Bindings,
        List<ImageAsset> Assets, Dictionary<string, string> Roles, Func<string, string, TextureSourceImage, ImageAsset> Save)
    {
        internal void Export(string role, int resource, int palette, bool alpha)
        {
            bool marker = resource >= 1220;
            int start = marker ? 1220 : 0, paletteBase = marker ? 311 : 200;
            if (Bindings[resource] != palette + paletteBase) throw new InvalidDataException("Unconfirmed interface resource binding.");
            var bank = marker ? Marker : Common;
            string id = "ui-" + role;
            Assets.Add(Save(id, TextureSourcePath.Image(marker ? "G_Marker" : "G_Common", resource - start, palette, true, alpha),
                IndexedImageArchive.Decode(bank.Images[resource - start], bank.Palettes[palette], true, alpha)));
            Roles.Add(role, id);
        }
    }

    private static void CompileDeployment(SkinSources sources)
    {
        var common = sources.Common;
        var bindings = sources.Bindings;
        var assets = sources.Assets;
        var ui = sources.Roles;
        var save = sources.Save;
        void Extra(string role, int resource, int palette, bool alpha = false) => sources.Export(role, resource, palette, alpha);
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
            assets.Add(save("ui-" + key, "ui/" + key + ".png", new PngImage(mask.Width, mask.Height, 4, pixels)));
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
        Extra("deployment-unit-preview-background",199,20,true);
        Extra("deployment-unit-preview-frame",200,20,true);
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
    }
    private static void CompileSaveLoad(SkinSources sources)
    {
        var common = sources.Common;
        var bindings = sources.Bindings;
        var assets = sources.Assets;
        var ui = sources.Roles;
        var save = sources.Save;
        void Extra(string role, int resource, int palette, bool alpha = false) => sources.Export(role, resource, palette, alpha);
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
    }
    private static void CompileEnvironment(SkinSources sources)
    {
        var common = sources.Common;
        var bindings = sources.Bindings;
        var assets = sources.Assets;
        var ui = sources.Roles;
        var save = sources.Save;
        void Extra(string role, int resource, int palette, bool alpha = false) => sources.Export(role, resource, palette, alpha);
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
    }
}
