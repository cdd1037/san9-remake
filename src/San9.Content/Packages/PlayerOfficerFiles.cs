using System.Text.Json;
using San9.Content.Scenarios;

namespace San9.Content.Packages;

public static class PlayerOfficerFiles
{
    public static async Task<OfficerDefaultsCatalog> LoadAsync(OfficerDefaultsCatalog shared, string? path, CancellationToken token)
        => path is null ? shared : Merge(shared, await PlayerDefinitionFiles.ReadOptionalAsync(path, token));

    public static OfficerDefaultsCatalog Merge(OfficerDefaultsCatalog shared, byte[]? playerFile)
    {
        if (playerFile is null) return shared;
        var personal = JsonSerializer.Deserialize<OfficerDefaultsCatalog>(playerFile, ScenarioContentFiles.DataJsonOptions)
            ?? throw new InvalidDataException("玩家武将库为空。");
        ContentStructure.Validate(personal);
        if (personal.SchemaVersion != OfficerDefaultsCatalog.CurrentSchemaVersion || personal.NextOfficerId < 0)
            throw new InvalidDataException("玩家武将库的版本或编号无效。");
        var officers = shared.Officers.Concat(personal.Officers).ToArray();
        OfficerComposition.Index(officers);
        return shared with { Officers = officers, NextOfficerId = Math.Max(shared.NextOfficerId, personal.NextOfficerId) };
    }
}
