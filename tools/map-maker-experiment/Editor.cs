using Godot;
using San9.TerrainEditor.Model;

namespace San9.TerrainEditor;

public partial class Editor : Control
{
    private TerrainDocument document = Demo();
    private TerrainDocument? startupExample;
    private EditHistory history = new();
    private TerrainPreview? preview;
    private Task<TerrainPreview>? loading;
    private Task<PreviewFrame>? rendering;
    private long revision, renderedRevision = -1, requestedRevision;
    private bool dirty, fitAfterRender;
    private int previousCell;
    private string packagePath = "", projectPath = "", outputDirectory = "";
    private TerrainView view = null!;
    private Label status = null!, info = null!, rangeHint = null!;
    private OptionButton type = null!, variant = null!, mode = null!;
    private SpinBox elevation = null!, radius = null!, width = null!, height = null!;
    private Button undo = null!, redo = null!;
    private FileDialog fileDialog = null!, packageDialog = null!;
    private ConfirmationDialog discard = null!;
    private Action? confirmed;
    private bool verificationStarted;

    public override void _Ready()
    {
        outputDirectory = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../local/generated/terrain-editor"));
        System.IO.Directory.CreateDirectory(outputDirectory);
        packagePath = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../local/generated/content/shared/textures"));
        string[] args = OS.GetCmdlineUserArgs();
        int packageArg = Array.IndexOf(args, "--texture-package");
        if (packageArg >= 0 && packageArg + 1 < args.Length) packagePath = System.IO.Path.GetFullPath(args[packageArg + 1]);
        Theme = new Theme { DefaultFontSize = 16, DefaultFont = new SystemFont { FontNames = ["Microsoft YaHei", "Noto Sans CJK SC", "sans-serif"] } };
        BuildUi(); SyncBrush();
        int projectArg = Array.IndexOf(args, "--project");
        string initialProject = projectArg >= 0 && projectArg + 1 < args.Length ? args[projectArg + 1] :
            System.IO.Path.Combine(outputDirectory, "luoyang-north-bank.terrain.json");
        if (projectArg >= 0 || System.IO.File.Exists(initialProject))
        {
            try
            {
                Replace(TerrainDocument.Load(initialProject)); startupExample = document.Snapshot(); projectPath = System.IO.Path.GetFullPath(initialProject);
                GetWindow().Title = "山河 · 地貌编辑实验 / " + System.IO.Path.GetFileNameWithoutExtension(projectPath);
            }
            catch (Exception error) { status.Text = "打开初始工程失败：" + error.Message; return; }
        }
        GetTree().AutoAcceptQuit = false;
        LoadPackage();
    }

    private void BuildUi()
    {
        var background = new ColorRect { Color = new Color("202d26"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
        var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        var stack = new VBoxContainer(); margin.AddChild(stack);
        var bar = new HBoxContainer(); stack.AddChild(bar);
        bar.AddChild(new Label { Text = "山河 / 地貌编辑实验", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        AddButton(bar, "素材包…", () => packageDialog.PopupCentered(new Vector2I(950, 600)));
        AddButton(bar, "打开…", OpenDialog);
        AddButton(bar, "保存…", SaveDialog);
        AddButton(bar, "导出预览 PNG", ExportImage);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; stack.AddChild(body);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(285, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; body.AddChild(scroll);
        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; panel.AddThemeConstantOverride("separation", 12); scroll.AddChild(panel);
        panel.AddChild(new Label { Text = "每格只有三个字段" });
        type = new OptionButton();
        for (int i = 0; i < TerrainRecipe.All.Length; i++) type.AddItem($"地形 {i:D2}" + (i == 11 ? " · 桥地表" : i == 12 ? " · 水岸地表" : ""), i);
        AddField(panel, "类型 / 转换包地形编号", type); type.ItemSelected += _ => SyncBrush();
        elevation = Number(0, 7, 1); AddField(panel, "高度", elevation);
        variant = new OptionButton(); AddField(panel, "纹理变体", variant);
        rangeHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; panel.AddChild(rangeHint);
        panel.AddChild(new HSeparator());
        mode = new OptionButton(); foreach (string text in new[] { "涂刷三个字段", "只刷类型", "只刷高度", "只刷变体" }) mode.AddItem(text);
        AddField(panel, "应用方式", mode);
        radius = Number(0, 5, 0); AddField(panel, "笔刷半径 / 格", radius); radius.ValueChanged += value => view.BrushRadius = (int)value;
        var historyBar = new HBoxContainer(); panel.AddChild(historyBar);
        undo = AddButton(historyBar, "撤销", () => Travel(false)); redo = AddButton(historyBar, "重做", () => Travel(true)); UpdateHistory();
        AddButton(panel, "适应视图", () => view.Fit());
        panel.AddChild(new HSeparator());
        var dimensions = new HBoxContainer(); panel.AddChild(dimensions);
        width = Number(4, 64, 32); height = Number(4, 64, 32);
        dimensions.AddChild(new Label { Text = "宽" }); dimensions.AddChild(width);
        dimensions.AddChild(new Label { Text = "高" }); dimensions.AddChild(height);
        AddButton(panel, "新建平地", () => Guard(() => Replace(TerrainDocument.Create((int)width.Value, (int)height.Value))));
        AddButton(panel, "重置地貌示例", () => Guard(() => Replace(startupExample?.Snapshot() ?? Demo())));
        panel.AddChild(new Label { Text = "左键涂刷 · Ctrl+左键吸取\n右键/中键拖动 · 滚轮缩放\nCtrl+Z 撤销 · Ctrl+Shift+Z 重做\nCtrl+S 保存 · Ctrl+O 打开", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        panel.AddChild(new Label { Text = "固定春季，使用真实转换素材。\n交界与高差自动生成。\n不含城市、桥对象、地域和玩法。\n素材范围外的单字段涂刷会跳过。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        view = new TerrainView { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(view); view.PaintCell = Paint; view.StrokeEnded = EndStroke; view.SampleCell = Sample; view.HoverCell = Hover;
        info = new Label { Text = "将鼠标移到地貌上查看三个字段" }; stack.AddChild(info);
        status = new Label { Text = "准备素材…", AutowrapMode = TextServer.AutowrapMode.WordSmart }; stack.AddChild(status);
        fileDialog = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.json ; 地貌工程"], CurrentDir = outputDirectory, UseNativeDialog = true };
        AddChild(fileDialog); fileDialog.FileSelected += SelectFile;
        packageDialog = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenDir, CurrentDir = System.IO.Path.GetDirectoryName(packagePath), UseNativeDialog = true };
        AddChild(packageDialog); packageDialog.DirSelected += path => { packagePath = path; LoadPackage(); };
        discard = new ConfirmationDialog { DialogText = "当前工程有未保存修改。继续将放弃这些修改；可取消后先保存。", OkButtonText = "继续", CancelButtonText = "取消" };
        AddChild(discard); discard.Confirmed += () => { Action? action = confirmed; confirmed = null; action?.Invoke(); };
    }

    private static Button AddButton(Node parent, string text, Action action)
    {
        var button = new Button { Text = text }; parent.AddChild(button); button.Pressed += action; return button;
    }
    private static SpinBox Number(int min, int max, int value) => new() { MinValue = min, MaxValue = max, Step = 1, Value = value, CustomMinimumSize = new Vector2(84, 0) };
    private static void AddField(Node panel, string title, Control field) { panel.AddChild(new Label { Text = title }); panel.AddChild(field); }
    private void SyncBrush()
    {
        TerrainRecipe rule = TerrainRecipe.All[type.Selected];
        elevation.MinValue = rule.MinHeight; elevation.MaxValue = rule.MaxHeight;
        elevation.Value = Math.Clamp(elevation.Value, rule.MinHeight, rule.MaxHeight);
        int previous = variant.Selected < 0 ? 0 : variant.GetSelectedId(); variant.Clear();
        foreach (int value in TerrainRecipe.VariantsFor(type.Selected)) variant.AddItem(value.ToString(), value);
        int index = variant.GetItemIndex(previous); variant.Select(index >= 0 ? index : 0);
        rangeHint.Text = $"此素材高度 {rule.MinHeight}–{rule.MaxHeight}；变体 {string.Join(" / ", TerrainRecipe.VariantsFor(type.Selected))}";
    }
    private TerrainCell Brush => new(type.Selected, (int)elevation.Value, variant.GetSelectedId());

    private void Paint(int index, bool first)
    {
        if (first) { history.Begin(); previousCell = index; }
        TerrainCell brush = Brush;
        TerrainCell Change(TerrainCell old) => mode.Selected switch
        {
            1 => old with { Type = brush.Type }, 2 => old with { Height = brush.Height },
            3 => old with { Variant = brush.Variant }, _ => brush
        };
        if (history.Paint(document, previousCell % document.Width, previousCell / document.Width,
            index % document.Width, index / document.Width, (int)radius.Value, Change)) Changed();
        previousCell = index; Hover(index);
    }
    private void EndStroke() { history.End(); UpdateHistory(); }
    private void Changed() { revision++; dirty = true; status.Text = "已修改 · 正在刷新地貌"; }
    private void UpdateHistory() { undo.Disabled = !history.CanUndo; redo.Disabled = !history.CanRedo; }
    private void Travel(bool forward)
    {
        view.FinishStroke();
        if (forward ? !history.CanRedo : !history.CanUndo) return;
        if (forward) history.Redo(document); else history.Undo(document);
        Changed(); UpdateHistory();
    }
    private void Sample(int index)
    {
        TerrainCell c = document.Cells[index]; type.Select(c.Type); SyncBrush();
        elevation.Value = c.Height; variant.Select(variant.GetItemIndex(c.Variant)); Hover(index);
        status.Text = "已吸取三个字段";
    }
    private void Hover(int index)
    {
        if (index < 0 || index >= document.Cells.Length) { info.Text = "地图外"; return; }
        TerrainCell c = document.Cells[index];
        info.Text = $"格 ({index % document.Width}, {index / document.Width})   类型 {c.Type}   高度 {c.Height}   变体 {c.Variant}   |   {document.Width} × {document.Height}";
    }

    private void LoadPackage()
    {
        if (loading is not null || rendering is not null) { status.Text = "正在准备预览，请稍后再切换素材包。"; return; }
        status.Text = "加载转换后的地貌纹理和遮罩…";
        loading = Task.Run(() => new TerrainPreview(packagePath));
    }
    public override void _Process(double delta)
    {
        if (loading is { IsCompleted: true })
        {
            try { preview = loading.GetAwaiter().GetResult(); revision++; }
            catch (Exception error) { status.Text = "素材加载失败，请用“素材包…”选择含 textures.json 的目录：" + error.Message; GD.PrintErr(status.Text); }
            loading = null;
        }
        if (rendering is { IsCompleted: true })
        {
            try
            {
                PreviewFrame frame = rendering.GetAwaiter().GetResult();
                if (requestedRevision == revision)
                {
                    view.DocumentWidth = document.Width; view.Present(frame); renderedRevision = revision;
                    if (fitAfterRender) { view.Fit(); fitAfterRender = false; }
                    status.Text = $"{(dirty ? "未保存" : "就绪")} · {document.Width}×{document.Height} · 地貌合成 {frame.Milliseconds:F1} ms · 春季";
                }
            }
            catch (Exception error) { renderedRevision = revision; status.Text = "预览失败：" + error.Message; GD.PrintErr(status.Text); }
            rendering = null;
        }
        if (preview is not null && loading is null && rendering is null && renderedRevision != revision)
        {
            TerrainDocument snapshot = document.Snapshot(); TerrainPreview renderer = preview;
            requestedRevision = revision; rendering = Task.Run(() => renderer.Render(snapshot));
        }
        if (!verificationStarted && view.Frame is not null && renderedRevision == revision && OS.GetCmdlineUserArgs().Contains("--verify"))
        {
            verificationStarted = true; _ = Verify();
        }
        if (!verificationStarted && view.Frame is not null && renderedRevision == revision && OS.GetCmdlineUserArgs().Contains("--capture"))
        {
            verificationStarted = true; _ = Capture();
        }
    }
    private void Guard(Action action)
    {
        view.FinishStroke();
        if (!dirty) action(); else { confirmed = action; discard.PopupCentered(); }
    }
    private void Replace(TerrainDocument next)
    {
        document = next; projectPath = ""; history = new(); width.Value = next.Width; height.Value = next.Height;
        revision++; dirty = false; fitAfterRender = true; UpdateHistory();
    }
    private void OpenDialog() => Guard(() => { fileDialog.FileMode = FileDialog.FileModeEnum.OpenFile; fileDialog.PopupCentered(new Vector2I(950, 650)); });
    private void SaveDialog()
    {
        view.FinishStroke(); fileDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
        fileDialog.CurrentFile = string.IsNullOrEmpty(projectPath) ? "terrain.json" : System.IO.Path.GetFileName(projectPath);
        fileDialog.PopupCentered(new Vector2I(950, 650));
    }
    private void SelectFile(string path)
    {
        try
        {
            if (fileDialog.FileMode == FileDialog.FileModeEnum.OpenFile) Replace(TerrainDocument.Load(path));
            else { document.Save(path); dirty = false; }
            projectPath = path; status.Text = "工程：" + path;
        }
        catch (Exception error) { status.Text = "操作失败：" + error.Message; }
    }
    private void ExportImage()
    {
        if (view.Frame is not { } frame || renderedRevision != revision) { status.Text = "请等待当前修改完成预览。"; return; }
        string path = System.IO.Path.Combine(outputDirectory, "terrain-preview.png");
        using var image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Pixels);
        Error error = image.SavePng(path); status.Text = error == Error.Ok ? "已导出：" + path : "导出失败：" + error;
    }
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Z: Travel(key.ShiftPressed); break;
            case Key.S: SaveDialog(); break;
            case Key.O: OpenDialog(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Guard(() => GetTree().Quit());
    }

    private static TerrainDocument Demo()
    {
        TerrainDocument d = TerrainDocument.Create();
        for (int y = 0; y < d.Height; y++) for (int x = 0; x < d.Width; x++)
        {
            int distance = Math.Max(Math.Abs(x - 12), Math.Abs(y - 12));
            int h = Math.Max(1, 6 - distance / 2);
            d.Cells[y * d.Width + x] = x > 23 ? new(9, 1, (y / 5) % 2 == 0 ? 0 : 3) : new(x < 8 ? 2 : 0, h, (x / 5 + y / 7) % 3);
        }
        return d;
    }
    private async Task WaitPreview()
    {
        for (int i = 0; i < 600; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (renderedRevision == revision && rendering is null) return;
        }
        throw new TimeoutException("Preview did not finish.");
    }
    private async Task Verify()
    {
        try
        {
            int target = (document.Height / 2) * document.Width + document.Width / 2;
            TerrainCell before = document.Cells[target];
            byte[] beforePixels = view.Frame!.Pixels;
            type.Select(0); SyncBrush(); elevation.Value = before.Height == 7 ? 1 : 7; variant.Select(before.Variant == 2 ? 0 : 2);
            TerrainCell expected = Brush;
            Vector2 local = view.VisiblePoint(target) ?? throw new InvalidOperationException("Target is invisible.");
            Vector2 point = view.GlobalPosition + local;
            Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (document.Cells[target] != expected) throw new InvalidOperationException("Pointer paint failed.");
            await WaitPreview();
            if (view.Frame!.Pixels.SequenceEqual(beforePixels)) throw new InvalidOperationException("Paint did not change preview pixels.");
            Travel(false); if (document.Cells[target] != before) throw new InvalidOperationException("Undo failed.");
            await WaitPreview();
            if (!view.Frame!.Pixels.SequenceEqual(beforePixels)) throw new InvalidOperationException("Undo did not restore preview pixels.");
            Travel(true); if (document.Cells[target] != expected) throw new InvalidOperationException("Redo failed.");
            string path = System.IO.Path.Combine(outputDirectory, "verification.terrain.json"); document.Save(path);
            if (!document.Cells.SequenceEqual(TerrainDocument.Load(path).Cells)) throw new InvalidOperationException("File roundtrip failed.");
            await WaitPreview(); ExportImage();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screenshot = GetViewport().GetTexture().GetImage();
            if (screenshot.SavePng(System.IO.Path.Combine(outputDirectory, "editor.png")) != Error.Ok) throw new IOException("Screenshot save failed.");
            GD.Print($"TERRAIN_EDITOR_VERIFY_OK render_ms={view.Frame!.Milliseconds:F1} cells={document.Cells.Length}");
            dirty = false; GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private async Task Capture()
    {
        try
        {
            ExportImage();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screenshot = GetViewport().GetTexture().GetImage();
            if (screenshot.SavePng(System.IO.Path.Combine(outputDirectory, "sample-editor.png")) != Error.Ok) throw new IOException("Screenshot save failed.");
            GD.Print($"TERRAIN_EDITOR_CAPTURE_OK project={projectPath} cells={document.Cells.Length}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }
}
