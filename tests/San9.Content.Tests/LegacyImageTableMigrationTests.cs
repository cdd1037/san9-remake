using System.Text.Json.Nodes;
using San9.Content.Packages;
using Xunit;

namespace San9.Content.Tests;

public sealed class LegacyImageTableMigrationTests
{
    [Fact]
    public void PreparesPortraitAnchorsWithoutClaimingCurrentSchemaOrSourceDigests()
    {
        var source = JsonNode.Parse("""
            {"schemaVersion":5,"sources":[{"name":"old.s9","length":1,"lastWriteTimeUtc":"2000-01-01T00:00:00Z"}],
             "map":{"units":{}},"portraits":[{"id":1,"anchorX":0.1,"anchorY":0.1}]}
            """)!.AsObject();

        var result = LegacyImageTableMigration.PrepareTextureV5(source);

        Assert.Equal(5, (int)result["schemaVersion"]!);
        Assert.Empty(result["sources"]!.AsArray());
        Assert.NotNull(result["portraits"]![0]!["bustAnchor"]);
        Assert.NotNull(result["portraits"]![0]!["faceAnchor"]);
        Assert.NotNull(source["portraits"]![0]!["anchorX"]);
    }

    [Fact]
    public void ConvertsRepeatedFilesWithoutChangingSource()
    {
        var source = JsonNode.Parse("""
            {"images":[
              {"id":"a","file":{"path":"images/a.png","width":4,"height":4,"channels":4},"x":0,"y":0,"width":2,"height":2},
              {"id":"b","file":{"path":"images/a.png","width":4,"height":4,"channels":4},"x":2,"y":2,"width":2,"height":2}
            ]}
            """)!.AsObject();

        var result = LegacyImageTableMigration.Convert(source);

        Assert.Single(result["files"]!.AsArray());
        Assert.Equal(0, (int)result["images"]![0]!["file"]!);
        Assert.Equal(0, (int)result["images"]![1]!["file"]!);
        Assert.IsType<JsonObject>(source["images"]![0]!["file"]);
    }

    [Fact]
    public void RejectsConflictingFileMetadata()
    {
        var source = JsonNode.Parse("""
            {"images":[
              {"id":"a","file":{"path":"a.png","width":4,"height":4,"channels":4},"x":0,"y":0,"width":2,"height":2},
              {"id":"b","file":{"path":"a.png","width":8,"height":4,"channels":4},"x":0,"y":0,"width":2,"height":2}
            ]}
            """)!.AsObject();

        Assert.Throws<InvalidDataException>(() => LegacyImageTableMigration.Convert(source));
    }
}
