using System.Collections.Immutable;
using San9.Content.Interface;

namespace San9.Rendering;

public sealed record BattleZoomMember(int X, int Y, int Group, int Band, int Frame, double AdvanceStart, double Arrival);
public sealed record BattleZoomShot(double Time, int Member);
public sealed record BattleZoomProjectile(double Start, double End, double X, double Y, double TargetX, double TargetY)
{
    public ImmutableArray<BattleProjectileFrame> Path { get; init; } = [];
}
public sealed record BattleZoomLocalCue(double Start, double End, BattleLocalEffect Effect, double X, double Y, double Depth);
public sealed record BattleZoomImpact(double Time, int X, int Y, uint Amount, bool Centered);
public sealed record BattleZoomSound(double Time, string Role);
public sealed record BattleZoomFrame(BattleSprite Sprite, double X, double Y, int Frame, float Opacity = 1, bool Centered = false)
{
    public double? Depth { get; init; }
}

/// <summary>Prepared formation and action timeline. Sampling never chooses randomness or advances the world.</summary>
public sealed class BattleZoomPlayback
{
    public BattleZoomDefinition Definition { get; }
    public ImmutableArray<BattleZoomMember> Source { get; }
    public ImmutableArray<BattleZoomMember> Target { get; }
    public ImmutableArray<BattleZoomShot> Shots { get; }
    public ImmutableArray<BattleZoomProjectile> Projectiles { get; }
    public ImmutableArray<BattleZoomImpact> Impacts { get; }
    public ImmutableArray<BattleZoomSound> Sounds { get; }
    public double FightStart { get; }
    public double ActionEnd { get; }
    public double FeedbackStart { get; }
    public BattleZoomNumberPlayback? Numbers { get; }
    public double Duration { get; }
    private readonly ImmutableArray<double> _waves;
    private readonly ImmutableArray<int> _combatPhases;
    private readonly ImmutableArray<double> _damageStarts;
    private readonly ImmutableArray<(double Start, double End)> _continuousFire = [];
    public ImmutableArray<BattleZoomLocalCue> LocalEffects { get; }
    public double FightStop { get; }

    public BattleZoomPlayback(BattleZoomDefinition definition, bool blocked, uint damage, Func<int, int> next,
        string hitSound, string blockSound, double interveningDuration = 0, uint? calculatedDamage = null)
    {
        Definition = definition;
        var action = definition.Action;
        ImmutableArray<BattleZoomMember> Members(BattleZoomBody body) => body.Formation.SelectMany(line =>
            Enumerable.Range(0, line.Count).Select(column => new BattleZoomMember(
                line.X + column * line.StepX + (line.Jitter == 0 ? 0 : next(line.Jitter * 2 + 1) - line.Jitter),
                line.Y + column * line.StepY + (line.Jitter == 0 ? 0 : next(line.Jitter * 2 + 1) - line.Jitter),
                line.Group, column / Math.Max(1, (line.Count + 2) / 3), next(body.Idle.Columns), 0, 0))).ToImmutableArray();
        Source = Members(definition.Source); Target = Members(definition.Target);
        _combatPhases = Source.Select(_ => next(3)).ToImmutableArray();
        List<BattleZoomShot> shots = []; List<BattleZoomProjectile> projectiles = [];
        List<BattleZoomSound> sounds = []; List<double> waves = [];
        void Projectile(double start, double x, double y, BattleZoomMember target, BattleSprite sprite)
        {
            double targetX = target.X + definition.Target.ImpactX, targetY = target.Y + definition.Target.ImpactY;
            double dx = targetX - x, dy = targetY - y;
            double travel = Math.Max(4, Math.Floor(Math.Sqrt(dx * dx + 4 * dy * dy) / 32)) * sprite.FrameMilliseconds / 1000.0;
            projectiles.Add(new(start, start + travel, x, y, targetX, targetY)
            { Path = BattleProjectilePath.Prepare(x, y, targetX, targetY, (int)Math.Round(travel * 1000 / sprite.FrameMilliseconds), definition.Source.ProjectileArcHeight, next) });
        }
        if (action.Kind == BattleZoomActionKind.Melee)
        {
            double distance = Math.Sqrt((double)action.TravelX * action.TravelX + (double)action.TravelY * action.TravelY);
            var moving = Source.Select(member => member with
            {
                Arrival = Math.Ceiling(distance / (action.MinimumStep + next(action.MaximumStep - action.MinimumStep + 1))) * action.MovementFrameMilliseconds / 1000
            }).ToArray();
            double frontArrival = moving.Where(m => !action.FrontFirst || m.Group == 0).Select(m => m.Arrival).DefaultIfEmpty(0).Min();
            Source = moving.Select(member => action.FrontFirst && member.Group != 0
                ? member with { AdvanceStart = frontArrival, Arrival = member.Arrival + frontArrival } : member).ToImmutableArray();
            var arrivals = Source.Select(m => m.Arrival).Order().ToArray();
            FightStart = arrivals.Length / 3 == 0 ? 0 : arrivals[arrivals.Length / 3 - 1];
            FightStop = FightStart + action.FightMilliseconds / 1000.0;
            ActionEnd = Source.Select((m, i) => SourceReset(m, i)).DefaultIfEmpty(FightStop).Max();
            sounds.Add(new(0, definition.AdvanceSound)); sounds.Add(new(FightStart, definition.AttackSound));
        }
        else if (action.Kind is BattleZoomActionKind.Volley or BattleZoomActionKind.ContinuousVolley)
        {
            double time = 0;
            bool continuous = action.Kind == BattleZoomActionKind.ContinuousVolley;
            var members = Enumerable.Range(0, Source.Length).ToArray();
            var groups = members.GroupBy(i => Source[i].Group).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
            var targets = Target.ToArray();
            var targetBands = targets.GroupBy(m => m.Band).ToDictionary(g => g.Key, g => g.ToArray());
            var body = definition.Source;
            int count = action.ProjectilesPerMember > 1 ? action.ProjectilesPerMember : body.Projectiles * action.ProjectileMultiplier;
            int batch = action.AlternateGroups ? Math.Max(1, Source.Length / 2) : Math.Max(1, Source.Length);
            for (int round = 0; round < action.Rounds; round++)
            {
                var candidates = members;
                if (action.AlternateGroups && groups.Length > 0 && (continuous || round > 0))
                    candidates = groups[(continuous ? round : round - 1) % groups.Length];
                int[] selected = [.. candidates];
                for (int i = selected.Length - 1; i > 0; i--)
                { int other = next(i + 1); (selected[i], selected[other]) = (selected[other], selected[i]); }
                time += action.IntervalMilliseconds / 1000.0;
                for (int i = 0; i < selected.Length; i++)
                {
                    if (!continuous && i > 0 && i % batch == 0) time += action.MovementFrameMilliseconds / 1000.0;
                    int index = selected[i];
                    shots.Add(new(time, index));
                    if (definition.Projectile is not { } projectile || targets.Length == 0) continue;
                    var member = Source[index];
                    var band = targetBands.GetValueOrDefault(member.Band, targets);
                    for (int arrow = 0; arrow < count; arrow++)
                    {
                        var target = band[next(band.Length)];
                        double x = member.X + body.ImpactX + next(Math.Max(1, body.ImpactX / 2)) - body.ImpactX / 4;
                        double y = member.Y + body.ImpactY + next(Math.Max(1, body.ImpactY / 2)) - body.ImpactY / 4;
                        int emitFrame = body.ProjectileStartFrame;
                        if (next(100) >= 50) { emitFrame++; if (next(100) >= 75) emitFrame++; }
                        double startTime = time + (emitFrame + (continuous ? arrow : 0)) * action.MovementFrameMilliseconds / 1000.0 + (30 + next(3) * 20) / 1000.0;
                        Projectile(startTime, x, y, target, projectile);
                    }
                }
                sounds.Add(new(time + .5, definition.AttackSound));
            }
            FightStart = shots.Select(s => s.Time).DefaultIfEmpty(0).Min();
            ActionEnd = Math.Max(time + action.RecoveryMilliseconds / 1000.0, projectiles.Select(p => p.End).DefaultIfEmpty(0).Max());
            if (body.Ranged is { } ranged)
            {
                double cycle = ranged.Columns * action.MovementFrameMilliseconds / 1000.0;
                var firingWindows = Enumerable.Repeat((Start: double.PositiveInfinity, End: double.NegativeInfinity), Source.Length).ToArray();
                foreach (var firing in shots.GroupBy(s => s.Member))
                {
                    double start = firing.First().Time, end = firing.Last().Time + cycle;
                    if (continuous) end = start + Math.Ceiling((end - start) / cycle - 1e-9) * cycle;
                    firingWindows[firing.Key] = (start, end);
                    ActionEnd = Math.Max(ActionEnd, end);
                }
                if (continuous) _continuousFire = firingWindows.ToImmutableArray();
            }
        }
        else
        {
            double distance = Math.Sqrt(Math.Pow(action.WaveStartX - action.WaveEndX, 2) + Math.Pow(action.WaveStartY - action.WaveEndY, 2));
            double travel = Math.Ceiling(distance / action.WaveStep) * action.MovementFrameMilliseconds / 1000.0;
            double spacing = Math.Ceiling((action.WaveStartX - action.WaveSpacingX) / (action.WaveStartX - (double)action.WaveEndX) * distance / action.WaveStep)
                * action.MovementFrameMilliseconds / 1000.0;
            for (int i = 0; i < action.Rounds; i++) waves.Add(i * spacing);
            if (action.WaveEmission is { } emission && definition.Projectile is { } projectile && Target.Length > 0)
                foreach (double start in waves)
                    for (int frame = emission.FrameInterval; frame * action.MovementFrameMilliseconds / 1000.0 < travel; frame += emission.FrameInterval)
                    {
                        double age = frame * action.MovementFrameMilliseconds / 1000.0;
                        double progress = age / travel;
                        double x = action.WaveStartX + (action.WaveEndX - action.WaveStartX) * progress;
                        double y = action.WaveStartY + (action.WaveEndY - action.WaveStartY) * progress;
                        foreach (var origin in emission.Origins[frame / emission.FrameInterval % emission.Origins.Length])
                            for (int arrow = 0; arrow < emission.ProjectilesPerOrigin; arrow++)
                            {
                                var target = Target[next(Target.Length)];
                                double delay = (emission.MinimumDelayMilliseconds + next(3) * emission.DelayStepMilliseconds) / 1000.0;
                                Projectile(start + age + delay, x + origin.X, y + origin.Y, target, projectile);
                            }
                    }
            ActionEnd = waves[^1] + travel + 8 * action.MovementFrameMilliseconds / 1000.0;
            ActionEnd = Math.Max(ActionEnd, projectiles.Select(p => p.End).DefaultIfEmpty(0).Max());
            sounds.Add(new(0, definition.AdvanceSound)); sounds.Add(new(0, definition.AttackSound));
        }
        Shots = shots.ToImmutableArray(); Projectiles = projectiles.ToImmutableArray(); _waves = waves.ToImmutableArray();
        FeedbackStart = ActionEnd + interveningDuration;
        if (!blocked && (definition.Target.FeedbackSound ?? definition.FeedbackSound) is { } feedbackSound) sounds.Add(new(FeedbackStart, feedbackSound));
        int bursts = blocked ? definition.Target.BlockPoints.Length : definition.DamageBands.First(b => b.MaximumExclusive is null || (calculatedDamage ?? damage) < b.MaximumExclusive).Bursts;
        List<BattleZoomImpact> impacts = [];
        uint[] amounts = new uint[bursts];
        uint left = damage;
        uint average = damage / (uint)bursts;
        for (int i = 0; i < bursts; i++)
        {
            uint amount = i == bursts - 1 ? left : Math.Min(left, average - average / 10 + (uint)next(Math.Max(1, (int)(average / 5))));
            amounts[i] = blocked ? 0 : amount; left -= amount;
        }
        for (int i = amounts.Length - 1; i > 0; i--)
        { int other = next(i + 1); (amounts[i], amounts[other]) = (amounts[other], amounts[i]); }
        for (int i = 0; i < bursts; i++)
        {
            var candidates = Target.Where(member => Math.Abs(member.Band - (bursts - i) % 3) <= 1).ToArray();
            var target = candidates.Length > 0 ? candidates[next(candidates.Length)] : Target.Length > 0 ? Target[next(Target.Length)] : new BattleZoomMember(0, 0, 0, 0, 0, 0, 0);
            var point = blocked ? definition.Target.BlockPoints[i] : new BattlePoint(
                target.X + definition.Target.ImpactX + next(49) - 24, target.Y + definition.Target.ImpactY + next(49) - 24);
            double time = FeedbackStart + (i + 1) * definition.FeedbackIntervalMilliseconds / 1000.0;
            impacts.Add(new(time, point.X, point.Y, amounts[i], !blocked));
            sounds.Add(new(time, blocked ? blockSound : hitSound));
        }
        Impacts = impacts.ToImmutableArray();
        _damageStarts = Target.Select(_ =>
        {
            if (blocked) return double.PositiveInfinity;
            for (double t = FeedbackStart + .125; t < Impacts[^1].Time; t += .125)
                if (next(100) < Math.Min(100, (t - FeedbackStart) * 1000 / (Target.Length + 10))) return t;
            return Impacts[^1].Time;
        }).ToImmutableArray();
        LocalEffects = PrepareLocalEffects(next, sounds);
        if (definition.Numbers is { } numbers)
        {
            Numbers = new(numbers, damage, blocked, Impacts[^1].Time, next);
            sounds.AddRange(Numbers.Sounds);
        }
        Sounds = sounds.OrderBy(s => s.Time).ToImmutableArray();
        Duration = Impacts[^1].Time + Math.Max(definition.Impact.Columns * definition.Impact.FrameMilliseconds,
            definition.Block.Columns * definition.Block.FrameMilliseconds) / 1000.0 + 4;
        Duration = Math.Max(Duration, (Numbers?.End ?? 0) + 4);
        Duration = Math.Max(Duration, LocalEffects.Select(c => c.End).DefaultIfEmpty(0).Max());
        Duration = Math.Max(Duration, FeedbackStart + ExitDuration(definition.Source.Motion));
    }

    public IEnumerable<BattleZoomFrame> SampleProjectiles(double elapsed)
    {
        if (Definition.Projectile is not { } sprite) yield break;
        foreach (var projectile in Projectiles)
        {
            if (elapsed < projectile.Start || elapsed >= projectile.End) continue;
            int index = Math.Clamp((int)((elapsed - projectile.Start) * 1000 / sprite.FrameMilliseconds), 0, projectile.Path.Length - 1);
            var frame = projectile.Path[index];
            yield return new(sprite, frame.X, frame.Y, frame.Direction % sprite.Columns, 204 / 256f, true);
        }
    }

    public IEnumerable<BattleZoomFrame> SampleBodies(double elapsed)
    {
        var action = Definition.Action;
        BattleZoomFrame Frame(BattleSprite sprite, BattleZoomMember member, double x, double y, double age, int milliseconds) =>
            new(sprite, x, y, ((int)(Math.Max(0, age) * 1000 / milliseconds) + member.Frame) % sprite.Columns);
        for (int i = 0; i < Source.Length; i++)
        {
            var member = Source[i]; var body = Definition.Source;
            int? combatFrame = null;
            var sprite = body.Idle; double age = elapsed, x = member.X, y = member.Y; int milliseconds = body.Idle.FrameMilliseconds;
            if (action.Kind == BattleZoomActionKind.Melee)
            {
                double progress = member.Arrival > member.AdvanceStart ? Math.Clamp((elapsed - member.AdvanceStart) / (member.Arrival - member.AdvanceStart), 0, 1) : 1;
                x += action.TravelX * progress; y += action.TravelY * progress;
                double combatStart = SourceCombatStart(member);
                if (elapsed >= member.AdvanceStart && elapsed < combatStart) { sprite = body.Advance ?? body.Idle; age -= member.AdvanceStart; milliseconds = action.MovementFrameMilliseconds; }
                else if (elapsed >= combatStart)
                {
                    double stop = SourceReset(member, i);
                    int combatMilliseconds = body.Motion.FightingFrameMilliseconds > 0 ? body.Motion.FightingFrameMilliseconds : action.MovementFrameMilliseconds;
                    int count = (int)(Math.Max(0, Math.Min(elapsed, stop) - combatStart) * 1000 / combatMilliseconds);
                    var offset = Movement(body.Motion.FightingSteps, count, _combatPhases[i]);
                    x += offset.X; y += offset.Y;
                    if (elapsed < stop) { sprite = body.Attack ?? body.Idle; combatFrame = (_combatPhases[i] + count) % sprite.Columns; milliseconds = combatMilliseconds; }
                }
                if (elapsed >= FeedbackStart && body.Motion.ExitTravel != new BattlePoint(0, 0))
                {
                    var exit = ExitOffset(body.Motion, elapsed - FeedbackStart); x += exit.X; y += exit.Y;
                    double duration = ExitDuration(body.Motion);
                    if (elapsed < FeedbackStart + duration) { sprite = body.Advance ?? body.Idle; combatFrame = null; age = elapsed - FeedbackStart; milliseconds = body.Motion.ExitFrameMilliseconds; }
                }
            }
            else if (action.Kind is BattleZoomActionKind.Volley or BattleZoomActionKind.ContinuousVolley && body.Ranged is { } ranged)
            {
                double? start;
                if (action.Kind == BattleZoomActionKind.ContinuousVolley)
                {
                    var window = _continuousFire[i];
                    start = elapsed >= window.Start && elapsed < window.End ? window.Start : null;
                }
                else start = Shots.LastOrDefault(s => s.Member == i && elapsed >= s.Time && elapsed < s.Time + ranged.Columns * action.MovementFrameMilliseconds / 1000.0)?.Time;
                if (start is { } firingStart)
                {
                    sprite = ranged;
                    age -= firingStart;
                    milliseconds = action.MovementFrameMilliseconds;
                    combatFrame = (int)(age * 1000 / milliseconds) % ranged.Columns;
                }
            }
            var current = Frame(sprite, member, x, y, age, milliseconds);
            yield return combatFrame is int phase ? current with { Frame = phase } : current;
            if (body.Wake is { } wake && sprite != body.Attack && sprite != body.Advance)
                yield return new(wake, x + body.WakeOffset.X, y + body.WakeOffset.Y, (member.Frame + (int)(elapsed * 1000 / wake.FrameMilliseconds)) % wake.Columns) { Depth = y - 1 };
            if (body.Motion.TrailFrames > 0 && action.Kind == BattleZoomActionKind.Melee && elapsed >= member.AdvanceStart && elapsed < SourceReset(member, i))
            {
                double tick = action.MovementFrameMilliseconds / 1000.0;
                for (int trail = 1; trail <= body.Motion.TrailFrames; trail++)
                {
                    double previous = elapsed - trail * tick;
                    if (previous < member.AdvanceStart || previous >= member.Arrival) continue;
                    double p = Math.Clamp((previous - member.AdvanceStart) / (member.Arrival - member.AdvanceStart), 0, 1);
                    yield return new(body.Advance ?? body.Idle, member.X + action.TravelX * p, member.Y + action.TravelY * p,
                        (member.Frame + (int)((previous - member.AdvanceStart) / tick)) % body.Idle.Columns,
                        body.Motion.TrailOpacity * (1 - trail / (float)(body.Motion.TrailFrames)))
                    { Depth = member.Y + action.TravelY * p - 1 };
                }
            }
            if (body.Motion.TrailFrames > 0 && elapsed >= FeedbackStart && body.Motion.ExitTravel != new BattlePoint(0, 0))
            {
                double tick = body.Motion.ExitFrameMilliseconds / 1000.0;
                for (int trail = 1; trail <= body.Motion.TrailFrames; trail++)
                {
                    double previous = elapsed - trail * tick;
                    if (previous < FeedbackStart || previous >= FeedbackStart + ExitDuration(body.Motion)) continue;
                    var position = SourcePosition(i, previous);
                    yield return new(body.Advance ?? body.Idle, position.X, position.Y,
                        (member.Frame + (int)((previous - FeedbackStart) / tick)) % body.Idle.Columns,
                        body.Motion.TrailOpacity * (1 - trail / (float)(body.Motion.TrailFrames)))
                    { Depth = position.Y - 1 };
                }
            }
        }
        for (int i = 0; i < Target.Length; i++)
        {
            var member = Target[i];
            var body = Definition.Target;
            double death = _damageStarts[i];
            bool reacting = action.Kind == BattleZoomActionKind.Melee && elapsed >= FightStart && elapsed < ActionEnd;
            var sprite = elapsed >= death ? body.Advance ?? body.Idle : reacting ? body.Attack ?? body.Idle : body.Idle;
            double age = elapsed >= death ? elapsed - death : reacting ? elapsed - FightStart : elapsed;
            int interval = elapsed >= death ? body.Motion.DamageFrameMilliseconds : sprite.FrameMilliseconds;
            int step = (int)(Math.Max(0, age) * 1000 / interval);
            float opacity = elapsed >= death ? Math.Max(0, 255 - Math.Max(0, step - sprite.Columns + 1) * 32) / 255f : 1;
            if (opacity == 0) continue;
            var reaction = Movement(body.Motion.ReactingSteps, (int)(Math.Max(0, Math.Min(elapsed, ActionEnd) - FightStart) * 1000 / body.Idle.FrameMilliseconds), member.Frame);
            double x = member.X + reaction.X + (elapsed >= death ? step * body.Motion.DamageStep.X : 0), y = member.Y + reaction.Y + (elapsed >= death ? step * body.Motion.DamageStep.Y : 0);
            yield return Frame(sprite, member, x, y, age, interval) with { Frame = elapsed >= death ? Math.Min(step, sprite.Columns - 1) : (step + member.Frame) % sprite.Columns, Opacity = opacity };
            if (body.Wake is { } wake && !reacting) yield return new(wake, x + body.WakeOffset.X, y + body.WakeOffset.Y,
                (member.Frame + (int)(elapsed * 1000 / wake.FrameMilliseconds)) % wake.Columns, opacity)
            { Depth = y - 1 };
        }
        if (Definition.Wave is { } wave)
        {
            double distance = Math.Sqrt(Math.Pow(action.WaveStartX - action.WaveEndX, 2) + Math.Pow(action.WaveStartY - action.WaveEndY, 2));
            double travel = Math.Ceiling(distance / action.WaveStep) * action.MovementFrameMilliseconds / 1000.0;
            foreach (double start in _waves)
            {
                double age = elapsed - start; if (age < 0) continue;
                double progress = Math.Clamp(age / travel, 0, 1);
                int alpha = age < travel ? Math.Min(255, (int)(age * 1000 / action.MovementFrameMilliseconds + 1) * 32) :
                    Math.Max(0, 255 - (int)((age - travel) * 1000 / action.MovementFrameMilliseconds + 1) * 32);
                if (alpha == 0) continue;
                yield return new(wave, action.WaveStartX + (action.WaveEndX - action.WaveStartX) * progress,
                    action.WaveStartY + (action.WaveEndY - action.WaveStartY) * progress,
                    (int)(age * 1000 / action.MovementFrameMilliseconds) % wave.Columns, alpha / 255f);
                var motion = Definition.Source.Motion;
                double tick = action.MovementFrameMilliseconds / 1000.0;
                for (int trail = 1; trail < motion.TrailFrames; trail++)
                {
                    double previous = age - trail * tick;
                    if (previous < 0 || previous >= travel) continue;
                    double p = previous / travel;
                    double x = action.WaveStartX + (action.WaveEndX - action.WaveStartX) * p;
                    double y = action.WaveStartY + (action.WaveEndY - action.WaveStartY) * p;
                    yield return new(wave, x, y, (int)(previous / tick) % wave.Columns,
                        motion.TrailOpacity * (1 - trail / (float)motion.TrailFrames))
                    { Depth = y - 1 };
                }
            }
        }
    }

    private double SourceReset(BattleZoomMember member, int index)
    {
        int interval = Definition.Source.Motion.FightingFrameMilliseconds > 0 ? Definition.Source.Motion.FightingFrameMilliseconds : Definition.Action.MovementFrameMilliseconds;
        double start = SourceCombatStart(member);
        int frames = (int)Math.Ceiling(Math.Max(0, FightStop - start) * 1000 / interval);
        int remainder = (frames + _combatPhases[index]) % Definition.Source.Idle.Columns;
        return start + (frames + (Definition.Source.Idle.Columns - remainder) % Definition.Source.Idle.Columns) * interval / 1000.0;
    }

    private double SourceCombatStart(BattleZoomMember member)
    {
        int frames = (int)Math.Round((member.Arrival - member.AdvanceStart) * 1000 / Definition.Action.MovementFrameMilliseconds);
        int phase = (frames + member.Frame) % Definition.Source.Idle.Columns;
        return member.Arrival + (Definition.Source.Idle.Columns - 1 - phase) * Definition.Action.MovementFrameMilliseconds / 1000.0;
    }
    private static double ExitDuration(BattleBodyMotion motion) => Math.Ceiling(Math.Sqrt((double)motion.ExitTravel.X * motion.ExitTravel.X + (double)motion.ExitTravel.Y * motion.ExitTravel.Y) / motion.ExitSpeed) * motion.ExitFrameMilliseconds / 1000.0;
    private static BattlePoint ExitOffset(BattleBodyMotion motion, double age)
    {
        double duration = ExitDuration(motion), p = duration > 0 ? Math.Clamp(age / duration, 0, 1) : 0;
        return new((int)(motion.ExitTravel.X * p), (int)(motion.ExitTravel.Y * p));
    }

    private static BattlePoint Movement(BattlePoint[] steps, int count, int phase)
    {
        if (steps.Length == 0) return new(0, 0);
        int cycles = count / steps.Length, rest = count % steps.Length;
        int x = cycles * steps.Sum(p => p.X), y = cycles * steps.Sum(p => p.Y);
        for (int i = 0; i < rest; i++) { var p = steps[(phase + i) % steps.Length]; x += p.X; y += p.Y; }
        return new(x, y);
    }

    private ImmutableArray<BattleZoomLocalCue> PrepareLocalEffects(Func<int, int> next, List<BattleZoomSound> sounds)
    {
        List<BattleZoomLocalCue> cues = [];
        void Emit(BattleLocalEffect effect, double time, double x, double y, double depth, bool guaranteed = false)
        {
            HashSet<int>? coverage = null;
            if (effect.RotatedCoverage)
            {
                // A nonempty footprint plus its rotated copy preserves the clustered scatter density.
                int mask = 1 + next((1 << effect.Origins.Length) - 1);
                coverage = Enumerable.Range(0, effect.Origins.Length).Where(i => (mask & (1 << i)) != 0).ToHashSet();
                int shift = 1 + next(effect.Origins.Length - 1);
                coverage.UnionWith(coverage.Select(i => (i + shift) % effect.Origins.Length).ToArray());
            }
            for (int originIndex = 0; originIndex < effect.Origins.Length; originIndex++)
            {
                if (coverage is not null && !coverage.Contains(originIndex)) continue;
                var origin = effect.Origins[originIndex];
                if (!guaranteed && next(100) >= effect.Chance) continue;
                double px = x + origin.X + (effect.Jitter > 0 ? next(effect.Jitter * 2 + 1) - effect.Jitter : 0);
                double py = y + origin.Y + (effect.Jitter > 0 ? next(effect.Jitter * 2 + 1) - effect.Jitter : 0);
                var prepared = effect;
                if (effect.Styles.Length > 0)
                {
                    var style = effect.Styles[next(effect.Styles.Length)];
                    prepared = effect with { Sprite = effect.Sprite with { Opacity = style.Opacity }, FadePerFrame = style.FadePerFrame };
                }
                int frames = prepared.LifetimeFrames > 0 ? prepared.LifetimeFrames : prepared.Sprite.Columns;
                cues.Add(new(time, time + frames * prepared.Sprite.FrameMilliseconds / 1000.0, prepared, px, py, depth));
                if (effect.Sounds.Length > 0) sounds.Add(new(time, effect.Sounds[next(effect.Sounds.Length)]));
            }
        }
        for (int i = 0; i < Source.Length; i++)
        {
            var member = Source[i];
            foreach (var effect in Definition.Source.Effects)
            {
                void At(double time, int frame)
                {
                    if (effect.TriggerFrame >= 0 && frame % Definition.Source.Idle.Columns != effect.TriggerFrame) return;
                    var position = SourcePosition(i, time);
                    Emit(effect, time, position.X, position.Y, position.Y - 1);
                }
                if (effect.Moving)
                    for (double t = member.AdvanceStart; t < member.Arrival; t += Definition.Action.MovementFrameMilliseconds / 1000.0)
                        At(t, (int)Math.Round((t - member.AdvanceStart) * 1000 / Definition.Action.MovementFrameMilliseconds) + member.Frame);
                if (effect.Fighting && Definition.Action.Kind == BattleZoomActionKind.Melee)
                {
                    double start = SourceCombatStart(member);
                    int interval = Definition.Source.Motion.FightingFrameMilliseconds > 0 ? Definition.Source.Motion.FightingFrameMilliseconds : Definition.Action.MovementFrameMilliseconds;
                    for (double t = start; t < SourceReset(member, i); t += interval / 1000.0)
                        At(t, (int)Math.Round((t - start) * 1000 / interval) + _combatPhases[i]);
                }
                if (effect.Moving)
                    for (double t = FeedbackStart; t < FeedbackStart + ExitDuration(Definition.Source.Motion); t += Definition.Source.Motion.ExitFrameMilliseconds / 1000.0)
                    { var position = SourcePosition(i, t); Emit(effect, t, position.X, position.Y, position.Y - 1); }
                if (effect.Arrival && Definition.Action.Kind == BattleZoomActionKind.Melee)
                {
                    var position = SourcePosition(i, member.Arrival);
                    Emit(effect, member.Arrival, position.X, position.Y, position.Y + 1, true);
                }
            }
        }
        if (Definition.Wave is not null)
        {
            var action = Definition.Action;
            double travel = Math.Ceiling(Math.Sqrt(Math.Pow(action.WaveStartX - action.WaveEndX, 2) + Math.Pow(action.WaveStartY - action.WaveEndY, 2)) / action.WaveStep) * action.MovementFrameMilliseconds / 1000.0;
            foreach (double start in _waves)
                for (double t = 0; t < travel; t += action.MovementFrameMilliseconds / 1000.0)
                    foreach (var effect in Definition.WaveEffects)
                        Emit(effect, start + t, action.WaveStartX + (action.WaveEndX - action.WaveStartX) * t / travel,
                            action.WaveStartY + (action.WaveEndY - action.WaveStartY) * t / travel,
                            action.WaveStartY + (action.WaveEndY - action.WaveStartY) * t / travel - 1);
        }
        for (int i = 0; i < Target.Length; i++)
            if (double.IsFinite(_damageStarts[i]))
                foreach (var effect in Definition.Target.DamageEffects)
                {
                    var reaction = Movement(Definition.Target.Motion.ReactingSteps, (int)(Math.Max(0, ActionEnd - FightStart) * 1000 / Definition.Target.Idle.FrameMilliseconds), Target[i].Frame);
                    Emit(effect, _damageStarts[i], Target[i].X + reaction.X, Target[i].Y + reaction.Y, Target[i].Y + reaction.Y + 1, true);
                }
        return cues.ToImmutableArray();
    }

    private BattlePoint SourcePosition(int i, double elapsed)
    {
        var member = Source[i]; var action = Definition.Action;
        double progress = member.Arrival > member.AdvanceStart ? Math.Clamp((elapsed - member.AdvanceStart) / (member.Arrival - member.AdvanceStart), 0, 1) : 0;
        int interval = Definition.Source.Motion.FightingFrameMilliseconds > 0 ? Definition.Source.Motion.FightingFrameMilliseconds : action.MovementFrameMilliseconds;
        var offset = Movement(Definition.Source.Motion.FightingSteps, (int)(Math.Max(0, Math.Min(elapsed, SourceReset(member, i)) - SourceCombatStart(member)) * 1000 / interval), _combatPhases[i]);
        var exit = ExitOffset(Definition.Source.Motion, Math.Max(0, elapsed - FeedbackStart));
        return new(member.X + (int)(action.TravelX * progress) + offset.X + exit.X, member.Y + (int)(action.TravelY * progress) + offset.Y + exit.Y);
    }

    public IEnumerable<BattleZoomFrame> SampleLocalEffects(double elapsed)
    {
        foreach (var cue in LocalEffects)
        {
            if (elapsed < cue.Start || elapsed >= cue.End) continue;
            int step = (int)((elapsed - cue.Start) * 1000 / cue.Effect.Sprite.FrameMilliseconds);
            float initial = cue.Effect.Sprite.Opacity;
            float opacity = initial > 0 ? Math.Max(0, initial - cue.Effect.FadePerFrame * step / 256f) / initial : 0;
            yield return new(cue.Effect.Sprite, cue.X + cue.Effect.Step.X * step, cue.Y + cue.Effect.Step.Y * step,
                step % cue.Effect.Sprite.Columns, opacity)
            { Depth = cue.Depth };
        }
    }
}
