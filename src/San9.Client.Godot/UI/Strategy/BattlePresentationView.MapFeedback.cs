using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class BattlePresentationView
{
    private sealed class MapNumberVisual(BattleMapNumberCue cue, BattleMapNumberDefinition definition)
    {
        public BattleMapNumberCue Cue { get; } = cue;
        public BattleMapNumberDefinition Definition { get; } = definition;
        public double Age { get; set; }
    }
    private readonly List<MapNumberVisual> _mapNumbers = [];
    private readonly HashSet<string> _publishedNumbers = [];
    private sealed class MapMoraleVisual(BattleVisualCue cue, double age)
    {
        public BattleVisualCue Cue { get; } = cue;
        public double Age { get; set; } = age;
    }
    private readonly List<MapMoraleVisual> _mapMorales = [];
    private readonly HashSet<string> _publishedMorales = [];
    private TextureScope? _numberAssets;

    // Publication happens before the prepared day is released. The view owns these
    // finite visuals and their resource lease across subsequent days and strategy UI.
    public void PublishMapFeedback(TurnDayPresentation day)
    {
        if (day.Battle is not { } plan) return;
        foreach (var cue in plan.Visuals.Where(c => c.MoraleGain > 0 && day.IsMapEffectPresented(c.Id)))
        {
            if (_publishedMorales.Add(cue.Id)) _mapMorales.Add(new(cue, day.MapEffectElapsed(cue.Id)));
        }
        if (plan.NumberDefinition is { } definition)
            foreach (var cue in plan.MapNumbers.Where(c => (c.Start <= day.Elapsed || day.IsComplete) &&
                day.IsFactReady(c.FactIndex) && day.IsDialogueComplete(c.AfterDialogueId)))
            {
                if (!_publishedNumbers.Add(cue.Id)) continue;
                if (day.Plan is null || day.Viewport?.Invoke() is not { } bounds || !day.Plan.AdmitMapNumber(cue, definition, bounds)) continue;
                _numberAssets ??= Textures.CreateScope(Textures.LibraryAssets);
                _numberAssets.Request([definition.Asset]);
                _mapNumbers.Add(new(cue, definition));
            }
    }

    public override void _Process(double delta)
    {
        if (_mapNumbers.Count == 0 && _mapMorales.Count == 0) return;
        foreach (var number in _mapNumbers)
            if (_numberAssets!.AreReady([number.Definition.Asset])) number.Age += Math.Max(0, delta);
        _mapNumbers.RemoveAll(n => n.Age >= BattleMapNumberPlayback.Duration(n.Definition));
        foreach (var morale in _mapMorales) morale.Age += Math.Max(0, delta);
        _mapMorales.RemoveAll(n => n.Age >= 1.5);
        if (_mapNumbers.Count == 0) { _numberAssets?.Dispose(); _numberAssets = null; }
        _numberLayer.QueueRedraw();
        QueueRedraw();
    }
}
