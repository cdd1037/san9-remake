using System.Text.Json;
using Godot;
using San9.Application.Authoring;
using San9.Content.Images;
using San9.Content.Packages;

namespace San9.Client.Godot;

public partial class PortraitEditor
{
    private IEnumerable<(string Key, string Name)> ReadCurrentUsers()
    {
        var officers = JsonSerializer.Deserialize<OfficerDefaultsCatalog>(
            File.ReadAllBytes(PlayerDefinitionFiles.ActivePath(_officerDefaultsPath)), PackageFiles.JsonOptions)
            ?? throw new InvalidDataException("人物资料为空。");
        ContentStructure.Validate(officers);
        if (officers.SchemaVersion != OfficerDefaultsCatalog.CurrentSchemaVersion)
            throw new InvalidDataException("人物资料版本不符，请重建内容包。");
        if (CreatedOfficersPath is not null)
        {
            byte[]? bytes;
            try { bytes = File.ReadAllBytes(CreatedOfficersPath); }
            catch (FileNotFoundException) { bytes = null; }
            catch (DirectoryNotFoundException) { bytes = null; }
            officers = PlayerOfficerFiles.Merge(officers, bytes);
        }
        foreach (var person in officers.Officers)
            if (person.PortraitKey is { } key) yield return (key, person.Name);
    }
    private void DeletePortrait()
    {
        FinishName(); ApplyCoordinates(); _canvas.CancelGesture(); CancelAnalysis();
        if (_workspace is null || _current is null || !_workspace.IsAuthored(_current)) return;
        string key = _current;
        try
        {
            var users = ReadCurrentUsers().Where(p => p.Key == key).Select(p => p.Name).Distinct().ToArray();
            if (users.Length > 0) { ShowError("请先为以下使用者更换头像：" + string.Join("、", users)); return; }
        }
        catch (Exception error) { ShowError("无法核对头像使用者：" + error.Message); return; }
        var dialog = new ConfirmationDialog { Title = "删除头像", DialogText = $"删除“{_workspace.Name(key)}”？",
            OkButtonText = "删除", CancelButtonText = "取消" };
        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            try
            {
                var users = ReadCurrentUsers().Where(p => p.Key == key).Select(p => p.Name).Distinct().ToArray();
                _workspace.Delete(key, users); RefreshSelection();
            }
            catch (Exception error) { ShowError("删除失败：" + error.Message); }
        };
        dialog.Canceled += () => dialog.QueueFree(); AddChild(dialog); dialog.PopupCentered(new(440, 160));
    }
    private void RestoreOriginal()
    {
        FinishName(); ApplyCoordinates(); _canvas.CancelGesture(); CancelAnalysis();
        if (_workspace is null || _current is null || _workspace.IsAuthored(_current)) return;
        _workspace.RestoreOriginal(_current); RefreshSelection();
    }
    private void ReplaceImage()
    {
        FinishName(); ApplyCoordinates(); _canvas.CancelGesture(); CancelAnalysis();
        if (_workspace is null || _current is null || _importCancellation is not null) return;
        string key = _current;
        var dialog = new FileDialog { Title = "替换头像图片", FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.png,*.jpg,*.jpeg,*.webp; 图片"], UseNativeDialog = true };
        dialog.FileSelected += path => { dialog.QueueFree(); ImportOptions([path], key); };
        dialog.Canceled += () => dialog.QueueFree(); AddChild(dialog); dialog.PopupCentered(new(800, 560));
    }
    private async Task ReplaceImage(string path, int size, string key)
    {
        if (_workspace is null || _lifetime.IsCancellationRequested) return;
        try
        {
            _workspace.Replace(key, ReadImportImage(path, size));
            _current = key; RefreshSelection(); await DetectFace();
        }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) ShowError("替换失败：" + error.Message); }
    }
    private void Import()
    {
        FinishName();
        if (_workspace is null || _importCancellation is not null) return;
        CancelAnalysis(); _canvas.CancelGesture();
        var dialog = new FileDialog { Title = "新增方形头像", FileMode = FileDialog.FileModeEnum.OpenFiles,
            Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.png,*.jpg,*.jpeg,*.webp; 图片"], UseNativeDialog = true };
        AddChild(dialog);
        dialog.FilesSelected += paths =>
        {
            try { ImportOptions(paths); }
            catch (Exception error) { ShowError("导入失败：" + error.Message); }
            finally { dialog.QueueFree(); }
        };
        dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCentered(new(800, 560));
    }
    private void ImportOptions(string[] paths, string? replacementKey = null)
    {
        if (paths.Length == 0) return;
        var dialog = new ConfirmationDialog { Title = replacementKey is null ? "导入头像" : "替换头像",
            OkButtonText = replacementKey is null ? "导入并自动取景" : "替换并自动取景", CancelButtonText = "取消" };
        var fields = new VBoxContainer(); fields.AddThemeConstantOverride("separation", 12); dialog.AddChild(fields);
        fields.AddChild(new Label { Text = $"已选择 {paths.Length} 张图片", TooltipText = string.Join("\n", paths.Select(Path.GetFileName)) });
        var original = new CheckBox { Text = "保留原图尺寸", ButtonPressed = true, SizeFlagsHorizontal = SizeFlags.ShrinkBegin }; fields.AddChild(original);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); fields.AddChild(row);
        row.AddChild(new Label { Text = "保存边长" });
        var size = new SpinBox { MinValue = 16, MaxValue = 16384, Step = 1, Rounded = true,
            Value = 512, Editable = false, CustomMinimumSize = new(140, 34), Suffix = "px" };
        row.AddChild(size);
        original.Toggled += keep => size.Editable = !keep;
        var overwrite = new CheckBox { Text = "同名覆盖", Visible = replacementKey is null, SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            TooltipText = "按文件名匹配左侧图片名，包括固有头像编号（忽略扩展名和大小写）；保留原头像身份与使用者。多个同名目标会跳过。" };
        fields.AddChild(overwrite);
        dialog.Confirmed += async () =>
        {
            size.Apply();
            int target = original.ButtonPressed ? 0 : (int)size.Value;
            dialog.QueueFree();
            if (replacementKey is null) await ImportImages(paths, target, overwrite.ButtonPressed);
            else await ReplaceImage(paths[0], target, replacementKey);
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered(new(440, 240));
    }
    private static PngImage ReadImportImage(string path, int size)
    {
        using var image = new Image();
        if (image.Load(path) != Error.Ok) throw new InvalidDataException("无法读取图片。");
        if (image.GetWidth() != image.GetHeight() || image.GetWidth() < 16)
            throw new InvalidDataException("请选择至少16×16的方形母图；不自动裁切或拉伸图片。");
        image.Convert(Image.Format.Rgb8);
        if (size != 0 && size != image.GetWidth()) image.Resize(size, size, Image.Interpolation.Lanczos);
        return new(image.GetWidth(), image.GetHeight(), 3, image.GetData());
    }
    private (PortraitDefinition Portrait, string Path, byte[]? Pending) ImageSource(string key)
    {
        var catalog = _workspace!.Catalog;
        var portrait = _workspace.Portrait(key);
        string path = catalog.ImageSources.TryGetValue(portrait.Image.Path, out var source)
            ? source : PackageFiles.Resolve(_textureRoot, portrait.Image.Path);
        return (portrait, path, _workspace.PendingImage(key));
    }
    private async Task ImportImages(string[] paths, int size, bool overwriteSameName)
    {
        if (_workspace is null || _importCancellation is not null || _lifetime.IsCancellationRequested) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _importCancellation = cancellation;
        var progress = new ConfirmationDialog { Title = "导入头像", CancelButtonText = "停止",
            DialogText = "正在导入…" };
        progress.GetOkButton().Hide();
        progress.Canceled += cancellation.Cancel;
        AddChild(progress); progress.PopupCentered(new(540, 180));
        var issues = new List<string>();
        string? lastImported = null;
        int imported = 0;
        try
        {
            CancelAnalysis();
            var batch = new PortraitImportBatch(_workspace, overwriteSameName);
            for (int i = 0; i < paths.Length; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                string path = paths[i], name = Path.GetFileNameWithoutExtension(path);
                progress.DialogText = $"正在导入 {i + 1}/{paths.Length}：{Path.GetFileName(path)}";
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                cancellation.Token.ThrowIfCancellationRequested();
                string key;
                try
                {
                    key = batch.Import(name, ReadImportImage(path, size));
                    lastImported = key; imported++;
                }
                catch (Exception error) { issues.Add($"{Path.GetFileName(path)}：导入失败，{error.Message}"); continue; }
                try
                {
                    var (portrait, sourcePath, pending) = ImageSource(key);
                    var faces = await _detector.DetectAsync(() => ReadAnalysisImage(portrait, sourcePath, pending), cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (faces.Length == 1)
                    {
                        var suggestion = AutomaticPortraitFraming.Suggest(faces[0]);
                        _workspace.ApplyAutomaticFace(key, suggestion.FaceAnchor);
                        if (suggestion.IsLimited) issues.Add($"{name}：取景已到边界，需手动微调。");
                    }
                    else issues.Add(faces.Length == 0 ? $"{name}：未识别人脸，已保留默认取景。"
                        : $"{name}：存在多个人脸，请选择该头像后点击自动取景，点选目标人脸。");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception error) { issues.Add($"{name}：图片已导入，自动取景失败，{error.Message}"); }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            _importCancellation = null;
            progress.QueueFree();
        }
        if (_lifetime.IsCancellationRequested) return;
        if (lastImported is not null)
        {
            _current = lastImported; _search.Text = ""; _modified.ButtonPressed = false;
            RefreshSelection();
        }
        ShowImportResult(imported, paths.Length, cancellation.IsCancellationRequested, issues);
    }
    private void ShowImportResult(int imported, int total, bool stopped, List<string> issues)
    {
        var result = new AcceptDialog { Title = stopped ? "导入已停止" : "导入完成", OkButtonText = "关闭" };
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); result.AddChild(column);
        column.AddChild(new Label { Text = $"已导入 {imported}/{total} 张，尚未保存。" });
        if (issues.Count > 0)
        {
            var scroll = new ScrollContainer { CustomMinimumSize = new(540, 240),
                SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            column.AddChild(scroll);
            scroll.AddChild(new Label { Text = string.Join("\n\n", issues), AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill });
        }
        result.Confirmed += () => result.QueueFree(); result.Canceled += () => result.QueueFree();
        AddChild(result); result.PopupCentered(new(580, issues.Count > 0 ? 360 : 150));
    }
}
