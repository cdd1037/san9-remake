using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Maps;

namespace San9.Rendering;

/// <summary>Finite member poses detached from an active unit. Sampling never changes the world or plays audio.</summary>
public sealed class UnitRetirementPlayback
{
    private sealed record Member(string BodyId, MapUnitRetirement Definition, string Clip, double Interval,
        double X, double Y, double GroundX, double GroundY, double CenterX, double CenterY, bool Lead);
    public sealed record Sound(string Role, double X, double Y);
    private readonly Member[] _members;
    private readonly IReadOnlyDictionary<string, MapAnimationClip> _clips;
    private readonly HashSet<string> _sounds = [];
    private readonly uint _seed;
    public string Id { get; }
    public double Start { get; }
    public double Duration { get; }
    public IEnumerable<string> Assets => _members.SelectMany(m => _clips[m.Clip].Frames
        .Concat(m.Definition.Effect is {} effect ? _clips[effect.ClipId].Frames : [])).Distinct();

    public UnitRetirementPlayback(string id, double start, UnitVisualState state, UnitMotionPlayback motion,
        MapUnitCatalog catalog, IReadOnlyDictionary<string, MapAnimationClip> clips,
        IReadOnlyDictionary<string, ImageAsset> assets, bool self, Func<double, double, double> elevationAt)
    {
        Id = id; Start = start; _clips = clips; _seed = UnitVisuals.Seed(state.Id, 0, 23);
        var layout = catalog.Formations.Single(f => f.FormationId == state.FormationId).Layouts
            .Where(l => l.MinimumSoldiers <= state.BodySoldiers).MaxBy(l => l.MinimumSoldiers)!;
        _members = layout.Members.Select(member =>
        {
            var body = catalog.Bodies.Single(b => b.Id == member.BodyId);
            if (state.UsesImperialAppearance && body.ImperialBodyId is {} imperial)
                body = catalog.Bodies.Single(b => b.Id == imperial);
            if (body.Retirement is not {} retirement) return null;
            var pose = motion.Position(member);
            int direction = self ? UnitVisuals.ScreenDirection(member.X, member.Y) : motion.Direction(member, state);
            string clip = retirement.DirectionClips[direction];
            var image = assets[clips[clip].Frames[0]];
            double x = pose.X - body.AnchorX, y = pose.Y - 10 * (elevationAt(pose.X, pose.Y) - 1) - body.AnchorY;
            return new Member(body.Id, retirement, clip, motion.Interval(member), x, y, pose.X, pose.Y,
                x + image.Width / 2.0, y + image.Height / 2.0, member.Group == 0 && member.Index == 0);
        }).OfType<Member>().ToArray();
        Duration = _members.Select(m => Math.Max(FrameDuration(m) + m.Definition.FadeMilliseconds,
            m.Definition.Effect is {} effect ? m.Definition.EffectDelayMilliseconds +
                clips[effect.ClipId].Frames.Length * clips[effect.ClipId].FrameMilliseconds : 0)).DefaultIfEmpty(0).Max();
    }

    private double FrameDuration(Member member) => _clips[member.Clip].Frames.Length * member.Interval;
    public bool IsComplete(double time) => time - Start >= Duration;
    public IEnumerable<MapAnimatedSprite> Sample(double time)
    {
        double age = time - Start;
        if (age < 0) yield break;
        foreach (var member in _members)
        {
            double fade = age - FrameDuration(member);
            var definition = member.Definition;
            if (fade < definition.FadeMilliseconds)
            {
                double opacity = fade < definition.FadeStepMilliseconds ? 1 : Math.Max(0,
                    1 - (Math.Floor(fade / definition.FadeStepMilliseconds) + 1) * definition.FadeStepMilliseconds / definition.FadeMilliseconds);
                yield return new(member.Clip, (int)Math.Round(member.X), (int)Math.Round(member.Y), 0, -1)
                {
                    Depth = member.GroundY + 30000, IsBody = true, GroundX = member.GroundX, GroundY = member.GroundY,
                    Frame = (int)Math.Min(_clips[member.Clip].Frames.Length - 1, Math.Floor(age / member.Interval)), Opacity = opacity
                };
            }
            if (definition.Effect is not {} effect) continue;
            double effectAge = age - definition.EffectDelayMilliseconds;
            var clip = _clips[effect.ClipId];
            if (effectAge >= 0 && effectAge < clip.Frames.Length * clip.FrameMilliseconds)
                yield return new(effect.ClipId, (int)Math.Round(member.CenterX + effect.X),
                    (int)Math.Round(member.CenterY + effect.Y), 0, -1)
                { Depth = member.GroundY + 30001, Frame = (int)(effectAge / clip.FrameMilliseconds) };
        }
    }

    // The owning map consumes each sound once, even when disabled or outside the viewport.
    public IEnumerable<Sound> TakeSounds(double time, MapAudioDefinition audio)
    {
        double age = time - Start;
        if (age < 0) yield break;
        foreach (var member in _members.Where(m => m.Lead))
        {
            if (audio.Bodies[member.BodyId].Retirement is not {} sounds) continue;
            if (age >= sounds.DelayMilliseconds && _sounds.Add("start") && sounds.Start.Length > 0)
                yield return new(sounds.Start[_seed % (uint)sounds.Start.Length], member.CenterX, member.CenterY);
            if (age >= FrameDuration(member) && _sounds.Add("end") && sounds.End is {} end)
                yield return new(end, member.CenterX, member.CenterY);
        }
    }
}
