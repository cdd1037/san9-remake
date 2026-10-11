namespace San9.Rendering;

/// <summary>Continuous entrance envelope sampled from the presentation clock.</summary>
public static class BattleSceneTransition
{
    public const double DarkenDuration = 1;
    public const double RevealDuration = .875;

    public static float Opacity(double elapsed) => elapsed < DarkenDuration
        ? SceneOpacity.Darken(elapsed, DarkenDuration)
        : SceneOpacity.Reveal(elapsed - DarkenDuration, RevealDuration);

    public static float OutgoingGain(double elapsed) => SceneOpacity.Reveal(elapsed, DarkenDuration);
    public static float IncomingGain(double elapsed) => SceneOpacity.Darken(elapsed - DarkenDuration, RevealDuration);
}
