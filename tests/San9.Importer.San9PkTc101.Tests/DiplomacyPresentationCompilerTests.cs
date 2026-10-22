using San9.Content.Images;
using San9.Content.Interface;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class DiplomacyPresentationCompilerTests
{
    [Fact]
    public void MeetingCatalogClosesOverSexRoleAndIndependentShadowResources()
    {
        var variants = Enumerable.Range(0, 51).Select(i => new TextureVariant(i, i < 25 ? 0 : i < 48 ? 1 : 2,
            new ImageAsset($"scene/{i}", $"{i}.png", i is 46 or 47 ? 80 : 100, 40))).ToArray();
        var library = new TextureLibrary(3, [new("g_gaikou", "scenes/diplomacy", variants)]);
        var scene = DiplomacyPresentationCompiler.Compile(library);
        scene.Validate(library.Assets);
        var femaleOfficer = scene.Actors.Single(a => a.Role == DiplomacyActorRole.Envoy && a.Female && !a.RulerEnvoy);
        var femaleRuler = scene.Actors.Single(a => a.Role == DiplomacyActorRole.Envoy && a.Female && a.RulerEnvoy);
        Assert.NotEqual(femaleOfficer.Rest.Sprite.Asset, femaleRuler.Rest.Sprite.Asset);
        Assert.NotEqual(femaleOfficer.Rest.Y, femaleOfficer.Shadow.Y);
        Assert.Throws<InvalidDataException>(() => scene.Validate(library.Assets.Where(a => a.Id != femaleOfficer.Shadow.Sprite.Asset)));
        Assert.Throws<InvalidDataException>(() => (scene with { Actors = scene.Actors.Where(a => a != femaleRuler).ToArray() }).Validate(library.Assets));
        Assert.Throws<InvalidDataException>(() => (scene with { Floor = scene.Floor with { Sprite = scene.Floor.Sprite with { Columns = 3 } } }).Validate(library.Assets));
    }
}
