using Godot;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private SeasonChangeAnimation? _seasonChange;
    private SeasonChangeView _seasonChangeView = null!;
    private string _seasonTarget = "spring";
    private string _badgeSeason = "spring";
    private float _badgeBrightness = 1;

    private bool PresentSeasonChange(int month)
    {
        if (_seasonChange is null)
        {
            _seasonTarget = CalendarSeason.ForMonth(month);
            _seasonChange = new();
            return false;
        }
        if (!_seasonChange.IsComplete) return false;
        _seasonChange = null;
        _seasonChangeView.Sample(null);
        return true;
    }

    private void AdvanceSeasonChange(double delta)
    {
        _seasonChange?.Advance(delta, World.IsVisibleReady && World.Season == _seasonTarget);
    }

    private void UpdateSeasonPresentation()
    {
        World.PresentationSeason = Turn.SeasonChangeOrigin is { } previous
            ? _seasonChange?.NeedsNewSeason == true ? _seasonTarget : CalendarSeason.ForMonth(previous.Month)
            : null;
        _seasonChangeView.Sample(_seasonChange, World.LoadError);
        string season = _seasonChange?.ShowsNewBadge == true ? _seasonTarget :
            Turn.SeasonChangeOrigin is { } old ? CalendarSeason.ForMonth(old.Month) :
            CalendarSeason.ForMonth((Turn.Display ?? Session.View).Month);
        float brightness = _seasonChange?.BadgeBrightness(season) ?? 1;
        if (_badgeSeason != season)
        {
            _text.Apply(_season, GameTextStyles.For(GameTextRole.Spring) with
            {
                EffectColor = new Color(season switch { "spring" => "770000", "summer" => "002878", "autumn" => "b82000", _ => "000000" })
            });
        }
        if (_badgeSeason != season || _badgeBrightness != brightness)
        {
            _badgeSeason = season; _badgeBrightness = brightness;
            QueueRedraw();
        }
        _season.Text = season switch { "spring" => "春", "summer" => "夏", "autumn" => "秋", _ => "冬" };
    }
}
