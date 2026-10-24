using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Maps;
using San9.Content.Packages;

namespace San9.Content.Tests;

public sealed class MapUnitAttackContractTests
{
    private static readonly Dictionary<string, MapAnimationClip> Clips = new() { ["body"] = new("body", ["frame"], 100, []) };
    private static MapUnitBody Body => new("custom", Enumerable.Repeat("body", 8).ToArray(), 5, 8)
    {
        Flags = Enumerable.Range(0, 8).Select(_ => Enumerable.Range(0, 3).Select(i => new MapUnitFlagPlacement(i, -20, i)).ToArray()).ToArray(),
        MeleeAttack = new([new(Enumerable.Repeat("body", 8).ToArray())], MapProjectileKind.Arrow, 7)
    };
    private static MapUnitCatalog Catalog(MapUnitBody body) => new([body], [], [], -1)
    { Projectiles = new() { [MapProjectileKind.Arrow] = new(["arrow"], 64, false, 0, 0) } };

    [Fact]
    public void AuthoredContentSupportsMultipleFlagsAndAnIndependentVolleyAndRejectsInvalidRecipes()
    {
        Catalog(Body).Validate(Clips);
        var roundTrip = JsonSerializer.Deserialize<MapUnitBody>(JsonSerializer.Serialize(Body, PackageFiles.JsonOptions), PackageFiles.JsonOptions)!;
        Assert.Equal(3, roundTrip.Flags[7].Length);
        Assert.Equal(7, roundTrip.MeleeAttack!.VolleySize);
        void Reject(MapUnitBody body) => Assert.Throws<InvalidDataException>(() => Catalog(body).Validate(Clips));
        Reject(Body with { Flags = Body.Flags[..7] });
        Reject(Body with { MeleeAttack = Body.MeleeAttack! with { Actions = [] } });
        Reject(Body with { MeleeAttack = Body.MeleeAttack! with { VolleySize = 0 } });
        Reject(Body with { MeleeAttack = Body.MeleeAttack! with { Projectile = MapProjectileKind.None } });
        Reject(Body with { MeleeAttack = Body.MeleeAttack! with { Projectile = MapProjectileKind.Stone } });
        Reject(Body with { MeleeAttack = Body.MeleeAttack! with { Timings = [] } });
        Reject(Body with { MeleeAttack = Body.MeleeAttack! with { Timings = [new(-1, 500)] } });
        Reject(Body with { MeleeAttack = Body.MeleeAttack! with { ShotSpacingMilliseconds = -1 } });
    }

    [Theory]
    [InlineData("timings")]
    [InlineData("shotSpacingMilliseconds")]
    [InlineData("blocksDayProgress")]
    public void TimingAndBlockingAreRequiredContentInsteadOfRuntimeBodyIdRules(string field)
    {
        var node = JsonSerializer.SerializeToNode(Body, PackageFiles.JsonOptions)!;
        var recipe = node["meleeAttack"]!.AsObject();
        if (field == "blocksDayProgress") recipe["actions"]![0]!.AsObject().Remove(field);
        else recipe.Remove(field);
        Assert.Throws<JsonException>(() => node.Deserialize<MapUnitBody>(PackageFiles.JsonOptions));
    }

    [Theory]
    [InlineData("flags")]
    [InlineData("meleeAttack")]
    [InlineData("rangedAttack")]
    [InlineData("retirement")]
    public void MissingVisualDefinitionsAreRejectedInsteadOfReadingTheOldActionContract(string field)
    {
        var node = JsonSerializer.SerializeToNode(Body, PackageFiles.JsonOptions)!.AsObject();
        node.Remove(field);
        Assert.Throws<JsonException>(() => node.Deserialize<MapUnitBody>(PackageFiles.JsonOptions));
    }

    [Fact]
    public void RetirementDefinitionsValidateTheirOwnClipsEffectsAndLifetime()
    {
        var retirement = new MapUnitRetirement(Enumerable.Repeat("body", 8).ToArray(), new("body", -4, -5), 500, 1000, 100);
        var body = Body with { Retirement = retirement };
        Catalog(body).Validate(Clips);
        Assert.Throws<InvalidDataException>(() => Catalog(body with { Retirement = retirement with { DirectionClips = ["body"] } }).Validate(Clips));
        Assert.Throws<InvalidDataException>(() => Catalog(body with { Retirement = retirement with { Effect = new("absent", 0, 0) } }).Validate(Clips));
        Assert.Throws<InvalidDataException>(() => Catalog(body with { Retirement = retirement with { FadeMilliseconds = 0 } }).Validate(Clips));
    }
}
