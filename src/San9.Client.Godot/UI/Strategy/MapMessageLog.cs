using San9.Application.Presentation;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>Session-local history; the ten-row map surface is a view, not a storage limit.</summary>
public sealed class MapMessageLog
{
    private readonly List<MapMessageRecord> _entries = [];
    private readonly HashSet<Guid> _published = [];
    public event Action? Changed;
    public IReadOnlyList<MapMessageRecord> Entries => _entries.AsReadOnly();
    public IEnumerable<MapMessageRecord> Visible => _entries.TakeLast(10);

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
        if (entry.Id == Guid.Empty) throw new ArgumentException("A publication identity is required.", nameof(entry));
        if (!_published.Add(entry.Id)) return false;
        _entries.Add(entry);
        Changed?.Invoke();
        return true;
    }
}
