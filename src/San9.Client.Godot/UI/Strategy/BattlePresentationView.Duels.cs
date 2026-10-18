using Godot;
using San9.Application.Presentation;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class BattlePresentationView
{
    private DuelSceneCue? _duel;
    private TextureScope? _duelUi;
    private GameRichTextLabel _duelText = null!;
    private Node2D _duelPanels = null!, _duelSpeechFrame = null!;
    private DuelSpeechLayout? _duelSpeechLayout;
    private Vector2 _duelSpeechPosition;
    private int _duelSpeechTiles;
    private readonly Dictionary<int, TextureScope> _duelPortraits = [];

    private void PrepareDuels(BattlePresentationPlan plan, HashSet<string> resources, HashSet<string> flagResources)
    {
        if (plan.Duels.Count > 0) _duelUi!.RequestPrefixes("text-icon-");
        foreach (var duel in plan.Duels)
        {
            resources.Add(duel.Backdrop);
            resources.UnionWith(duel.Definition.Interface.Assets);
            resources.UnionWith(duel.Definition.Particles.Select(p => p.Animation.Asset));
            foreach (var flag in duel.Flags)
                foreach (string role in FlagVisuals.Layers(flag.Direction, flag.Color, flag.Glyph))
                    flagResources.UnionWith(Textures.AnimationClips[role].Frames);
            foreach (var audience in duel.Definition.Audience)
                resources.UnionWith([audience.Idle.Asset, audience.Cheer.Asset, audience.Defeat.Asset]);
            foreach (var stage in duel.Stages)
            {
                foreach (var (fighter, right, action) in new[] { (stage.Left, false, stage.LeftAction), (stage.Right, true, stage.RightAction) })
                    resources.Add(duel.Definition.Bodies.Single(b => b.Female == fighter.Female && b.Right == right).Actions[action].Asset);
                resources.UnionWith(stage.Effects.Select(e => e.Asset));
            }
            foreach (int person in duel.LeftRoster.Concat(duel.RightRoster))
            {
                if (_duelPortraits.ContainsKey(person)) continue;
                string role = $"information-person-{person}";
                var scope = Textures.CreatePortraitScope(role, PortraitView.Bust, this);
                scope.Request([role]); _portraits.Add(scope); _duelPortraits.Add(person, scope);
            }
        }
    }

    private void UpdateDuel(BattlePresentationPlan plan, double time)
    {
        _duel = plan.Duels.FirstOrDefault(c => time >= c.Start && time < c.End);
        var stage = _duel?.Sample(time);
        _duelSpeechLayout = null;
        _duelText.Visible = stage?.Dialogue is not null;
        if (_duel is null || stage is null) return;
        if (stage.Dialogue is { } dialogue)
        {
            var layout = _duelSpeechLayout = _duel.Definition.Interface.Speech.Single(s => s.Right == (dialogue.SpeakerId == stage.Right.Id));
            string markup = dialogue.Message.BbCode;
            if (_duelText.Markup != markup) _duelText.Present(markup, new(24, Colors.Black, GameTextEffect.None, Colors.Transparent));
            var font = _duelText.GetThemeFont("normal_font");
            float width = font.GetMultilineStringSize(_duelText.GetParsedText(), width: layout.MaximumTextWidth, fontSize: 24).X;
            int tileWidth = _assets![layout.MiddleAsset].GetWidth();
            _duelSpeechTiles = Mathf.CeilToInt(Math.Max(layout.ReferenceWidth, width) / tileWidth);
            int textWidth = _duelSpeechTiles * tileWidth;
            _duelSpeechPosition = new(layout.Position.X + (layout.Right ? layout.ReferenceWidth - textWidth : 0), layout.Position.Y);
            _duelText.Size = new(textWidth, layout.LineHeight * 3);
            _duelText.Position = GameTextMetrics.GlyphCellPosition(
                SceneOffset + _duelSpeechPosition + new Vector2(_assets[layout.StartAsset].GetWidth() + layout.TextOffset.X, layout.TextOffset.Y), font, 24);
            _duelText.AddThemeConstantOverride("line_separation", layout.LineHeight - Mathf.CeilToInt(font.GetHeight(24)));
        }
        int index = _duel.Stages.IndexOf(stage);
        // A seek plays only the active stage, never sounds belonging to skipped strikes.
        if (_sounds.Add((_duel.Id, index)))
        {
            if (stage.Sound is { } sound) PlaySound?.Invoke(sound);
            if (stage.Effects.FirstOrDefault()?.Sound is { } effectSound && effectSound != stage.Sound) PlaySound?.Invoke(effectSound);
        }
    }

    private double DuelTime(DuelStage stage) => stage.Dialogue is null ? _day!.Elapsed : Math.Min(stage.End, _day!.Elapsed + _day.DuelDialogueElapsed);

    private void DrawDuel()
    {
        if (_duel is not { } duel || _day is null || _assets is null || !ResourcesReady || duel.Sample(_day.Elapsed) is not { } stage) return;
        double time = DuelTime(stage);
        double age = time - stage.Start, progress = DuelSceneCue.Progress(stage, time);
        var font = GetThemeFont("font");
        var ui = duel.Definition.Interface;
        DrawRect(new(Vector2.Zero, GetViewportRect().Size), new Color(new Color(ui.TintColor), ui.TintOpacity));
        DrawSetTransform(SceneOffset);
        DrawTexture(_assets[duel.Backdrop], new(ui.BackdropPosition.X, ui.BackdropPosition.Y));
        void Text(string text, float x, float y, int size = 22, Color? color = null) =>
            DrawString(font, new(x, y), text, fontSize: size, modulate: color ?? Colors.White);

        List<(float Depth, Action Draw)> actors = [];
        foreach (var flag in duel.Flags)
            actors.Add((flag.Position.Y, () =>
            {
                foreach (string role in FlagVisuals.Layers(flag.Direction, flag.Color, flag.Glyph))
                {
                    var clip = Textures.AnimationClips[role];
                    int frame = (flag.Phase + (int)((time - duel.Start) * 1000 / clip.FrameMilliseconds)) % clip.Frames.Length;
                    DrawTexture(_flags![clip.Frames[frame]], new(flag.Position.X, flag.Position.Y));
                }
            }));
        foreach (var audience in duel.Definition.Audience)
        {
            var mood = audience.Right ? stage.RightAudience : stage.LeftAudience;
            var animation = mood == DuelAudienceMood.Cheer ? audience.Cheer : mood == DuelAudienceMood.Dismay ? audience.Defeat : audience.Idle;
            var texture = _assets[animation.Asset];
            var size = new Vector2(texture.GetWidth() / (float)animation.Columns, texture.GetHeight());
            for (int i = 0; i < audience.Positions.Length; i++)
            {
                var point = audience.Positions[i];
                int frame = ((int)(time * 1000 / animation.FrameMilliseconds) + i) % animation.Columns;
                actors.Add((point.Y, () => DrawTextureRectRegion(texture, new(new Vector2(point.X, point.Y), size), new(frame * size.X, 0, size.X, size.Y))));
            }
        }

        void Fighter(DuelFighterView fighter, bool right, DuelAction action)
        {
            var animation = duel.Definition.Bodies.Single(b => b.Female == fighter.Female && b.Right == right).Actions[action];
            var texture = _assets[animation.Asset];
            int frame = DuelSceneCue.AnimationFrame(animation, age + (right ? stage.RightAnimationAge : stage.LeftAnimationAge), action is DuelAction.Idle or DuelAction.Return);
            var size = new Vector2(texture.GetWidth() / (float)animation.Columns, texture.GetHeight());
            var point = DuelSceneCue.Position(stage, time, right);
            var position = new Vector2((float)point.X, (float)point.Y);
            float fromAlpha = right ? stage.RightOpacityFrom : stage.LeftOpacityFrom;
            float toAlpha = right ? stage.RightOpacityTo : stage.LeftOpacityTo;
            float opacity = Mathf.Lerp(fromAlpha, toAlpha, (float)progress);
            DrawTextureRectRegion(texture, new(position, size), new(frame * size.X, 0, size.X, size.Y), new Color(1, 1, 1, opacity));
        }
        actors.Add(((float)DuelSceneCue.Position(stage, time, true).Y, () => Fighter(stage.Right, true, stage.RightAction)));
        actors.Add(((float)DuelSceneCue.Position(stage, time, false).Y, () => Fighter(stage.Left, false, stage.LeftAction)));
        foreach (var actor in actors.OrderBy(a => a.Depth)) actor.Draw();
        Text(CommandText.Resolve(Texts, IsPaused() ? "battle.resume" : "battle.pause"), 832, 730, 20);
        Text(CommandText.Resolve(Texts, "battle.skip"), 932, 730, 20);
        DrawSetTransform(Vector2.Zero);
    }

    private void DrawDuelPanels()
    {
        if (_duel is not { } duel || _day is null || _assets is null || !ResourcesReady || duel.Sample(_day.Elapsed) is not { } stage) return;
        var layer = _duelPanels;
        var font = GetThemeFont("font");
        foreach (var panel in duel.Definition.Interface.Panels)
        {
            var fighter = panel.Right ? stage.Right : stage.Left;
            var origin = SceneOffset + new Vector2(panel.Position.X, panel.Position.Y);
            var portrait = panel.Portrait;
            // Original order: the bust is behind the native frame, including its transparent margins.
            layer.DrawTextureRect(_duelPortraits[fighter.Id][$"information-person-{fighter.Id}"],
                new(origin + new Vector2(portrait.X, portrait.Y), new(portrait.Width, portrait.Height)), false);
            layer.DrawTexture(_assets[panel.Asset], origin);
            void Text(string text, DuelUiRect rect, int size, Color color) => GameTextPresenter.DrawAtGlyphCell(layer, font, text,
                origin + new Vector2(rect.X, rect.Y), new(size, color, size == 16 ? GameTextEffect.None : GameTextEffect.Shadow, Colors.Black), HorizontalAlignment.Center, rect.Width);
            Text(Texts.Resolve(_day.Before.MessageSpeaker(fighter.Id).NameKey), panel.Name, 24, new("f0f0f0"));
            Text(CommandText.Resolve(Texts, "label.war"), panel.WarLabel, 16, Colors.Black);
            Text(fighter.War.ToString(), panel.War, 24, new(fighter.Injury > 0 ? "f06850" : "f0f0f0"));
            Text(CommandText.Resolve(Texts, "label.health"), panel.HealthLabel, 16, Colors.Black);
            var health = _assets[panel.HealthAsset];
            double hp = DuelSceneCue.HitPoints(stage, DuelTime(stage), panel.Right);
            int width = DuelInterfacePlayback.HealthWidth(health.GetWidth(), hp);
            int x = panel.Right ? 0 : health.GetWidth() - width;
            if (width > 0) layer.DrawTextureRectRegion(health,
                new(origin + new Vector2(panel.HealthPosition.X + x, panel.HealthPosition.Y), new(width, health.GetHeight())),
                new(x, 0, width, health.GetHeight()));
        }
    }

    private void DrawDuelSpeech()
    {
        if (_duel is null || _duelSpeechLayout is not { } layout || !ResourcesReady) return;
        var point = SceneOffset + _duelSpeechPosition;
        var start = _assets![layout.StartAsset];
        var middle = _assets[layout.MiddleAsset];
        _duelSpeechFrame.DrawTexture(start, point);
        point.X += start.GetWidth();
        for (int i = 0; i < _duelSpeechTiles; i++)
        {
            _duelSpeechFrame.DrawTexture(middle, point);
            point.X += middle.GetWidth();
        }
        _duelSpeechFrame.DrawTexture(_assets[layout.EndAsset], point);
    }
}
