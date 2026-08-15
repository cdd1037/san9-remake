namespace San9.Content.Validation;

public sealed record ContentValidationIssue(string Code, string Path, string Message);

public sealed record ContentValidationResult(IReadOnlyList<ContentValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class ContentValidator
{
    public static ContentValidationResult Validate(ContentPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        List<ContentValidationIssue> issues = [];

        if (package.Manifest.SchemaVersion <= 0)
        {
            issues.Add(new("manifest.schema_version", "manifest.schemaVersion", "Schema version must be positive."));
        }

        AddDuplicateIssues(
            package.Scenario.Officers.Select(static item => item.Id.Value),
            "scenario.officers",
            issues);
        AddDuplicateIssues(
            package.Scenario.Forces.Select(static item => item.Id.Value),
            "scenario.forces",
            issues);
        AddDuplicateIssues(
            package.Scenario.Settlements.Select(static item => item.Id.Value),
            "scenario.settlements",
            issues);

        HashSet<int> forceIds = package.Scenario.Forces.Select(static item => item.Id.Value).ToHashSet();
        foreach (var settlement in package.Scenario.Settlements)
        {
            if (settlement.OwnerId is { } ownerId && !forceIds.Contains(ownerId.Value))
            {
                issues.Add(new(
                    "settlement.owner_missing",
                    $"scenario.settlements[{settlement.Id.Value}].ownerId",
                    $"Force {ownerId.Value} does not exist."));
            }
        }

        return new ContentValidationResult(issues.AsReadOnly());
    }

    private static void AddDuplicateIssues(
        IEnumerable<int> ids,
        string path,
        ICollection<ContentValidationIssue> issues)
    {
        foreach (int duplicate in ids.GroupBy(static id => id).Where(static group => group.Count() > 1).Select(static group => group.Key))
        {
            issues.Add(new("entity.duplicate_id", path, $"Duplicate id {duplicate}."));
        }
    }
}
