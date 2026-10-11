using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class BattleSceneCompiler
{
    internal static BattleSceneDefinition Compile(Func<int, int, string> asset)
    {
        BattleSceneLayer Layer(int image, int x, int y) => new(asset(image, image), x, y);
        List<BattleTerrainAppearance> terrains = [];
        foreach (string season in new[] { "spring", "summer", "autumn", "winter" })
        {
            void Ground(int[] ids, int image, string kind, int min = 0, int max = 7, int[]? variants = null)
            {
                bool river = kind.StartsWith("river", StringComparison.Ordinal), water = kind.StartsWith("water", StringComparison.Ordinal);
                terrains.Add(new(ids, season, min, max, variants, kind, Layer(image,24,104), Layer(image+1,144,104),
                    kind == "other" ? null : Layer(river || water ? 46 : 44,144,184),
                    kind == "other" ? null : Layer(river ? 47 : water ? kind == "water-steep" ? 49 : 48 : 45,144,144)));
            }
            foreach (bool high in new[] { false, true })
            {
                Ground([0], (season == "autumn" ? 12 : season == "winter" ? 14 : 10) + (high ? 6 : 0), "land", high ? 4 : 0, high ? 7 : 3);
                Ground([2,4,7], high ? 8 : 6, "land", high ? 4 : 0, high ? 7 : 3);
            }
            Ground([3,5,8,9,12], season == "autumn" ? 2 : season == "winter" ? 4 : 0, "land");
            Ground([6],24,"land",variants:[1]); Ground([6],22,"land");
            Ground([10,15,16],28,"river-steep",variants:[3]);
            Ground([10,15,16],28,"river",variants:[4]); Ground([10,15,16],26,"river");
            Ground([11],32,"water-steep",variants:[3]); Ground([11],32,"water",variants:[4]); Ground([11],30,"water");
            Ground([1,13,14],0,"other");
        }
        List<BattleTerrainTransition> transitions = [];
        void Transition(string source, string target, int image, int x, int y) => transitions.Add(new(source,target,Layer(image,x,y)));
        Transition("land","river",35,72,184); Transition("land","river-steep",39,120,200);
        Transition("land","water",42,176,176); Transition("land","water-steep",43,176,176);
        Transition("river","land",36,120,168); Transition("river-steep","land",40,160,192);
        Transition("river","river",34,64,104); Transition("river","river-steep",37,64,104);
        Transition("river-steep","river",41,64,104); Transition("river-steep","river-steep",38,64,104);
        var city = new BattleFacilityBackdrop([Layer(51,0,152)], [Layer(50,144,0)], true);
        var field = new BattleFacilityBackdrop([Layer(55,80,168)], [Layer(54,200,120)], false);
        var fort = new BattleFacilityBackdrop([Layer(53,24,192),Layer(57,120,240),Layer(57,480,420)],
            [Layer(52,144,24),Layer(56,488,56),Layer(56,848,236)],true);
        return new(terrains.ToArray(),transitions.ToArray(),new()
        { [0]=city,[1]=field,[2]=city,[3]=field,[4]=fort,[5]=fort,[6]=city });
    }
}
