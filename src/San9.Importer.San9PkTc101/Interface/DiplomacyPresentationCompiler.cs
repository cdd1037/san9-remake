using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class DiplomacyPresentationCompiler
{
    internal static DiplomacyPresentationDefinition Compile(TextureLibrary library)
    {
        // RE-0182 section 17: regular gift/request/return/surrender meetings share this composition.
        string Asset(int image) => library.Archives.Single(a => a.Id == "g_gaikou").Variants
            .Single(v => v.ImageIndex == image && v.PaletteIndex == (image < 25 ? 0 : image < 48 ? 1 : 2)).Image.Id;
        DiplomacySprite Sprite(int image, int columns = 10, int milliseconds = 100) => new(Asset(image), columns, milliseconds);
        DiplomacyLayer Layer(int image, int x, int y, int columns = 1, int milliseconds = 100) => new(Sprite(image, columns, milliseconds), x, y);
        List<DiplomacyActorDefinition> actors = [];
        void Actor(DiplomacyActorRole role, bool female, bool ruler, int rest, int salute, int bow, int x, int y, int shadowY)
        {
            int depth = role switch { DiplomacyActorRole.LeftGuard => 4, DiplomacyActorRole.Envoy => 7,
                DiplomacyActorRole.Ruler => 9, DiplomacyActorRole.Adviser => 11, _ => 13 };
            // Shadow images are independently positioned, rather than scaled copies of the bodies.
            actors.Add(new(role, female, ruler, Layer(rest, x, y, 10) with { Depth = depth }, Sprite(salute), Sprite(bow, milliseconds: 143),
                Layer(rest + 24, x, shadowY, 10) with { Depth = depth - 1 }, Sprite(salute + 24), Sprite(bow + 24, milliseconds: 143)));
        }
        foreach (bool female in new[] { false, true })
        {
            int sex = female ? 2 : 0;
            Actor(DiplomacyActorRole.Ruler, female, false, 10 + sex, 9 + sex, 10 + sex, 511, 245, 325);
            // Advisers only have the forward bow; the salute slot is unused for this role.
            int adviser = female ? 8 : 7;
            Actor(DiplomacyActorRole.Adviser, female, false, adviser, adviser, adviser, 555, 273, 357);
            foreach (bool ruler in new[] { false, true })
            {
                int envoy = (ruler ? 1 : 13) + sex;
                Actor(DiplomacyActorRole.Envoy, female, ruler, envoy, envoy, envoy + 1, 415, 278, 362);
            }
        }
        Actor(DiplomacyActorRole.LeftGuard, false, false, 17, 17, 17, 255, 261, 337);
        Actor(DiplomacyActorRole.RightGuard, false, false, 18, 18, 18, 583, 300, 380);
        return new(Layer(44, 172, 288), Layer(23, 439, 310),
            [Layer(24, 499, 227, 10, 125) with { Depth = 2 }, Layer(46, 495, 241, 8, 200) with { Depth = 5 },
                Layer(47, 614, 266, 8, 200) with { Depth = 5 }],
            Asset(48), Asset(49), Asset(50), actors.ToArray(), "101922", 217 / 256f,
            "{0}军  {1}", "使者：{0}〈{1}军〉");
    }
}
