using System.Text.Json;
using San9.Content.Interface;
using San9.Content.Packages;

namespace San9.Content.Tests;

public sealed class ContentCatalogTests
{
    [Fact]
    public async Task CatalogReadsManifestPreviewsWithoutOpeningAnyWorld()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var entries = Enumerable.Range(0, 20).Select(i => new ScenarioCatalogEntry($"custom-{i}", $"scenarios/custom/{i}")).ToArray();
            var catalog = new ContentCatalog(ContentCatalog.CurrentSchemaVersion, "shared/textures", "shared/opening.json", entries);
            await File.WriteAllTextAsync(Path.Combine(root, "catalog.json"), JsonSerializer.Serialize(catalog, PackageFiles.JsonOptions));
            foreach (var entry in entries)
            {
                string package = PackageFiles.Resolve(root, entry.Package);
                Directory.CreateDirectory(package);
                var preview = new ScenarioPreviewEntry(entry.Id, "custom-category", 281, 1, "自制剧本", "简介", []);
                var manifest = new ScenarioContentManifest(ScenarioContent.CurrentSchemaVersion, new("textures"),
                    "map.json", "scenario.json", "text.json", ["messages.json"], "illustrations.json")
                    { Preview = preview, SharedMessages = "missing-library.json" };
                // None of the world dependencies exist. Preview browsing must only read this entry file.
                await File.WriteAllTextAsync(Path.Combine(package, "content.json"), JsonSerializer.Serialize(manifest, PackageFiles.JsonOptions));
            }
            var loaded = await ContentCatalogFiles.LoadAsync(root);
            Assert.Equal(entries[0].Package, loaded.RequireScenario("custom-0").Package);
            Assert.Throws<InvalidDataException>(() => loaded.RequireScenario("190"));
            var previews = await ContentCatalogFiles.LoadPreviewsAsync(root, loaded);
            Assert.Equal(20, previews.Length);
            Assert.All(previews, p => Assert.Equal("custom-category", p.CategoryId));

            string firstPackage = PackageFiles.Resolve(root, entries[0].Package);
            await ScenarioContentFiles.WritePreviewAsync(firstPackage, previews[0] with { Id = "other" });
            await Assert.ThrowsAsync<InvalidDataException>(() => ContentCatalogFiles.LoadPreviewsAsync(root, loaded));
        }
        finally { Directory.Delete(root, true); }
    }
}
