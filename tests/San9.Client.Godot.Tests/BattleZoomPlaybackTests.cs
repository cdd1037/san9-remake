using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BattleZoomPlaybackTests
{
    [Fact]
    public void SceneRetainsSlowRollingNumbersUntilTheirCompletionSound()
    {
        var definition = Definition(new(BattleZoomActionKind.Melee)) with
        {
            Numbers = new(new("small", 20, 28, 15), new("total", 48, 96, 48), new(733, 144),
                48, 250, "roll", "digit", "complete", "blocked")
        };
        var scene = new BattleZoomPlayback(definition, false, 12345, _ => 0, "hit", "block");
        Assert.True(scene.Duration >= scene.Numbers!.End);
        Assert.All(scene.Sounds, sound => Assert.True(sound.Time < scene.Duration));
        Assert.False(scene.Numbers.IsRolling(scene.Duration));
        Assert.Equal(5, scene.Numbers.SampleTotal(scene.Duration).Count());
    }

    private static BattleZoomDefinition Definition(BattleZoomAction action)
    {
        BattleSprite idle = new("idle", 8, 125), attack = new("fight", 8, 125), advance = new("advance", 8, 125), ranged = new("shoot", 8, 125);
        var body = new BattleZoomBody(idle, attack, advance, ranged)
        {
            Formation = [new(248, 292, 24, 12, 15, 0, 0), new(220, 306, 24, 12, 15, 1, 0)],
            BlockPoints = [new(413, 60), new(520, 113), new(627, 166)]
        };
        return new(body, body with { Formation = [new(464, 184, 24, 12, 15, 0, 0)] },
            new("hit", 8, 100), new("block", 8, 100), "left", "right", [])
        {
            Action = action,
            Wave = new("wave", 8, 125),
            Projectile = new("arrow", 16, 64),
            AdvanceSound = "advance",
            AttackSound = "attack",
            AmbienceSound = "ambience",
            DamageBands = [new(2501, 2), new(4500, 3), new(6500, 4), new(9000, 5), new(12500, 6), new(20000, 7), new(null, 8)]
        };
    }

    [Fact]
    public void MeleeStagesFollowersAfterTheFrontAndRetainsTheirResolvedPositions()
    {
        var definition = Definition(new(BattleZoomActionKind.Melee)
        { TravelX = 168, TravelY = -84, MinimumStep = 12, MaximumStep = 12, FrontFirst = true });
        var scene = new BattleZoomPlayback(definition, false, 500, _ => 0, "hit", "block");
        Assert.Equal(30, scene.Source.Length);
        Assert.All(scene.Source.Where(m => m.Group == 1), m => Assert.Equal(scene.Source[0].Arrival, m.AdvanceStart));
        var halfway = scene.SampleBodies(scene.Source[0].Arrival / 2).ToArray();
        Assert.Equal("advance", halfway[0].Sprite.Asset);
        Assert.Equal("idle", halfway[15].Sprite.Asset);
        var finished = scene.SampleBodies(scene.Duration).ToArray();
        Assert.Equal(248 + 168, finished[0].X); Assert.Equal(292 - 84, finished[0].Y);
        Assert.Equal("idle", finished[0].Sprite.Asset);
        Assert.Equal(finished, scene.SampleBodies(scene.Duration));
        Assert.True(scene.ActionEnd >= scene.FightStart + 6);
        Assert.True(scene.FeedbackStart >= scene.ActionEnd);
    }

    [Fact]
    public void VolleyCompletesEveryRoundAndEveryProjectileBeforeFeedback()
    {
        var scene = new BattleZoomPlayback(Definition(new(BattleZoomActionKind.Volley)), false, 500, _ => 0, "hit", "block");
        Assert.Equal(120, scene.Shots.Length);
        Assert.Equal(4, scene.Shots.Select(s => s.Time).Distinct().Count());
        Assert.All(scene.Source, m => Assert.Equal(0, m.Arrival));
        Assert.All(scene.Projectiles, p => Assert.True(p.End <= scene.ActionEnd));
        Assert.Equal(4, scene.Sounds.Count(s => s.Role == "attack"));
        Assert.Equal("shoot", scene.SampleBodies(scene.Shots[0].Time + .1).First().Sprite.Asset);
        Assert.Empty(scene.SampleProjectiles(scene.ActionEnd));
        Assert.True(scene.ActionEnd > 7.2);
    }

    [Fact]
    public void SplitVolleyReturnsToAuthoredGroupsAfterTheInitialWholeFormation()
    {
        var definition = Definition(new(BattleZoomActionKind.Volley) { AlternateGroups = true, ProjectilesPerMember = 3 }) with
        { Source = Definition(new(BattleZoomActionKind.Volley)).Source with { Formation = [new(200, 300, 24, 12, 12, 0, 0), new(300, 200, 24, 12, 12, 1, 0), new(400, 100, 24, 12, 12, 2, 0)] } };
        var scene = new BattleZoomPlayback(definition, false, 500, _ => 0, "hit", "block");
        Assert.Equal(72, scene.Shots.Length);
        Assert.Equal(12, scene.Shots.Count(s => scene.Source[s.Member].Group == 2));
        Assert.True(scene.Projectiles.Length > scene.Shots.Length);
    }

    [Theory]
    [InlineData(6, 125)]
    [InlineData(12, 64)]
    public void AssaultWavesUseTheirOwnPositionsAndFinishBeforeFeedback(int count, int interval)
    {
        var definition = Definition(new(BattleZoomActionKind.AssaultWave) { Rounds = count, MovementFrameMilliseconds = interval }) with
        { Source = Definition(new(BattleZoomActionKind.AssaultWave)).Source with { Formation = [] } };
        var scene = new BattleZoomPlayback(definition, false, 500, _ => 0, "hit", "block");
        Assert.Empty(scene.Source); Assert.Empty(scene.Shots);
        Assert.Contains(scene.SampleBodies(.1), f => f.Sprite.Asset == "wave" && f.X < definition.Action.WaveStartX);
        Assert.DoesNotContain(scene.SampleBodies(scene.ActionEnd), f => f.Sprite.Asset == "wave");
        Assert.All(scene.Impacts, p => Assert.True(p.Time > scene.ActionEnd));
    }

    [Theory]
    [InlineData(6, 125, 2, 30, 20)]
    [InlineData(12, 64, 3, 45, 30)]
    public void MovingArcherWavesEmitFromTheirCurrentPositionAndKeepArrowsUntilArrival(int waves, int milliseconds, int arrows, int delay, int delayStep)
    {
        var action = new BattleZoomAction(BattleZoomActionKind.AssaultWave)
        {
            Rounds = waves,
            MovementFrameMilliseconds = milliseconds,
            WaveEmission = new([[new(43, 19), new(65, 47)], [new(79, 13), new(24, 50)]], arrows, 2, delay, delayStep)
        };
        var definition = Definition(action) with { Source = Definition(action).Source with { Formation = [] } };
        var scene = new BattleZoomPlayback(definition, false, 500, _ => 0, "hit", "block");
        Assert.Empty(scene.Source);
        Assert.NotEmpty(scene.Projectiles);
        var first = scene.Projectiles[0];
        double movementAge = first.Start - delay / 1000.0;
        var wave = Assert.Single(scene.SampleBodies(movementAge), f => f.Sprite.Asset == "wave");
        Assert.Equal(wave.X + 79, first.X, 6);
        Assert.Equal(wave.Y + 13, first.Y, 6);
        Assert.Equal(2 * arrows, scene.Projectiles.Count(p => p.Start == first.Start));
        Assert.Contains(scene.SampleProjectiles((first.Start + first.End) / 2), f => f.Sprite.Asset == "arrow");
        Assert.All(scene.Projectiles, p => Assert.True(p.End <= scene.FeedbackStart));
        Assert.Empty(scene.SampleProjectiles(scene.ActionEnd));
        Assert.Equal(scene.SampleProjectiles(first.Start + .01), scene.SampleProjectiles(first.Start + .01));
    }

    [Theory]
    [InlineData(2500u, 2)]
    [InlineData(2501u, 3)]
    [InlineData(20000u, 8)]
    public void BurstFeedbackConservesActualLossAndKeepsIndependentLocations(uint loss, int bursts)
    {
        var scene = new BattleZoomPlayback(Definition(new(BattleZoomActionKind.Volley)), false, loss, _ => 0, "hit", "block");
        Assert.Equal(bursts, scene.Impacts.Length);
        Assert.Equal((long)loss, scene.Impacts.Sum(p => (long)p.Amount));
        Assert.All(scene.Impacts, p => Assert.True(p.Centered));
        Assert.All(scene.Impacts.Zip(scene.Impacts.Skip(1)), pair => Assert.Equal(.25, pair.Second.Time - pair.First.Time, 6));
    }

    [Fact]
    public void OverkillKeepsCalculatedBurstCountWhileDisplayingOnlyActualLoss()
    {
        var scene = new BattleZoomPlayback(Definition(new(BattleZoomActionKind.Volley)), false, 300, _ => 0,
            "hit", "block", calculatedDamage: 9942);
        Assert.Equal(6, scene.Impacts.Length);
        Assert.Equal(300L, scene.Impacts.Sum(p => (long)p.Amount));
    }

    [Fact]
    public void BlockedFeedbackUsesPreparedPositionsAndNeverInventsSoldierLoss()
    {
        var scene = new BattleZoomPlayback(Definition(new(BattleZoomActionKind.Volley)), true, 0, _ => 0, "hit", "block");
        Assert.Equal(3, scene.Impacts.Length);
        Assert.All(scene.Impacts, p => { Assert.False(p.Centered); Assert.Equal(0u, p.Amount); });
        Assert.Equal(3, scene.Sounds.Count(s => s.Role == "block"));
        Assert.DoesNotContain(scene.Sounds, s => s.Role == "hit");
    }

    [Fact]
    public void DamageFadesTargetsWhileBlockPreservesTheFormation()
    {
        var definition = Definition(new(BattleZoomActionKind.Melee) { TravelX = 168, TravelY = -84, MinimumStep = 12, MaximumStep = 12 });
        var hit = new BattleZoomPlayback(definition, false, 500, _ => 0, "hit", "block");
        var block = new BattleZoomPlayback(definition, true, 0, _ => 0, "hit", "block");
        Assert.Equal(hit.Source.Length, hit.SampleBodies(hit.Duration).Count());
        Assert.Equal(block.Source.Length + block.Target.Length, block.SampleBodies(block.Duration).Count());
        Assert.Equal(block.SampleBodies(block.Duration), block.SampleBodies(block.Duration));
    }

    [Fact]
    public void PreparedLocalEffectsKeepTheirPhaseAndNativeOpacityWithoutSamplingRandomness()
    {
        var definition = Definition(new(BattleZoomActionKind.Melee) { TravelX = 168, TravelY = -84, MinimumStep = 12, MaximumStep = 12 });
        var smoke = new BattleLocalEffect(new("smoke", 8, 100) { Opacity = 166 / 256f }, [new(5, -66)], 0, false, true)
        { Arrival = true, LifetimeFrames = 21, FadePerFrame = 8, Step = new(0, -2), Sounds = ["fire"] };
        definition = definition with { Source = definition.Source with { Effects = [smoke] } };
        int calls = 0;
        var scene = new BattleZoomPlayback(definition, false, 500, _ => { calls++; return 0; }, "hit", "block");
        int prepared = calls;
        var cue = scene.LocalEffects.First();
        var first = scene.SampleLocalEffects(cue.Start).First();
        var later = scene.SampleLocalEffects(cue.Start + .201).First();
        Assert.Equal(166 / 256f, first.Opacity * first.Sprite.Opacity);
        Assert.Equal(150 / 256f, later.Opacity * later.Sprite.Opacity, 6);
        Assert.Equal(first.Y - 4, later.Y);
        Assert.Contains(scene.Sounds, s => s.Time == cue.Start && s.Role == "fire");
        Assert.Equal(scene.SampleLocalEffects(cue.Start + .201), scene.SampleLocalEffects(cue.Start + .201));
        Assert.Equal(prepared, calls);
    }

    [Fact]
    public void FinalChargeFinishesAtItsDeclaredOffsetAndLeavesNoTrailingActors()
    {
        var definition = Definition(new(BattleZoomActionKind.Melee) { TravelX = 168, TravelY = -84, MinimumStep = 12, MaximumStep = 12 });
        definition = definition with { Source = definition.Source with { Motion = new([], new(0, 0), 125) { ExitTravel = new(98, -49), ExitSpeed = 15, ExitFrameMilliseconds = 32 } } };
        var scene = new BattleZoomPlayback(definition, true, 0, _ => 0, "hit", "block");
        var frames = scene.SampleBodies(scene.Duration).Take(scene.Source.Length).ToArray();
        Assert.Equal(scene.Source.Length, frames.Length);
        for (int i = 0; i < frames.Length; i++)
        {
            Assert.Equal(scene.Source[i].X + 168 + 98, frames[i].X);
            Assert.Equal(scene.Source[i].Y - 84 - 49, frames[i].Y);
            Assert.Equal("idle", frames[i].Sprite.Asset);
        }
    }

    [Theory]
    [InlineData(100, 0, 3)]
    [InlineData(-100, 0, 11)]
    [InlineData(0, 100, 7)]
    [InlineData(0, -100, 15)]
    public void ProjectileArtworkSectorsMatchCardinalDirections(double dx, double dy, int direction)
        => Assert.Equal(direction, BattleProjectilePath.Direction(dx, dy));

    [Fact]
    public void ProjectileCurvesReachBothEndpointsAndKeepPreparedDirections()
    {
        var curve = BattleProjectilePath.Prepare(100, 200, 500, 100, 12, false, _ => 0);
        Assert.Equal((100d, 200d), (curve[0].X, curve[0].Y));
        Assert.Equal((500d, 100d), (curve[^1].X, curve[^1].Y));
        Assert.True(curve[6].Y < 150);
        Assert.NotEqual(curve[0].Direction, curve[^1].Direction);
        Assert.All(curve, f => Assert.InRange(f.Direction, 0, 15));
    }

    [Fact]
    public void MovingWavesRetainFadingFramesAtTheirEarlierPositions()
    {
        var definition = Definition(new(BattleZoomActionKind.AssaultWave) { Rounds = 1 }) with
        { Source = Definition(new(BattleZoomActionKind.AssaultWave)).Source with { Formation = [], Motion = new([], new(0, 0), 125, TrailFrames: 4, TrailOpacity: 96 / 256f) } };
        var scene = new BattleZoomPlayback(definition, true, 0, _ => 0, "hit", "block");
        var frames = scene.SampleBodies(.5).Where(f => f.Sprite.Asset == "wave").ToArray();
        var main = Assert.Single(frames, f => f.Depth is null);
        Assert.Equal(3, frames.Count(f => f.Depth is not null));
        Assert.All(frames.Where(f => f.Depth is not null), f =>
        { Assert.True(f.X > main.X); Assert.InRange(f.Opacity, 0, 96 / 256f); });
        Assert.DoesNotContain(scene.SampleBodies(scene.Duration), f => f.Sprite.Asset == "wave");
    }
}
