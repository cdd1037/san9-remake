namespace San9.Content;

public sealed record EntityMapping(string SourceId, string CanonicalId);

public sealed record ResourceEntry(string ResourceId, string RelativePath, string Sha256);

public sealed record ContentManifest(
    int SchemaVersion,
    string PackageId,
    string DisplayName,
    string SourceBuild,
    IReadOnlyList<EntityMapping> EntityMappings,
    IReadOnlyList<ResourceEntry> Resources);
