using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public enum DuelStageKind { Entry, Dialogue, Approach, Preparation, Strike, Impact, Effect, Victory, Return, Exit, Departure, Audience, Feint, Recovery }
public enum DuelAudienceMood { Idle, Cheer, Dismay }
public sealed record DuelEffectFrame(BattleSprite Sprite, int Frame, float Opacity);
public sealed record DuelFighterView(int Id, bool Female, int War, int Injury, int HitPoints);
public sealed record DuelFlagView(BattlePoint Position, int Color, int Glyph, int Direction, int Phase);
public sealed record DuelParticleCue(double Start, double End, DuelParticleDefinition Definition, bool Right);
public sealed record DuelStage(double Start, double End, DuelStageKind Kind,
    DuelFighterView Left, DuelFighterView Right, DuelAction LeftAction, DuelAction RightAction,
    int LeftHitPoints, int RightHitPoints, string? Sound, CommandMessage? Dialogue, ImmutableArray<BattleSprite> Effects)
{
    public BattlePoint LeftFrom { get; init; } = new(0, 0);
    public BattlePoint LeftTo { get; init; } = new(0, 0);
    public BattlePoint RightFrom { get; init; } = new(0, 0);
    public BattlePoint RightTo { get; init; } = new(0, 0);
    public double LeftAnimationAge { get; init; }
    public double RightAnimationAge { get; init; }
    public float LeftOpacityFrom { get; init; } = 1;
    public float LeftOpacityTo { get; init; } = 1;
    public float RightOpacityFrom { get; init; } = 1;
    public float RightOpacityTo { get; init; } = 1;
    public DuelAudienceMood LeftAudience { get; init; }
    public DuelAudienceMood RightAudience { get; init; }
    public double LeftAudienceAge { get; init; }
    public double RightAudienceAge { get; init; }
    public double LeftHitPointsStart { get; init; }
    public double RightHitPointsStart { get; init; }
    public double LeftHitPointsFrom { get; init; }
    public double RightHitPointsFrom { get; init; }
}

/// <summary>A complete occurrence prepared from facts, including replacements. Sampling never touches world RNG.</summary>
public sealed record DuelSceneCue(string Id, double Start, double End, DuelPresentationDefinition Definition,
    string Backdrop, ImmutableArray<int> LeftRoster, ImmutableArray<int> RightRoster, ImmutableArray<DuelStage> Stages)
{
    public int FactIndex { get; init; }
    public ImmutableArray<DuelFlagView> Flags { get; init; } = [];
    public ImmutableArray<DuelParticleCue> Particles { get; init; } = [];
    public const double EffectFadeDuration = .2;
    public const double DialogueDuration = 3;
    public static double HitPointsDuration(double from, int to)
    {
        double difference = Math.Abs(to - from);
        return Math.Ceiling(difference / Math.Max(1, (int)difference / 10)) * .1;
    }
    public static (DuelAudienceMood Left, DuelAudienceMood Right) AudienceReaction(int leftHp, int rightHp,
        bool attackingLeft, DuelStrikeKind kind, int advantage)
    {
        DuelAudienceMood left = DuelAudienceMood.Idle, right = DuelAudienceMood.Idle;
        if (Math.Abs(leftHp - rightHp) >= 30)
        {
            left = leftHp > rightHp ? DuelAudienceMood.Cheer : DuelAudienceMood.Dismay;
            right = rightHp > leftHp ? DuelAudienceMood.Cheer : DuelAudienceMood.Dismay;
        }
        else if (leftHp < rightHp && leftHp < 20) left = DuelAudienceMood.Dismay;
        else if (rightHp < leftHp && rightHp < 20) right = DuelAudienceMood.Dismay;
        if (kind is DuelStrikeKind.DoubleStrike or DuelStrikeKind.FinishingStrike)
        {
            left = attackingLeft ? DuelAudienceMood.Cheer : DuelAudienceMood.Dismay;
            right = attackingLeft ? DuelAudienceMood.Dismay : DuelAudienceMood.Cheer;
        }
        else if (kind == DuelStrikeKind.Parry && (attackingLeft ? leftHp < rightHp : rightHp < leftHp))
        {
            if (attackingLeft) left = DuelAudienceMood.Dismay; else right = DuelAudienceMood.Dismay;
        }
        if (advantage >= 3) { if (attackingLeft) left = DuelAudienceMood.Cheer; else right = DuelAudienceMood.Cheer; }
        return (left, right);
    }
    public static DuelEffectFrame? SampleEffect(ImmutableArray<BattleSprite> effects, double age)
    {
        if (effects.IsEmpty || age < 0) return null;
        foreach (var effect in effects)
        {
            double duration = effect.Columns * effect.FrameMilliseconds / 1000.0;
            if (age < duration) return new(effect, (int)(age * 1000 / effect.FrameMilliseconds), 1);
            age -= duration;
        }
        if (age >= EffectFadeDuration) return null;
        return new(effects[^1], effects[^1].Columns - 1, Math.Max(0, 255 - 32 * (int)(age / .025)) / 255f);
    }
    public static (double X, double Y) Position(DuelStage stage, double time, bool right)
    {
        var from = right ? stage.RightFrom : stage.LeftFrom;
        var to = right ? stage.RightTo : stage.LeftTo;
        double progress = Progress(stage, time);
        return (from.X + (to.X - from.X) * progress, from.Y + (to.Y - from.Y) * progress);
    }
    public static int AnimationFrame(DuelAnimation animation, double age, bool loop)
    {
        double milliseconds = Math.Max(0, age) * 1000;
        if (animation.Frames.Length == 0)
        {
            int frame = (int)(milliseconds / animation.FrameMilliseconds);
            return loop ? frame % animation.Columns : Math.Min(frame, animation.Columns - 1);
        }
        if (loop) milliseconds %= animation.Intervals.Sum();
        for (int i = 0; i < animation.Frames.Length; i++)
        {
            if (milliseconds < animation.Intervals[i]) return animation.Frames[i];
            milliseconds -= animation.Intervals[i];
        }
        return animation.Frames[^1];
    }
    public DuelStage? Sample(double time) => Stages.FirstOrDefault(s => time >= s.Start && time < s.End);
    public static double Progress(DuelStage stage, double time) => Math.Clamp((time - stage.Start) / (stage.End - stage.Start), 0, 1);
    public static double HitPoints(DuelStage stage, double time, bool right)
    {
        double previous = right ? stage.RightHitPointsFrom : stage.LeftHitPointsFrom;
        int target = right ? stage.RightHitPoints : stage.LeftHitPoints;
        double duration = HitPointsDuration(previous, target);
        return duration == 0 ? target : previous + (target - previous) * Math.Clamp((time - (right ? stage.RightHitPointsStart : stage.LeftHitPointsStart)) / duration, 0, 1);
    }
}
