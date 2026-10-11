using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Content.Maps;

namespace San9.Rendering;

public sealed record BattleOfficerVisual(int PersonId, int Color, int Glyph, int Frame);

/// <summary>Independent scene actors; flags reuse directional map layers, never portraits or soldier formations.</summary>
public sealed class BattleOfficerPlayback(BattleOfficerDisplay definition, ImmutableArray<BattleOfficerVisual> officers)
{
    public BattleOfficerDisplay Definition { get; } = definition;
    public ImmutableArray<BattleOfficerVisual> Officers { get; } = officers;
    public IEnumerable<string> FlagClips => Officers.SelectMany(p => FlagVisuals.Layers(Definition.FlagDirection, p.Color, p.Glyph)).Distinct();

    public IEnumerable<BattleZoomFrame> Sample(double elapsed, IReadOnlyDictionary<string, MapAnimationClip> clips)
    {
        if (elapsed < 0) yield break;
        for (int i = 0; i < Officers.Length; i++)
        {
            var person = Officers[i];
            var position = i < Definition.Positions.Length ? Definition.Positions[i] : Extend(i);
            int frame = (person.Frame + (int)(elapsed * 1000 / Definition.Idle.FrameMilliseconds)) % Definition.Idle.Columns;
            yield return new(Definition.Idle, position.X, position.Y, frame);
            foreach (string role in FlagVisuals.Layers(Definition.FlagDirection, person.Color, person.Glyph))
            {
                var clip = clips[role];
                string asset = clip.Frames[(int)(elapsed * 1000 / clip.FrameMilliseconds) % clip.Frames.Length];
                yield return new(new(asset, 1, clip.FrameMilliseconds), position.X + Definition.FlagOffset.X,
                    position.Y + Definition.FlagOffset.Y, 0) { Depth = position.Y };
            }
        }
    }

    private BattlePoint Extend(int index)
    {
        // Continue the alternating wings beyond the authored positions, without a native slot limit.
        int wing = (index - Definition.Positions.Length) % 2;
        int steps = (index - Definition.Positions.Length) / 2 + 1;
        var last = Definition.Positions[Definition.Positions.Length - 2 + wing];
        int sign = wing == 0 ? 1 : -1;
        return new(last.X + sign * steps * Definition.ExtensionStep.X, last.Y + sign * steps * Definition.ExtensionStep.Y);
    }
}
