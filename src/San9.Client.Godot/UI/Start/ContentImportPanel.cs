using System.Collections.Concurrent;
using Godot;
using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>Startup content preparation UI; imports run on a background task in the shared game runtime.</summary>
public partial class ContentImportPanel : Control
{
    public string ContentRoot { get; init; } = "";
    public string Failure { get; init; } = "";
    public bool ImportAvailable { get; init; }
    public Func<Task> Continue { get; init; } = () => Task.CompletedTask;
    private readonly ConcurrentQueue<ContentImportProgress> _messages = new();
    private readonly Queue<string> _recent = new();
    private CancellationTokenSource? _cancellation;
    private ContentImportStage? _stage;
    private bool _busy;
    private bool _exiting;
    private string? _source;
    private string? _ffmpeg;
    private CenterContainer _center = null!;
    private PanelContainer _card = null!;
    private Label _title = null!;
    private Label _introduction = null!;
    private Label _status = null!;
    private Label _mediaHint = null!;
    private Label _encoderHint = null!;
    private RichTextLabel _log = null!;
    private Label _failureDetails = null!;
    private Button _details = null!;
    private Button _logToggle = null!;
    private LineEdit _sourcePath = null!;
    private LineEdit _encoderPath = null!;
    private Button _select = null!;
    private Button _start = null!;
    private Button _encoder = null!;
    private Button _cancel = null!;
    private Button _continue = null!;
    private CheckBox _audio = null!;
    private CheckBox _movies = null!;
    private CheckBox _originalTextEdits = null!;
    private ProgressBar _progress = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = ContentEditorStyle.Create(GetThemeFont("font"));
        var background = new ColorRect { Color = new("111820"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background); background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string edge in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 24);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(scroll);
        _center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_center);
        _card = new PanelContainer();
        var surface = new StyleBoxFlat { BgColor = new("1b222c"), BorderColor = new("394758"),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 22, ContentMarginBottom = 22 };
        surface.SetCornerRadiusAll(12); surface.SetBorderWidthAll(1);
        _card.AddThemeStyleboxOverride("panel", surface);
        _center.AddChild(_card);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 12);
        _card.AddChild(layout);
        _title = Text("准备游戏内容", 26, ContentEditorStyle.Accent);
        layout.AddChild(_title);
        _introduction = Text(File.Exists(Path.Combine(ContentRoot, "catalog.json"))
            ? "现有内容未能加载，可以重新加载或选择原版目录转换。"
            : "首次运行，请先将原版资源转换为游戏内容。");
        layout.AddChild(_introduction);

        layout.AddChild(Text("原版游戏目录", 14, ContentEditorStyle.Muted));
        var sourceRow = Row(layout);
        _sourcePath = PathField("", "选择含 San9PK.exe 的原版游戏目录");
        sourceRow.AddChild(_sourcePath);
        _select = Action("选择目录", SelectSource); sourceRow.AddChild(_select);
        layout.AddChild(Text("支持繁体中文 PK 1.0.1.0。转换不会修改原版文件。", 14, ContentEditorStyle.Muted));

        layout.AddChild(Text("内容保存位置", 14, ContentEditorStyle.Muted));
        var outputRow = Row(layout);
        outputRow.AddChild(PathField(ContentRoot));
        outputRow.AddChild(Action("打开目录", () =>
        {
            try { ClientFolderAccess.Open(ContentRoot); }
            catch (Exception error) { _status.Text = "打开目录失败：" + error.Message; }
        }));

        var options = new HFlowContainer();
        options.AddThemeConstantOverride("h_separation", 10);
        options.AddThemeConstantOverride("v_separation", 10);
        layout.AddChild(options);
        _originalTextEdits = new CheckBox { Text = "应用原版文本修订", ButtonPressed = true,
            TooltipText = "按原版编号校订姓名、表字、官职和用词。转换已改名的Mod时可关闭，关闭后使用通用繁简转换。" };
        options.AddChild(_originalTextEdits);
        _audio = new CheckBox { Text = "转换音频（音乐与音效）", ButtonPressed = true,
            TooltipText = "关闭后不处理音频，保留内容目录中的已有音乐与音效。首次准备请开启。" };
        _movies = new CheckBox { Text = "转换影片", ButtonPressed = true,
            TooltipText = "关闭后不处理影片，保留已有影片。影片可选安装。" };
        options.AddChild(_audio); options.AddChild(_movies);
        _mediaHint = Text("", 14, ContentEditorStyle.Muted); layout.AddChild(_mediaHint);
        var encoderRow = Row(layout);
        _ffmpeg = ImportAvailable ? ContentImportService.FindEncoder() : null;
        _encoderPath = PathField(_ffmpeg ?? "", "请选择 FFmpeg 可执行文件"); encoderRow.AddChild(_encoderPath);
        _encoder = Action("选择 FFmpeg", SelectEncoder); encoderRow.AddChild(_encoder);
        _encoderHint = Text(!ImportAvailable ? "开发构建不包含内置转换器。"
            : _ffmpeg is null ? "未在系统 PATH 中找到 FFmpeg，请手动选择。" : "已自动找到 FFmpeg。", 14, ContentEditorStyle.Muted);
        layout.AddChild(_encoderHint);
        _audio.Toggled += _ => UpdateMediaOptions();
        _movies.Toggled += _ => UpdateMediaOptions();

        _status = Text(ImportAvailable ? "选择原版目录，确认转换选项后开始。" : "开发构建不包含内置转换器，请使用 San9.Tools 准备内容。");
        layout.AddChild(_status);
        _progress = new ProgressBar { Indeterminate = true, ShowPercentage = false, Visible = false, CustomMinimumSize = new(0, 6) };
        layout.AddChild(_progress);
        var actions = Row(layout);
        _start = Action("开始转换", async () => await Import());
        actions.AddChild(_start);
        _cancel = Action("取消转换", CancelImport); _cancel.Visible = false; actions.AddChild(_cancel);
        actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _continue = Action("重新加载内容", async () => await EnterGame()); actions.AddChild(_continue);
        SetPrimary(_start, true);

        var detailActions = Row(layout);
        _details = Action("查看加载详情", () =>
        {
            _failureDetails.Visible = !_failureDetails.Visible;
            _details.Text = _failureDetails.Visible ? "收起加载详情" : "查看加载详情";
        });
        detailActions.AddChild(_details);
        _logToggle = Action("展开转换日志", ToggleLog); _logToggle.Disabled = true; detailActions.AddChild(_logToggle);
        _failureDetails = Text(Failure, 13, ContentEditorStyle.Muted); _failureDetails.Visible = false; layout.AddChild(_failureDetails);
        _log = new RichTextLabel { Visible = false, CustomMinimumSize = new(0, 140), ScrollFollowing = true, SelectionEnabled = true };
        _log.AddThemeFontSizeOverride("normal_font_size", 13); layout.AddChild(_log);
        Resized += FitCard; FitCard(); SetBusy(false);
    }

    private void FitCard()
    {
        _card.CustomMinimumSize = new(Math.Clamp(Size.X - 64, 360, 880), 0);
        _center.CustomMinimumSize = new(0, Math.Max(0, Size.Y - 48));
    }

    private static Label Text(string text, int size = 16, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } value) label.AddThemeColorOverride("font_color", value);
        return label;
    }

    private static HBoxContainer Row(Control parent)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); parent.AddChild(row); return row;
    }

    private static LineEdit PathField(string path, string placeholder = "") => new()
    {
        Text = path, PlaceholderText = placeholder, Editable = false, TooltipText = path,
        SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 38)
    };

    private static Button Action(string text, System.Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new(0, 38) };
        button.Pressed += () => pressed(); return button;
    }

    private static void SetPrimary(Button button, bool primary)
    {
        if (primary) { CompanionPanelStyle.Primary(button); return; }
        foreach (string state in new[] { "normal", "hover", "pressed" }) button.RemoveThemeStyleboxOverride(state);
        foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color" }) button.RemoveThemeColorOverride(color);
    }

    private void UpdateMediaOptions()
    {
        bool media = _audio.ButtonPressed || _movies.ButtonPressed;
        _encoder.Disabled = _busy || !ImportAvailable || !media;
        _encoderPath.Modulate = media ? Colors.White : new Color(1, 1, 1, .5f);
        _mediaHint.Text = !_audio.ButtonPressed
            ? "不转换音频：需已有完整音乐与音效；首次准备请开启。" + (_movies.ButtonPressed ? "影片转换需要 FFmpeg。" : "本次不需要 FFmpeg。")
            : "音频转换需要 FFmpeg。已转换且未变化的媒体会自动复用。";
    }

    private void SelectSource()
    {
        if (_busy) return;
        var dialog = new FileDialog { Title = "选择原版游戏目录", Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenDir, UseNativeDialog = true };
        dialog.DirSelected += path =>
        {
            _source = path; _sourcePath.Text = path; _sourcePath.TooltipText = path; _select.Text = "更换目录";
            _status.Text = "目录已选择，确认转换选项后点击“开始转换”。";
            SetBusy(false); dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered(new(800, 560));
    }

    private void SelectEncoder()
    {
        if (_busy) return;
        var dialog = new FileDialog { Title = "选择 ffmpeg 可执行文件", Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile, UseNativeDialog = true };
        dialog.FileSelected += path =>
        {
            _ffmpeg = path; _encoderPath.Text = path; _encoderPath.TooltipText = path; dialog.QueueFree();
            _encoderHint.Text = "已手动选择 FFmpeg。";
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered(new(800, 560));
    }

    private async Task Import()
    {
        if (_busy || _exiting || !ImportAvailable || _source is null) return;
        _recent.Clear(); _log.Text = ""; _stage = null;
        _continue.Text = "重新加载内容"; SetPrimary(_continue, false); SetPrimary(_start, true);
        _title.Text = "正在准备游戏内容";
        _introduction.Text = "正在转换所选内容，请等待完成。";
        _status.Text = "正在检查原版目录与转换依赖……";
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation; SetBusy(true);
        try
        {
            await ContentImportService.ImportAsync(_source, ContentRoot, _ffmpeg, _audio.ButtonPressed, _movies.ButtonPressed, _originalTextEdits.ButtonPressed, _messages.Enqueue, cancellation.Token);
            if (_exiting) return;
            DrainMessages();
            _title.Text = "游戏内容已准备就绪";
            _introduction.Text = "转换与内容校验已完成，可以进入游戏。";
            _status.Text = "准备完成。";
            _status.AddThemeColorOverride("font_color", new Color("a8d7bd"));
            _details.Visible = false; _failureDetails.Visible = false;
            _continue.Text = "进入游戏"; SetPrimary(_start, false); SetPrimary(_continue, true);
        }
        catch (OperationCanceledException)
        {
            if (!_exiting)
            {
                _title.Text = "准备游戏内容"; _introduction.Text = "调整目录或转换选项后，可以重新开始。";
                _status.Text = "转换已取消，已完成的内容会保留，可以重试。";
            }
        }
        catch (Exception error)
        {
            if (!_exiting)
            {
                _messages.Enqueue(new(ContentImportStage.Failed, error.Message)); DrainMessages();
                _title.Text = "准备游戏内容"; _introduction.Text = "调整目录或转换选项后，可以重新开始。";
                _status.Text = "转换失败，请查看日志，调整后重试。";
                _status.AddThemeColorOverride("font_color", new Color("edb79a")); SetLogVisible(true);
            }
        }
        finally { _cancellation = null; if (!_exiting) SetBusy(false); }
    }

    public override void _Process(double delta) => DrainMessages();

    private void DrainMessages()
    {
        bool changed = false;
        while (_messages.TryDequeue(out var update))
        {
            _recent.Enqueue(update.Message);
            if (_recent.Count > 80) _recent.Dequeue();
            if (_cancellation is not null && update.Stage != _stage) _status.Text = update.Message;
            _stage = update.Stage;
            changed = true;
        }
        if (changed) { _log.Text = string.Join('\n', _recent); _logToggle.Disabled = false; }
    }

    private void ToggleLog() => SetLogVisible(!_log.Visible);
    private void SetLogVisible(bool visible) { _log.Visible = visible; _logToggle.Text = visible ? "收起转换日志" : "展开转换日志"; }

    private void CancelImport()
    {
        if (_cancellation is null) return;
        _cancel.Disabled = true; _status.Text = "正在取消，请等待当前步骤退出……"; _cancellation.Cancel();
    }

    private async Task EnterGame()
    {
        if (_busy || _exiting) return;
        SetBusy(true);
        try { await Continue(); if (!_exiting) QueueFree(); }
        catch (Exception error)
        {
            if (!_exiting) { _messages.Enqueue(new(ContentImportStage.Failed, error.Message)); DrainMessages(); _status.Text = "内容加载失败，请查看日志。"; SetLogVisible(true); }
        }
        finally { if (!_exiting) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy; _select.Disabled = busy || !ImportAvailable;
        _start.Disabled = busy || !ImportAvailable || _source is null;
        _originalTextEdits.Disabled = busy || !ImportAvailable;
        _audio.Disabled = busy || !ImportAvailable; _movies.Disabled = busy || !ImportAvailable;
        _continue.Disabled = busy; _progress.Visible = busy;
        _cancel.Visible = busy && _cancellation is not null; _cancel.Disabled = !busy;
        if (busy) _status.RemoveThemeColorOverride("font_color");
        UpdateMediaOptions();
    }

    public override void _ExitTree() { _exiting = true; _cancellation?.Cancel(); }
}
