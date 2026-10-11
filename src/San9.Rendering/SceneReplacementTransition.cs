namespace San9.Rendering;

public enum SceneReplacementPhase { Darkening, WaitingForScene, Revealing, Complete }

/// <summary>Replacement may occur only under full cover; loading time is independent of the reveal.</summary>
public sealed class SceneReplacementTransition(double darkenDuration, double revealDuration)
{
    public SceneReplacementPhase Phase { get; private set; } = SceneReplacementPhase.Darkening;
    private double _elapsed;
    public float Opacity => Phase switch
    {
        SceneReplacementPhase.Darkening => SceneOpacity.Darken(_elapsed, darkenDuration),
        SceneReplacementPhase.WaitingForScene => 1,
        SceneReplacementPhase.Revealing => SceneOpacity.Reveal(_elapsed, revealDuration),
        _ => 0
    };
    public void Advance(double seconds)
    {
        _elapsed += Math.Max(0, seconds);
        if (Phase == SceneReplacementPhase.Darkening && _elapsed >= darkenDuration)
            Phase = SceneReplacementPhase.WaitingForScene;
        else if (Phase == SceneReplacementPhase.Revealing && _elapsed >= revealDuration)
            Phase = SceneReplacementPhase.Complete;
    }
    public void RevealPreparedScene()
    {
        if (Phase != SceneReplacementPhase.WaitingForScene)
            throw new InvalidOperationException("Scene replacement requires full cover.");
        Phase = SceneReplacementPhase.Revealing; _elapsed = 0;
    }
}
