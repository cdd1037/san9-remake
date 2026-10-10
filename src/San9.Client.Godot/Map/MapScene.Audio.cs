using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot;

internal sealed partial class MapScene
{
    internal IEnumerable<MapSoundSource> SoundSources(MapAudioDefinition audio, IEnumerable<UnitVisual> visibleUnits)
    {
        foreach (var unit in visibleUnits)
        {
            var sampled = Sample(unit);
            var state = _stateSampler(unit.Id, MotionTime - _motionStart)!;
            var formation = Map.Units.Formations.Single(f => f.FormationId == state.FormationId);
            var layout = formation.Layouts.Where(l => l.MinimumSoldiers <= state.Soldiers).MaxBy(l => l.MinimumSoldiers)!;
            var bodyId = layout.Members[0].BodyId;
            if (state.UsesImperialAppearance) bodyId = Map.Units.Bodies.Single(b => b.Id == bodyId).ImperialBodyId ?? bodyId;
            var sounds = audio.Bodies[bodyId];
            var roles = sampled.Action switch
            {
                UnitVisualAction.Moving when !state.IsEngaged => sounds.Movement,
                UnitVisualAction.Melee => sounds.Melee,
                UnitVisualAction.Ranged => sounds.Ranged,
                _ => []
            };
            if (roles.Length > 0)
            {
                long? pulse = null;
                if (sampled.Action is UnitVisualAction.Melee or UnitVisualAction.Ranged)
                {
                    var sprite = sampled.Animations.First(s => s.IsBody);
                    var clip = Clips[sprite.ClipId];
                    pulse = (long)Math.Floor(Math.Max(0, Day!.CombatMilliseconds) / (clip.FrameMilliseconds * clip.Frames.Length));
                }
                int variation = pulse is long tick ? (int)(UnitVisuals.Seed(unit.Id, 0, (int)tick) % (uint)roles.Length) : unit.Id % roles.Length;
                yield return new($"unit:{unit.Id}", roles[variation], sampled.AnchorX, sampled.AnchorY) { Pulse = pulse };
            }
            if (state.IsEngaged)
                yield return new($"battle-unit:{unit.Id}", audio.BattleAmbience[0], sampled.AnchorX, sampled.AnchorY) { Kind = MapSoundKind.BattleAmbience };
        }
        foreach (var building in Buildings)
        {
            var place = Settlements[building.Id];
            var anchor = Anchor(building);
            if (place.IsUnderConstruction && place.FacilityTypeId is int type)
                yield return new($"construction:{building.Id}", audio.ConstructionSounds[type], anchor.X, anchor.Y) { Kind = MapSoundKind.Construction };
            bool engaged = Day?.CombatMilliseconds >= 0 ? Day.After.IsSettlementEngaged(place.Id) : State.IsSettlementEngaged(place.Id);
            if (engaged)
                yield return new($"battle-place:{building.Id}", audio.BattleAmbience[0], anchor.X, anchor.Y) { Kind = MapSoundKind.BattleAmbience };
        }
        if (Day is not { CombatMilliseconds: >= 0 } day) yield break;
        foreach (var attack in _attacks.Where(a => a.SourceIsSettlement))
        {
            if (attack.Projectile == San9.Content.Maps.MapProjectileKind.Arrow)
            {
                int count = attack.Origins.Length > 0 ? attack.Origins.Length : 8;
                long pulse = Math.Min(count - 1, (long)(day.CombatMilliseconds / 200));
                var origin = attack.Origins.Length > 0 ? attack.Origins[pulse] : new MapMotionPoint(attack.FromX, attack.FromY);
                int variation = (int)(UnitVisuals.Seed(attack.SourceId, attack.TargetId, (int)pulse) % (uint)audio.FacilityArrowSounds.Length);
                yield return new($"arrows:{attack.SourceId}:{attack.TargetIsSettlement}:{attack.TargetId}", audio.FacilityArrowSounds[variation], origin.X, origin.Y) { Pulse = pulse };
            }
            else if (attack.Projectile == San9.Content.Maps.MapProjectileKind.Rockfall && day.CombatMilliseconds >= MapProjectiles.Duration(attack))
                for (int i = 0; i < audio.RockfallImpactSounds.Length; i++)
                    yield return new($"rockfall:{attack.SourceId}:{attack.TargetIsSettlement}:{attack.TargetId}:{i}", audio.RockfallImpactSounds[i], attack.ToX, attack.ToY) { Pulse = 0 };
        }
    }
}
