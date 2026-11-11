using Godot;

namespace San9.Client.Godot;

/// <summary>Shared catalog actions own cancellation, dialogs and save exclusion.</summary>
public partial class SharedCatalogActions : HBoxContainer
{
    public SharedDataEditorPage Page { get; init; } = null!;
    public string Subject { get; init; } = "";
    public string Identity { get; init; } = "";
    public string SuccessMessage { get; init; } = "已更新草稿，尚未保存。";
    public Func<int?> Current { get; init; } = null!;
    public Func<int, bool> CanDelete { get; init; } = null!;
    public string? DeleteUnavailableTooltip { get; init; }
    public Func<int, string> EntryName { get; init; } = null!;
    public Func<int?, CancellationToken, Task<int>> Create { get; init; } = null!;
    public Func<int, CancellationToken, Task> Delete { get; init; } = null!;
    public Action<int?> Select { get; init; } = null!;
    public Func<bool> Commit { get; init; } = () => true;
    public bool Busy { get; private set; }
    private readonly CancellationTokenSource _lifetime = new();
    private Button _copy = null!, _delete = null!;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        var create = ContentEditorControls.ActionButton(this, "新建", () => _ = Run(async token => await Create(null, token)), "New" + Identity);
        _copy = ContentEditorControls.ActionButton(this, "复制", () =>
        {
            if (Current() is int id) _ = Run(async token => await Create(id, token));
        }, "Copy" + Identity);
        _delete = ContentEditorControls.ActionButton(this, "删除", ConfirmDelete, "Delete" + Identity);
        foreach (var button in new[] { create, _copy, _delete }) button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Refresh();
    }

    public void Refresh()
    {
        if (!IsNodeReady()) return;
        _copy.Disabled = Busy || Current() is null;
        _delete.Disabled = Busy || Current() is not int id || !CanDelete(id);
        _delete.TooltipText = _delete.Disabled ? DeleteUnavailableTooltip ?? $"仅能删除自建{Subject}；原有项可编辑、可复制。" : "检查引用后删除，保存全部修改后生效。";
    }

    private void ConfirmDelete()
    {
        if (Current() is not int id || !CanDelete(id) || Busy || !Commit()) return;
        var dialog = new ConfirmationDialog { Title = "删除" + Subject, DialogText = $"删除“{EntryName(id)}”？保存全部修改后生效。",
            OkButtonText = "删除", CancelButtonText = "取消", Exclusive = true };
        dialog.Confirmed += () => { dialog.QueueFree(); _ = Run(async token => { await Delete(id, token); return null; }); };
        dialog.Canceled += () => dialog.QueueFree();
        Page.AddChild(dialog); dialog.PopupCentered(new(560, 180));
    }

    private async Task Run(Func<CancellationToken, Task<int?>> action)
    {
        if (Busy || !Page.CanProcess() || !Commit()) return;
        var token = _lifetime.Token;
        Busy = true; Refresh(); Page.ProcessMode = ProcessModeEnum.Disabled;
        Page.ReportStatus("正在检查身份与引用…", false);
        string? error = null;
        int? selection = null;
        try { selection = await action(token); token.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or System.Text.Json.JsonException or OverflowException) { error = failure.Message; }
        finally
        {
            if (!token.IsCancellationRequested) { Busy = false; Page.ProcessMode = ProcessModeEnum.Inherit; Refresh(); }
        }
        if (token.IsCancellationRequested) return;
        if (error is not null) { ShowError(error); return; }
        Select(selection); Page.ReportStatus(SuccessMessage, false);
    }

    private void ShowError(string error)
    {
        Page.ReportStatus("操作未完成，当前草稿已保留。", true);
        var dialog = new AcceptDialog { Title = "操作未完成", OkButtonText = "关闭", Exclusive = true };
        var scroll = new ScrollContainer { CustomMinimumSize = new(620, 200), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        dialog.AddChild(scroll);
        scroll.AddChild(new Label { Text = error, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree();
        Page.AddChild(dialog); dialog.PopupCentered(new(680, 300));
    }

    public override void _ExitTree() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
