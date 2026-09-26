using San9.Content.Images;
using San9.Content.Packages;
using San9.Importer.San9PkTc101.Images;

namespace San9.Importer.San9PkTc101.Packages;

internal static class TexturePackagePacking
{
    public static TexturePackage Pack(string output, TexturePackage package, string directory, CancellationToken token)
    {
        var map = package.Map;
        var skin = package.Skin;
        var portraits = package.Portraits;
        var library = package.Library;
        var cpuIds = map.Masks.SelectMany(m => new[] { m.WeightAsset, m.CoverageAsset }).Append("cloud-fill").ToHashSet();
        var animationGroups = new Dictionary<string, HashSet<string>>();
        foreach (var clip in map.Clips)
        {
            // Equivalent seasonal frame sets have the same lifetime; fallback Frames is not another group.
            var frameSets = clip.SeasonFrames.Values.Append(clip.Frames).Where(frames => frames.Length != 0)
                .Select(frames => frames.Distinct().Order(StringComparer.Ordinal).ToArray())
                .GroupBy(frames => string.Join("|", frames), StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal);
            int set = 0;
            foreach (var frames in frameSets)
            {
                string group = TextureGroups.MapAnimation(clip.Id, set++);
                foreach (string id in frames.First())
                {
                    if (!animationGroups.TryGetValue(id, out var groups)) animationGroups[id] = groups = [];
                    groups.Add(group);
                }
            }
        }
        string MapCategory(ImageAsset asset)
        {
            if (asset.Id.StartsWith("mask-", StringComparison.Ordinal) || asset.Id.StartsWith("coverage-", StringComparison.Ordinal)) return "map/cpu/masks";
            if (asset.Id == "cloud-fill") return "map/cpu/clouds";
            if (animationGroups.TryGetValue(asset.Id, out var groups)) return groups.Count == 1 ? groups.Single() : "map/animations/shared";
            string prefix = asset.Id.Split('-')[0];
            return "map/objects/" + prefix;
        }
        var mapSources = map.Assets.Select(a => new TextureAtlasCompiler.Source(a, cpuIds.Contains(a.Id) ? null :
            animationGroups.TryGetValue(a.Id, out var groups) ? groups.Count == 1 ? groups.Single() : null : MapCategory(a), MapCategory(a)));
        var rolesByAsset = skin.Roles.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Select(p => TextureGroups.Interface(p.Key)).Distinct().ToArray());
        var cursorAssets = skin.Cursors.Values.Select(c => c.Asset).ToHashSet();
        var uiSources = skin.Assets.Select(a =>
        {
            if (cursorAssets.Contains(a.Id)) return new TextureAtlasCompiler.Source(a, null, "interface/cursors");
            string category = rolesByAsset.TryGetValue(a.Id, out var groups) && groups.Length == 1 ? groups[0] : "shared";
            string folder = category == "portrait" ? "portraits" : "interface/" + category;
            return new TextureAtlasCompiler.Source(a, category is "portrait" or "shared" ? null : category, folder);
        });
        var landSources = map.Seasons.SelectMany(s => s.Textures.Select(a => new TextureAtlasCompiler.Source(
            a with { Id = $"terrain/{s.Id}/{a.Id}" }, null, $"map/cpu/terrain/{s.Id}")));
        var librarySources = library.Archives.SelectMany(a => a.Variants.Select(v =>
            new TextureAtlasCompiler.Source(v.Image,
                a.Category.StartsWith("portraits/", StringComparison.Ordinal) || a.Category == "illustrations"
                    ? null : $"{a.Category}/{a.Id}/palette-{v.PaletteIndex}",
                a.Id == "g_still" && v.ImageIndex is >= 172 and < 272 ? "illustrations/treasures" : a.Category,
                a.Id == "g_still" && v.ImageIndex is >= 172 and < 272 ? v.ImageIndex - 172 : null)));
        var requests = mapSources.Concat(uiSources).Concat(landSources).Concat(librarySources)
            .Concat(portraits.Select(p => new TextureAtlasCompiler.Source(p.Image, null, "portraits", p.Id))).ToArray();
        // Resolve consumers to their declared source before packing. No pixel comparison occurs.
        // Display/CPU definitions own placement; the complete library references those definitions.
        var selected = requests.Where(s => s.Asset.Path.StartsWith(".sources/", StringComparison.Ordinal)).ToArray();
        var owners = selected.GroupBy(s => s.Asset.File).Select(g => g
            .OrderBy(s => s.Asset.Id.StartsWith("library/", StringComparison.Ordinal))
            .ThenBy(s => s.Asset.Id, StringComparer.Ordinal).First()).ToArray();
        var pages = TextureAtlasCompiler.Compile(output, owners, directory, token).ToDictionary(a => a.Id);
        var bySource = owners.ToDictionary(s => s.Asset.File, s => pages[s.Asset.Id]);
        var packed = requests.ToDictionary(s => s.Asset.Id, s => bySource.TryGetValue(s.Asset.File, out var asset) ? asset with { Id = s.Asset.Id } : s.Asset);
        map = map with { Assets = map.Assets.Select(a => packed[a.Id]).ToArray(),
            Seasons = map.Seasons.Select(s => s with { Textures = s.Textures.Select(a => packed[$"terrain/{s.Id}/{a.Id}"] with { Id = a.Id }).ToArray() }).ToArray() };
        skin = skin with { Assets = skin.Assets.Select(a => packed[a.Id]).ToArray() };
        library = library with { Archives = library.Archives.Select(a => a with {
            Variants = a.Variants.Select(v => v with { Image = packed[v.Image.Id] }).ToArray() }).ToArray() };
        portraits = portraits.Select(p => p with { Image = packed[p.Image.Id] }).ToArray();
        return package with { Map = map, Skin = skin, Library = library, Portraits = portraits };
    }
}
