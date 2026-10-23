using Godot;
using San9.Application.Persistence;
using San9.Client.Godot.Persistence;

namespace San9.Client.Godot;

public partial class StartMenuView
{
    public SlotSaveStore Store { get; init; } = null!;
    public Func<SlotSaveDocument, Task> LoadGame { get; init; } = null!;
    private readonly SaveLoadBrowser _browser = new();
    private readonly CancellationTokenSource _lifetime = new();
    private TextureScope? _loadTextures;
    private SaveLoadView? _loadView;
    private Control? _loadRoot;
    private bool _loading;

    private async void OpenLoad()
    {
        if (_loading || _loadView is not null) return;
        _loading = true;
        _controls.Hide(); _artwork.MenuVisible = false;
        var token = _lifetime.Token;
        try
        {
            var (catalog, automatic) = await Task.Run(() => (Store.ReadCatalog(_browser.SlotCount), Store.ReadAutoSaveCatalog()), token);
            if (token.IsCancellationRequested) return;
            _browser.SetEntries(catalog); _browser.SetAutoSaves(automatic); _browser.Open(SaveLoadMode.Load);
            _loadTextures = Service.CreateScope();
            _loadTextures.RequestPrefixes("save-load-", "message-", "dialog-");
            _loadView = new SaveLoadView { State = _browser, Textures = _loadTextures, Service = Service,
                Closed = CloseLoad, Requested = LoadSlot };
            _loadRoot = OverlayPage.Create(_loadView, Colors.Black);
            AddChild(_loadRoot);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                _artwork.MenuVisible = true; _controls.Show();
                _hint.Text = "无法读取存档目录：" + error.Message;
                _controls.GetNode<StartMenuButton>("load-game").GrabFocus();
            }
            GD.PushWarning(error.ToString());
        }
        finally { if (!token.IsCancellationRequested) _loading = false; }
    }

    private void CloseLoad()
    {
        if (_loading) return;
        _browser.Close();
        if (_loadRoot is not null) { RemoveChild(_loadRoot); _loadRoot.QueueFree(); }
        _loadRoot = null; _loadView = null;
        _loadTextures?.Dispose(); _loadTextures = null;
        _artwork.MenuVisible = true; _controls.Show();
        _controls.GetNode<StartMenuButton>("load-game").GrabFocus(); RestorePrompt();
    }

    private async void LoadSlot(SaveSlotRequest request)
    {
        if (_loading) return;
        _loading = true;
        var token = _lifetime.Token;
        try
        {
            var document = await Task.Run(() => request.AutoSaveSlot is { } slot
                ? Store.ReadAutoSaveDocument(slot) : Store.ReadDocument(request.Slot), token);
            token.ThrowIfCancellationRequested();
            await LoadGame(document);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
                _loadView!.ShowNotice("载入失败：\n" + (error is SaveException save ? SaveFailureMessages.Describe(save) : error.Message));
            GD.PushWarning(error.ToString());
        }
        finally
        {
            if (!token.IsCancellationRequested) _loading = false;
        }
    }
}
