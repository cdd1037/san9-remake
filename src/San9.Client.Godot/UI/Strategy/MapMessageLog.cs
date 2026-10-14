using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>Session-local history; the ten-row map surface is a view, not a storage limit.</summary>
public sealed class MapMessageLog
{
    private readonly List<MapMessageRecord> _entries = [];
    private readonly HashSet<string> _published = [];
    public event Action? Changed;
    public IReadOnlyList<MapMessageRecord> Entries => _entries.AsReadOnly();
    private readonly Queue<MapMessageRecord> _immediate = new(10);
    public Settings.ProgressReportMode ReportMode { get; set; } = Settings.ProgressReportMode.Detailed;
    public IEnumerable<MapMessageRecord> Visible => _immediate;
    public bool Allows(MapMessageRecord entry) => ReportMode switch
    {
        Settings.ProgressReportMode.Detailed => true,
        Settings.ProgressReportMode.Summary => entry.Importance == MapReportImportance.Summary,
        Settings.ProgressReportMode.Off => false,
        _ => throw new InvalidOperationException("Invalid report preference.")
    };

    public InformationTable ReadHistory() => new("进行记录", ["记录"], ["日期", "内容"],
        _entries.Select((entry, index) =>
        {
            var clock = entry.Message.Clock;
            // Sorting uses text only; rendering retains the original produced markup in Entries.
            string withoutImages = System.Text.RegularExpressions.Regex.Replace(entry.Message.BbCode,
                @"\[img[^\]]*\][^\[]*\[/img\]", "");
            string plain = MessageMarkup.Plain(System.Text.RegularExpressions.Regex.Replace(withoutImages,
                @"\[(?!(?:lb|rb)\])[^\]]+\]", ""));
            return new InformationRow(new(InformationCategory.Records, index),
                [new($"{clock.Year}年{clock.Month}月{clock.Day}日", clock.DayNumber), plain]);
        }).ToArray());

    public bool Append(MapMessageRecord entry)
    {
        if (entry.Id == string.Empty) throw new ArgumentException("A publication identity is required.", nameof(entry));
        if (!_published.Add(entry.Id)) return false;
        _entries.Add(entry);
        if (Allows(entry))
        {
            if (_immediate.Count == 10) _immediate.Dequeue();
            _immediate.Enqueue(entry);
        }
        Changed?.Invoke();
        return true;
    }
}
