using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>Session-scoped dependencies and output ports, never another workflow's mutable state.</summary>
public sealed record CommandWorkflowServices(GameSession Session, MapWorld World, TextCatalog Texts,
    TextureService Textures, TextureScope Scope, InterfaceSkin Skin, CommandPresentation Presentation,
    StrategyOverlayHost OverlayHost, CommandTargetFlow Targets, Action<string> Notice,
    Action<IReadOnlyList<CommandMessage>, StrategySnapshot, Action?> Narrate,
    Func<MapMessageRecord, bool> Record, Action ClearMapSelection);

public abstract partial class CommandWorkflow : Node
{
    public CommandWorkflowServices Services { get; init; } = null!;
    protected GameSession Session => Services.Session;
    protected MapWorld World => Services.World;
    protected TextCatalog Texts => Services.Texts;
    protected TextureService Textures => Services.Textures;
    protected TextureScope Scope => Services.Scope;
    protected InterfaceSkin Skin => Services.Skin;
    protected CommandPresentation CommandPresentation => Services.Presentation;
    protected CommandTargetFlow Targets => Services.Targets;
    private WorkflowWindows? _windows;
    protected bool HasWindow => _windows is { IsEmpty: false };
    protected void ShowWindow(Control view, Action release) =>
        (_windows ??= new(Services.OverlayHost)).Show(view, () => { Targets.ReleaseOwner(view); release(); });
    protected void CloseWindow(Control view)
    {
        if (_windows is not null) _windows.Close(view);
        else view.QueueFree();
    }
    protected void Notice(string text) => Services.Notice(text);
    protected void OpenNarration(IReadOnlyList<CommandMessage> messages, StrategySnapshot view, Action? completed = null) =>
        Services.Narrate(messages, view, completed);
    protected bool PresentMapRecord(MapMessageRecord record) => Services.Record(record);
    public abstract void Close();
    public override void _ExitTree()
    {
        _windows?.Dispose();
        Close();
    }
}
