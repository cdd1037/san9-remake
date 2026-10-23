using System.Text.Json;
using San9.Content.Packages;

namespace San9.Content.Tests;

public sealed class ContentCatalogTests
{
    [Fact]
    public async Task CatalogReadsAuthoredCategoriesWithoutOpeningWorldsOrAssumingSourceIds()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new ContentCatalog(ContentCatalog.CurrentSchemaVersion, "shared/textures", "shared/opening.json",
                [new(new("a-custom-id", "custom-category", 281, 1, "自制剧本", "简介", []), "scenarios/custom/a", "world-custom")]);
            await File.WriteAllTextAsync(Path.Combine(root, "catalog.json"), JsonSerializer.Serialize(catalog, PackageFiles.JsonOptions));
            var loaded = await ContentCatalogFiles.LoadAsync(root);
            Assert.Equal("custom-category", loaded.RequireScenario("a-custom-id").Preview.CategoryId);
            Assert.Equal("world-custom", loaded.RequireScenario("a-custom-id").WorldId);
            Assert.Throws<InvalidDataException>(() => loaded.RequireScenario("190"));
            Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }
}
