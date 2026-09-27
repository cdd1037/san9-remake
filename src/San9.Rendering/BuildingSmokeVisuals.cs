using San9.Content.Maps;

namespace San9.Rendering;

public static class BuildingSmokeVisuals
{
    public static MapAnimatedSprite[] Resolve(MapBuildingSmoke smoke, BuildingVisualState state, int definitionDurability, bool isCity,
        IReadOnlyDictionary<string, MapAnimationClip> clips)
    {
        if (state.IsDestroyed) return [];
        List<MapAnimatedSprite> result = [];
        // Display-only stable samples, independent of simulation random state and frame rate.
        uint seed = unchecked((uint)(state.Id + state.Morale + state.Month));
        void Add(MapSmokeEmitter[] emitters)
        {
            for (int i = 0; i < emitters.Length; i++)
            {
                var emitter = emitters[i];
                uint sample = Mix(seed ^ unchecked((uint)i * 0x9e3779b9u));
                if (sample % 100 >= emitter.Chance) continue;
                result.Add(new(emitter.ClipId, emitter.X, emitter.Y, (int)(Mix(sample) % clips[emitter.ClipId].Frames.Length), state.Id)
                    { BehindBuilding = emitter.BehindBuilding });
            }
        }
        if (state.IsEngaged) Add(smoke.Engagement);
        if (state.Durability < (long)definitionDurability * 20 / 100 && (!state.IsUnderConstruction || state.IsEngaged))
        {
            bool both = isCity && state.Development >= 601;
            if (both || (seed & 1) != 0) Add(smoke.DamageA);
            if (both || (seed & 1) == 0) Add(smoke.DamageB);
        }
        return result.ToArray();
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value = unchecked(value * 0x7feb352du);
        value ^= value >> 15;
        value = unchecked(value * 0x846ca68bu);
        return value ^ (value >> 16);
    }
}
