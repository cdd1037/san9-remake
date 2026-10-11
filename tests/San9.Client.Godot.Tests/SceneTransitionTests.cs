using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class SceneTransitionTests
{
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
