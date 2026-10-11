using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class SceneTransitionTests
{
    [Fact]
    public void SceneReplacementWaitsUnderFullCoverAndRevealsFromItsOwnClock()
    {
        var transition = new SceneReplacementTransition(1, .875);
        transition.Advance(.43);
        Assert.Equal(SceneReplacementPhase.Darkening, transition.Phase);
        Assert.InRange(transition.Opacity, .429f, .431f);
        Assert.Throws<InvalidOperationException>(transition.RevealPreparedScene);
        transition.Advance(10);
        Assert.Equal(SceneReplacementPhase.WaitingForScene, transition.Phase);
        Assert.Equal(1, transition.Opacity);
        transition.Advance(100);
        Assert.Equal(1, transition.Opacity);
        transition.RevealPreparedScene();
        Assert.Equal(1, transition.Opacity);
        transition.Advance(.4375);
        Assert.Equal(.5f, transition.Opacity);
        transition.Advance(10);
        Assert.Equal(SceneReplacementPhase.Complete, transition.Phase);
        Assert.Equal(0, transition.Opacity);
    }

    [Fact]
    public void BattleCoverAndAudioRemainContinuousAcrossTheSceneSwitch()
    {
        const double epsilon = .00001;
        Assert.InRange(Math.Abs(BattleSceneTransition.Opacity(1 - epsilon) - BattleSceneTransition.Opacity(1 + epsilon)), 0, .0001f);
        Assert.Equal(0, BattleSceneTransition.OutgoingGain(1));
        Assert.Equal(0, BattleSceneTransition.IncomingGain(1));
        Assert.Equal(1, BattleSceneTransition.IncomingGain(100));
        Assert.Equal(0, BattleSceneTransition.Opacity(100));
        // Sampling frequency and repeated drawing cannot change the envelope.
        float sampled = BattleSceneTransition.Opacity(1.3);
        for (int i = 0; i < 200; i++) BattleSceneTransition.Opacity(i / 100.0);
        Assert.Equal(sampled, BattleSceneTransition.Opacity(1.3));
    }
}
