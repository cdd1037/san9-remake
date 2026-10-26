using Godot;

namespace San9.Client.Godot;

public partial class WorkflowLifecycleVerification
{
    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task KeyPress(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = false }, true);
        await Frame();
    }

    private async Task Click(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await Frame();
    }

    private static Button ModalButton(Control parent, Vector2 position)
    {
        var button = new Button { Position = position, Size = new(160, 40) };
        parent.AddChild(button);
        return button;
    }

    private async Task CheckModals()
    {
        CheckModalInitialization();
        var scene = new Node(); AddChild(scene);
        var background = new ModalVerificationWindow { Size = new(1024, 768) };
        scene.AddChild(background);
        var backgroundButton = ModalButton(background, new(16, 16));
        backgroundButton.GrabFocus();
        var host = new StrategyOverlayHost { Size = background.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        scene.AddChild(host); host.AddBackground(background);
        var load = new ModalVerificationWindow { Size = background.Size, Visible = false };
        host.AddChild(load);
        var slot = ModalButton(load, new(8, 72));
        int loaded = 0; slot.Pressed += () => loaded++;
        var title = new ModalVerificationWindow { Size = background.Size };
        host.AddChild(title);
        var titleButton = ModalButton(title, new(400, 350));
        titleButton.GrabFocus();
        await Frame();
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                load.Show();
                int titleInputs = title.Inputs, titleKeys = title.Keys, backgroundInputs = background.Inputs, titleFrames = title.Frames;
                Check(load.ZIndex > title.ZIndex, "Reused load window is visually behind the terminal title.");
                await Click(new(50, 90));
                await KeyPress(Key.F6);
                Check(loaded == attempt + 1, "Terminal title intercepted the visible save row.");
                Check(title.Inputs == titleInputs && title.Keys == titleKeys && background.Inputs == backgroundInputs,
                    "Covered window received raw input before GUI dispatch.");
                Check(title.Frames > titleFrames, "Input isolation paused covered presentation updates.");
                load.Hide();
                Check(GetViewport().GuiGetFocusOwner() == titleButton, "Closing load did not restore terminal menu focus.");
            }
            load.Show(); slot.GrabFocus();
            var notice = new ModalVerificationWindow { Size = background.Size };
            load.AddChild(notice);
            var close = ModalButton(notice, new(430, 430));
            close.GrabFocus();
            int loadInputs = load.Inputs;
            for (int i = 0; i < 4; i++)
            {
                await KeyPress(Key.Tab);
                Check(GetViewport().GuiGetFocusOwner() == close, "Tab escaped the nested notice.");
            }
            int latePresses = 0;
            var late = ModalButton(load, new(200, 72)); late.Pressed += () => latePresses++;
            Check(late.FocusMode == Control.FocusModeEnum.None, "A new covered control remained focusable.");
            Check(late.GetMouseFilterWithOverride() == Control.MouseFilterEnum.Ignore, "A new covered control retained mouse input.");
            notice.Size = new(64, 64);
            await Click(new(220, 90));
            await KeyPress(Key.Space);
            Check(latePresses == 0 && load.Inputs == loadInputs, "Nested notice leaked keyboard input to its owner.");
            // Rebuilding a covered body must not place it above an existing child modal.
            Check(notice.ZIndex > load.ZIndex, "Covered body insertion changed the active modal.");
            load.RemoveChild(notice); notice.Free();
            Check(GetViewport().GuiGetFocusOwner() == slot && slot.FocusMode == Control.FocusModeEnum.All,
                "Nested modal did not restore the selected row and its focus mode.");
            Check(late.FocusMode == Control.FocusModeEnum.All, "A late control did not recover its original focus mode.");
            Check(late.GetMouseFilterWithOverride() != Control.MouseFilterEnum.Ignore, "Closing a modal did not restore mouse input.");
            title.Free();
            Check(GetViewport().GuiGetFocusOwner() == slot, "Removing a covered window stole active focus.");
            load.Hide();
            Check(!host.HasModal && background.IsProcessingInput(), "Hidden windows retained modal input ownership.");
            Check(GetViewport().GuiGetFocusOwner() == backgroundButton, "Invalid return focus did not fall back to the scene's saved focus.");
            var router = new ModalVerificationRouter { Size = background.Size };
            host.AddChild(router);
            var dialog = new ModalVerificationWindow { Size = background.Size };
            router.AddChild(dialog);
            router.Back = dialog.Hide;
            await KeyPress(Key.Escape);
            Check(!dialog.Visible && router.Backs == 1, "Ancestor navigation did not route to its active child.");
            var unrelated = new ModalVerificationWindow { Size = background.Size }; host.AddChild(unrelated);
            await KeyPress(Key.Escape);
            Check(router.Backs == 1, "Covered navigation router received input from another window branch.");
            scene.Free();
            Check(!GodotObject.IsInstanceValid(host), "Scene teardown retained the modal host.");
        }
        finally { if (GodotObject.IsInstanceValid(scene)) scene.Free(); }
    }

    private void CheckModalInitialization()
    {
        var host = new StrategyOverlayHost(); AddChild(host);
        try
        {
            var owner = new ModalVerificationInitializingWindow { Size = new(1024, 768) };
            host.AddChild(owner);
            var child = owner.GetChildren().OfType<ModalVerificationWindow>().Single();
            owner.RemoveChild(child); child.Free();
            Check(owner.FocusMode == Control.FocusModeEnum.All && !owner.IsProcessingInput(),
                "Closing an initial child restored input settings captured before its owner's Ready callback.");
            owner.FocusMode = Control.FocusModeEnum.None;
            var fallback = ModalButton(owner, new(16, 16));
            var popup = new Control { Size = new(200, 200) }; owner.AddChild(popup);
            var popupButton = ModalButton(popup, new(40, 40)); popupButton.GrabFocus();
            ModalStack.Find(owner)!.Register(popup);
            owner.RemoveChild(popup); popup.Free();
            Check(GetViewport().GuiGetFocusOwner() == fallback,
                "Closing a popup restored focus to a control in the exiting subtree.");
        }
        finally { host.Free(); }
    }
}

internal partial class ModalVerificationWindow : ModalView
{
    public int Inputs { get; private set; }
    public int Keys { get; private set; }
    public int Frames { get; private set; }
    public override void _Input(InputEvent input) { if (IsVisibleInTree()) Inputs++; }
    public override void _UnhandledKeyInput(InputEvent input) { if (IsVisibleInTree()) Keys++; }
    public override void _Process(double delta) => Frames++;
    public override void _GuiInput(InputEvent input) => AcceptEvent();
}

internal partial class ModalVerificationRouter : ModalVerificationWindow, IModalInputRouter
{
    public Action Back { get; set; } = null!;
    public int Backs { get; private set; }
    public override void _Input(InputEvent input)
    {
        base._Input(input);
        if (IsVisibleInTree() && input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); Backs++; Back(); }
    }
}

internal partial class ModalVerificationInitializingWindow : ModalVerificationWindow
{
    public override void _Ready()
    {
        AddChild(new ModalVerificationWindow { Size = Size });
        FocusMode = FocusModeEnum.All;
        SetProcessInput(false);
    }
}
