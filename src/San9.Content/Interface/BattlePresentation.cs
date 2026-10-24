using San9.Content.Images;
using San9.Core.Domain;
using San9.Content.Text;

namespace San9.Content.Interface;

public sealed record BattleSprite(string Asset, int Columns, int FrameMilliseconds, int OffsetY = 0,
    bool Additive = false, string? Sound = null)
{
    public int OffsetX { get; init; }
    public bool Centered { get; init; } = true;
    public float Opacity { get; init; } = 1;
}
public sealed record BattleFormationLine(int X, int Y, int StepX, int StepY, int Count, int Group, int Jitter = 8);
public sealed record BattlePoint(int X, int Y);
public sealed record BattleMapNumberDefinition(string Asset, int GlyphWidth, int GlyphHeight,
    int FrameMilliseconds, int Steps, int StepY);
public sealed record BattleNumberSheet(string Asset, int Width, int Height, int Advance);
public sealed record BattleZoomNumbers(BattleNumberSheet Small, BattleNumberSheet Total, BattlePoint Position,
    int SmallFrameMilliseconds, int RollFrameMilliseconds, string RollSound, string DigitSound,
    string CompletedSound, string BlockedSound);
public sealed record BattleOfficerDisplay(BattleSprite Idle, BattlePoint[] Positions,
    BattlePoint ExtensionStep, BattlePoint FlagOffset, int FlagDirection);
public sealed record BattleDamageBand(uint? MaximumExclusive, int Bursts);
public sealed record BattleParticleStyle(float Opacity, int FadePerFrame);
public sealed record BattleLocalEffect(BattleSprite Sprite, BattlePoint[] Origins, int Chance,
    bool Moving, bool Fighting, int TriggerFrame = -1, int Jitter = 0)
{
    public bool RotatedCoverage { get; init; }
    public BattleParticleStyle[] Styles { get; init; } = [];
    public int LifetimeFrames { get; init; }
    public int FadePerFrame { get; init; }
    public BattlePoint Step { get; init; } = new(0, 0);
    public string[] Sounds { get; init; } = [];
    public bool Arrival { get; init; }
}
public sealed record BattleBodyMotion(BattlePoint[] FightingSteps, BattlePoint DamageStep, int DamageFrameMilliseconds,
    int FightingFrameMilliseconds = 0, int TrailFrames = 0, float TrailOpacity = 0)
{
    public BattlePoint[] ReactingSteps { get; init; } = [];
    public BattlePoint ExitTravel { get; init; } = new(0, 0);
    public int ExitSpeed { get; init; } = 1;
    public int ExitFrameMilliseconds { get; init; } = 125;
}
public enum BattleZoomActionKind { Melee, Volley, AssaultWave, ContinuousVolley }
public sealed record BattleWaveEmission(BattlePoint[][] Origins, int ProjectilesPerOrigin,
    int FrameInterval, int MinimumDelayMilliseconds, int DelayStepMilliseconds);
public sealed record BattleZoomAction(BattleZoomActionKind Kind)
{
    public int TravelX { get; init; }
    public int TravelY { get; init; }
    public int MinimumStep { get; init; } = 1;
    public int MaximumStep { get; init; } = 1;
    public int MovementFrameMilliseconds { get; init; } = 125;
    public bool FrontFirst { get; init; }
    public int FightMilliseconds { get; init; } = 6000;
    public int Rounds { get; init; } = 4;
    public int IntervalMilliseconds { get; init; } = 1100;
    public bool AlternateGroups { get; init; }
    public int RecoveryMilliseconds { get; init; } = 2200;
    public int ProjectilesPerMember { get; init; } = 1;
    public int ProjectileMultiplier { get; init; } = 1;
    public int WaveStartX { get; init; } = 560;
    public int WaveStartY { get; init; } = 456;
    public int WaveEndX { get; init; } = 160;
    public int WaveEndY { get; init; } = 256;
    public int WaveSpacingX { get; init; } = 488;
    public int WaveStep { get; init; } = 20;
    [System.Text.Json.Serialization.JsonRequired]
    public BattleWaveEmission? WaveEmission { get; init; }
}
public sealed record BattleZoomBody(BattleSprite Idle, BattleSprite? Attack, BattleSprite? Advance, BattleSprite? Ranged)
{
    public string? FeedbackSound { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public BattleBodyMotion Motion { get; init; } = new([], new(0, 0), 125);
    [System.Text.Json.Serialization.JsonRequired]
    public BattleLocalEffect[] Effects { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public BattleLocalEffect[] DamageEffects { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public BattleSprite? Wake { get; init; }
    public BattlePoint WakeOffset { get; init; } = new(0, 0);
    public string? AdvanceSound { get; init; }
    public string? AttackSound { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public BattleFormationLine[] Formation { get; init; } = [];
    public int ImpactX { get; init; } = 28;
    public int ImpactY { get; init; } = 44;
    public int Projectiles { get; init; } = 1;
    public int ProjectileStartFrame { get; init; } = 3;
    [System.Text.Json.Serialization.JsonRequired]
    public double ProjectileArcHeight { get; init; } = 1;
    [System.Text.Json.Serialization.JsonRequired]
    public BattlePoint[] BlockPoints { get; init; } = [];
}
public sealed record BattleZoomDefinition(BattleZoomBody Source, BattleZoomBody Target,
    BattleSprite Impact, BattleSprite Block, string SourcePanel, string TargetPanel, BattleSprite[] DoubleEffect)
{
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<int, BattleZoomBody> CountrySources { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public BattleZoomBody? ImperialSource { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public BattleLocalEffect[] WaveEffects { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public string? FeedbackSound { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public BattleZoomNumbers? Numbers { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<int, BattlePoint> FacilityNumberPositions { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public BattleOfficerDisplay? Officers { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<int, BattleOfficerDisplay> FacilityOfficers { get; init; } = [];
    public BattleSprite? Projectile { get; init; }
    public BattleSprite? Wave { get; init; }
    public string AdvanceSound { get; init; } = null!;
    public string AttackSound { get; init; } = null!;
    public string AmbienceSound { get; init; } = null!;
    public Dictionary<int, BattleZoomBody> SourceFormations { get; init; } = [];
    public Dictionary<int, BattleZoomBody> TargetFormations { get; init; } = [];
    public Dictionary<int, BattleZoomBody> FacilitySources { get; init; } = [];
    public Dictionary<int, BattleZoomBody> FacilityTargets { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public BattleZoomAction Action { get; init; } = new(BattleZoomActionKind.Melee);
    [System.Text.Json.Serialization.JsonRequired]
    public BattleDamageBand[] DamageBands { get; init; } = [];
    public int FeedbackIntervalMilliseconds { get; init; } = 250;
}
public sealed record TacticPresentationDefinition(Dictionary<string, DecisionMessageDefinition> Messages,
    BattleSprite? MapEffect, BattleZoomDefinition? Zoom)
{
    public bool Support { get; init; }
    public bool EffectAtSource { get; init; }
    public bool UnitedResponse { get; init; }
    public bool FormationResponse { get; init; }
    public string? CounterResponse { get; init; }
    public BattleSprite[] Prelude { get; init; } = [];
}

/// <summary>Author supplied message and resource roles; executable resource numbers are never consumed by the client.</summary>
public sealed record BattlePresentationDefinition(Dictionary<string, DecisionMessageDefinition> Messages,
    Dictionary<int, TacticPresentationDefinition> Tactics, string HitSound, string BlockSound)
{
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<UnitCondition, BattleSprite> ConditionEffects { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public BattleMapNumberDefinition? MapNumbers { get; init; }
    public BattleSprite[] SnipingEffect { get; init; } = [];
    public Dictionary<string, Dictionary<int, BattleSprite[]>> BuildingEffects { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public BattleSceneDefinition Scene { get; init; } = new([], [], []);
    [System.Text.Json.Serialization.JsonRequired]
    public DuelPresentationDefinition? Duel { get; init; }
    public void Validate(TextCatalog texts, IReadOnlyDictionary<string, string> sounds,
        IEnumerable<int> people, IEnumerable<int> tactics, IEnumerable<ImageAsset> images)
    {
        var catalog = images.ToDictionary(a => a.Id);
        if (Duel is null) throw new InvalidDataException("Missing duel presentation.");
        Duel.Validate(texts, sounds, people, catalog);
        string[] required = ["activation-own","activation-enemy","activation-other","chain-own","chain-enemy","support","link","blocked","damage",
            "defection","healed","hit-reaction","block-reaction","block-reply","chain-confused","chain-unformed","link-0","link-1","link-2","link-3",
            "sniping-action","sniping-fatal","sniping-heavy","sniping-light","sniping-own","sniping-enemy"];
        if (required.Except(Messages.Keys).Any()) throw new InvalidDataException("Incomplete battle message roles.");
        if (Tactics.Keys.Except(tactics).Any() || !sounds.ContainsKey(HitSound) || !sounds.ContainsKey(BlockSound))
            throw new InvalidDataException("Invalid battle presentation references.");
        string[] roles = ["actor", "source-commander", "target-commander", "responder", "soldier", "source", "target", "tactic", "force", "target-force", "damage", "injury"];
        var definitions = Messages.Values.Concat(Tactics.Values.SelectMany(t => t.Messages.Values)).ToArray();
        if (definitions.Any(m => m.Bindings.Values.Except(roles).Any() || m.Speaker is { } speaker && !roles.Contains(speaker)))
            throw new InvalidDataException("Unknown battle message role.");
        new DecisionPresentations([], []) { Shared = definitions.Select((d, i) => (d, i)).ToDictionary(p => p.i.ToString(), p => p.d) }
            .Validate(texts, sounds, people.ToHashSet());
        void Image(string id) { if (!catalog.ContainsKey(id)) throw new InvalidDataException("Missing battle image: " + id); }
        void Sprite(BattleSprite? sprite)
        {
            if (sprite is null) return;
            Image(sprite.Asset);
            if (sprite.Columns <= 0 || sprite.FrameMilliseconds <= 0 || sprite.Opacity is <= 0 or > 1 || catalog[sprite.Asset].Width % sprite.Columns != 0 ||
                sprite.Sound is { } sound && !sounds.ContainsKey(sound)) throw new InvalidDataException("Invalid battle sprite: " + sprite.Asset);
        }
        void Body(BattleZoomBody body)
        {
            if (body.Idle is null || new[] { body.AdvanceSound, body.AttackSound, body.FeedbackSound }.OfType<string>().Any(s => !sounds.ContainsKey(s)))
                throw new InvalidDataException("Invalid battle body resource.");
            Sprite(body.Idle); Sprite(body.Attack); Sprite(body.Advance); Sprite(body.Ranged);
            Sprite(body.Wake);
            if (body.Motion.DamageFrameMilliseconds <= 0 || body.Motion.FightingFrameMilliseconds < 0 ||
                body.Motion.TrailFrames < 0 || body.Motion.TrailOpacity is < 0 or > 1 || body.Motion.ExitSpeed <= 0 || body.Motion.ExitFrameMilliseconds <= 0)
                throw new InvalidDataException("Invalid battle body motion.");
            foreach (var effect in body.Effects.Concat(body.DamageEffects)) LocalEffect(effect);
            if (body.Formation.Any(line => line.Count <= 0 || line.Group < 0 || line.Jitter < 0) ||
                body.Projectiles <= 0 || body.ProjectileStartFrame is < 0 or > 5 ||
                !double.IsFinite(body.ProjectileArcHeight) || body.ProjectileArcHeight < 0 || body.BlockPoints.Length == 0)
                throw new InvalidDataException("Invalid battle body layout.");
        }
        void LocalEffect(BattleLocalEffect effect)
        {
            Sprite(effect.Sprite);
            if (effect.Origins.Length == 0 || effect.Chance is < 0 or > 100 || effect.TriggerFrame < -1 ||
                effect.RotatedCoverage && effect.Origins.Length is < 2 or > 30 || effect.Styles.Any(s => s.Opacity is <= 0 or > 1 || s.FadePerFrame < 0) ||
                effect.Jitter < 0 || effect.LifetimeFrames < 0 || effect.FadePerFrame < 0 || effect.Sounds.Any(s => !sounds.ContainsKey(s)))
                throw new InvalidDataException("Invalid battle local effect.");
        }
        foreach (var (condition, sprite) in ConditionEffects)
        {
            if (!Enum.IsDefined(condition) || condition is UnitCondition.Normal or UnitCondition.Engaged)
                throw new InvalidDataException("Invalid condition effect.");
            Sprite(sprite);
        }
        if (MapNumbers is { } numbers)
        {
            Image(numbers.Asset);
            if (numbers.GlyphWidth <= 0 || numbers.GlyphHeight <= 0 || numbers.FrameMilliseconds <= 0 ||
                numbers.Steps <= 0 || numbers.StepY <= 0 || catalog[numbers.Asset].Width < numbers.GlyphWidth * 12 ||
                catalog[numbers.Asset].Height < numbers.GlyphHeight * 2)
                throw new InvalidDataException("Invalid map number sheet.");
        }
        foreach (var tactic in Tactics.Values)
        {
            string[] tacticRoles = tactic.Zoom is not null ? ["action"] : tactic.Support ? ["announcement", "action", "hit"] : ["announcement", "action", "hit", "reduced", "failed"];
            if (tacticRoles.Except(tactic.Messages.Keys).Any() ||
                tactic.UnitedResponse && !tactic.Messages.ContainsKey("united") ||
                tactic.FormationResponse && !tactic.Messages.ContainsKey("defensive") ||
                tactic.CounterResponse is { } counter && !tactic.Messages.ContainsKey(counter))
                throw new InvalidDataException("Incomplete tactic message roles.");
            Sprite(tactic.MapEffect);
            foreach (var sprite in tactic.Prelude) Sprite(sprite);
            if (tactic.Zoom is not { } zoom) continue;
            Image(zoom.SourcePanel); Image(zoom.TargetPanel);
            if (zoom.Numbers is { } digits)
            {
                foreach (var sheet in new[] { digits.Small, digits.Total })
                {
                    Image(sheet.Asset);
                    if (sheet.Width <= 0 || sheet.Height <= 0 || sheet.Advance <= 0 ||
                        catalog[sheet.Asset].Width < sheet.Width || catalog[sheet.Asset].Height < sheet.Height * 10)
                        throw new InvalidDataException("Invalid battle number sheet.");
                }
                if (digits.SmallFrameMilliseconds <= 0 || digits.RollFrameMilliseconds <= 0 ||
                    new[] { digits.RollSound, digits.DigitSound, digits.CompletedSound, digits.BlockedSound }.Any(s => !sounds.ContainsKey(s)))
                    throw new InvalidDataException("Invalid battle number animation.");
            }
            foreach (var display in zoom.FacilityOfficers.Values.Concat(zoom.Officers is { } officers ? [officers] : Array.Empty<BattleOfficerDisplay>()))
            {
                Sprite(display.Idle);
                if (display.Positions.Length < 2 || display.FlagDirection is < 0 or > 7 ||
                    display.ExtensionStep == new BattlePoint(0, 0))
                    throw new InvalidDataException("Invalid battle officer layout.");
            }
            Body(zoom.Source); Body(zoom.Target); Sprite(zoom.Impact); Sprite(zoom.Block);
            Sprite(zoom.Projectile); Sprite(zoom.Wave);
            foreach (var body in zoom.SourceFormations.Values.Concat(zoom.TargetFormations.Values)) Body(body);
            foreach (var body in zoom.FacilitySources.Values.Concat(zoom.FacilityTargets.Values)) Body(body);
            foreach (var body in zoom.CountrySources.Values.Concat(zoom.ImperialSource is { } imperial ? [imperial] : Array.Empty<BattleZoomBody>())) Body(body);
            foreach (var effect in zoom.WaveEffects) LocalEffect(effect);
            if (zoom.FeedbackSound is { } feedback && !sounds.ContainsKey(feedback)) throw new InvalidDataException("Invalid battle feedback sound.");
            var action = zoom.Action;
            if (!Enum.IsDefined(action.Kind) || action.MinimumStep <= 0 || action.MaximumStep < action.MinimumStep ||
                action.MovementFrameMilliseconds <= 0 || action.FightMilliseconds <= 0 || action.Rounds <= 0 ||
                action.IntervalMilliseconds <= 0 || action.RecoveryMilliseconds < 0 || action.ProjectilesPerMember <= 0 ||
                action.ProjectileMultiplier <= 0 || action.WaveStep <= 0 ||
                action.Kind == BattleZoomActionKind.AssaultWave && zoom.Wave is null ||
                zoom.FeedbackIntervalMilliseconds <= 0 || zoom.DamageBands.Length == 0 ||
                zoom.DamageBands.Any(b => b.Bursts <= 0) || zoom.DamageBands[^1].MaximumExclusive is not null ||
                zoom.DamageBands.Take(zoom.DamageBands.Length - 1).Any(b => b.MaximumExclusive is null) ||
                !zoom.DamageBands.Where(b => b.MaximumExclusive is not null).Select(b => b.MaximumExclusive!.Value).SequenceEqual(
                    zoom.DamageBands.Where(b => b.MaximumExclusive is not null).Select(b => b.MaximumExclusive!.Value).Distinct().Order()))
                throw new InvalidDataException("Invalid battle action or damage sequence.");
            if (action.WaveEmission is { } emission && (action.Kind != BattleZoomActionKind.AssaultWave ||
                zoom.Projectile is null || emission.Origins is null || emission.Origins.Length == 0 ||
                emission.Origins.Any(phase => phase is null || phase.Length == 0) ||
                emission.ProjectilesPerOrigin <= 0 || emission.FrameInterval <= 0 ||
                emission.MinimumDelayMilliseconds < 0 || emission.DelayStepMilliseconds < 0))
                throw new InvalidDataException("Invalid wave projectile emission.");
            if (new[] { zoom.AdvanceSound, zoom.AttackSound, zoom.AmbienceSound }.Any(s => s is null || !sounds.ContainsKey(s)))
                throw new InvalidDataException("Missing battle zoom sound.");
            foreach (var effect in zoom.DoubleEffect) Sprite(effect);
        }
        foreach (var sprite in BuildingEffects.Values.SelectMany(types => types.Values).SelectMany(s => s)) Sprite(sprite);
        foreach (var sprite in SnipingEffect) Sprite(sprite);
        if (Scene.Terrains.Length == 0 || Scene.Terrains.Any(t => t.MinimumElevation > t.MaximumElevation ||
            t.Terrains.Length == 0 || t.Season.Length == 0 || t.TransitionKind.Length == 0 || t.TextureVariants?.Length == 0) ||
            Scene.Transitions.Select(t => (t.SourceKind, t.TargetKind)).Distinct().Count() != Scene.Transitions.Length)
            throw new InvalidDataException("Invalid battle scene terrain rules.");
        foreach (var layer in Scene.Terrains.SelectMany(t => new[] { t.Source, t.Target, t.SourceEdge, t.TargetEdge }).OfType<BattleSceneLayer>()
            .Concat(Scene.Transitions.Select(t => t.Layer)).Concat(Scene.Facilities.Values.SelectMany(f => f.Source.Concat(f.Target)))) Image(layer.Asset);
    }
}
