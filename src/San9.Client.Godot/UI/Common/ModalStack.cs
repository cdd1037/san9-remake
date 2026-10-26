using Godot;

namespace San9.Client.Godot;

/// <summary>One scene owns modal ordering and input isolation; ordinary processing never pauses.</summary>
internal partial class ModalStack : Node
{
    private sealed class Window(ModalStack stack, Control view) : IDisposable
    {
        public Control View { get; } = view;
        public Control? ReturnFocus { get; private set; } = stack.GetViewport().GuiGetFocusOwner();
        public Control? LastFocus { get; set; }
        public Control.MouseBehaviorRecursiveEnum MouseBehavior { get; } = view.MouseBehaviorRecursive;

        public void Attach()
        {
            View.VisibilityChanged += Changed;
            View.TreeExiting += Exiting;
        }

        private void Changed()
        {
            if (View.IsVisibleInTree() && stack.IsReady(View))
            {
                ReturnFocus = stack.GetViewport().GuiGetFocusOwner();
                stack.BringForward(View);
            }
            stack.Refresh();
        }

        private void Exiting()
        {
            Dispose();
            stack._leaving.Add(View);
            stack._windows.Remove(View);
            stack.Refresh();
        }

        public void Dispose()
        {
            View.VisibilityChanged -= Changed;
            View.TreeExiting -= Exiting;
        }
    }

    private sealed class InputState(Node node)
    {
        private readonly bool _input = node.IsProcessingInput();
        private readonly bool _shortcut = node.IsProcessingShortcutInput();
        private readonly bool _unhandled = node.IsProcessingUnhandledInput();
        private readonly bool _key = node.IsProcessingUnhandledKeyInput();
        private readonly Control.FocusModeEnum? _focus = (node as Control)?.FocusMode;
        private readonly Control.MouseBehaviorRecursiveEnum? _mouse = (node as Control)?.MouseBehaviorRecursive;

        public void Suspend(bool routeInput)
        {
            node.SetProcessInput(routeInput && _input);
            node.SetProcessShortcutInput(false);
            node.SetProcessUnhandledInput(false);
            node.SetProcessUnhandledKeyInput(false);
            if (node is Control control)
            {
                control.FocusMode = Control.FocusModeEnum.None;
                control.MouseBehaviorRecursive = Control.MouseBehaviorRecursiveEnum.Disabled;
            }
        }

        public void Restore()
        {
            node.SetProcessInput(_input);
            node.SetProcessShortcutInput(_shortcut);
            node.SetProcessUnhandledInput(_unhandled);
            node.SetProcessUnhandledKeyInput(_key);
            if (node is Control control)
            {
                control.FocusMode = _focus!.Value;
                control.MouseBehaviorRecursive = _mouse!.Value;
            }
        }
    }

    private Node Root => GetParent();
    private readonly List<Node> _backgrounds = [];
    private readonly Dictionary<Control, Window> _windows = [];
    private readonly Dictionary<Node, InputState> _suspended = [];
    private readonly Dictionary<Node, Action> _pendingReady = [];
    private readonly HashSet<Node> _leaving = [];
    private Window? _top;
    private Control? _backgroundFocus;
    private bool _refreshing, _refreshAgain, _exiting;
    public bool HasModal => _top is not null;

    public static ModalStack Attach(Node root)
    {
        var stack = new ModalStack { Name = nameof(ModalStack) };
        root.AddChild(stack, false, InternalMode.Back);
        return stack;
    }

    public static ModalStack? Find(Node? ancestor)
    {
        for (; ancestor is not null; ancestor = ancestor.GetParent())
            if (ancestor.GetNodeOrNull<ModalStack>(nameof(ModalStack)) is { } stack) return stack;
        return null;
    }

    public void AddBackground(Node root)
    {
        _backgrounds.Add(root);
        WatchReady(root);
    }

    public override void _Ready()
    {
        GetTree().NodeAdded += NodeAdded;
        GetTree().NodeRemoved += NodeRemoved;
        GetViewport().GuiFocusChanged += FocusChanged;
    }

    public void Register(Control view)
    {
        if (_windows.ContainsKey(view)) return;
        var window = new Window(this, view);
        _windows.Add(view, window);
        window.Attach();
        WatchReady(view);
    }

    private void WatchReady(Node node)
    {
        if (_pendingReady.ContainsKey(node)) return;
        void Ready()
        {
            if (_pendingReady.Remove(node, out var pending)) node.Ready -= pending;
            if (node is Control view && _windows.ContainsKey(view)) BringForward(view);
            Refresh();
        }
        if (node.IsNodeReady()) Ready();
        else if (_pendingReady.TryAdd(node, Ready)) node.Ready += Ready;
    }

    private void BringForward(Control view)
    {
        if (!view.IsVisibleInTree()) return;
        for (Node branch = view; branch != Root && branch.GetParent() is { } parent; branch = parent)
            if (branch.GetIndex() != parent.GetChildCount() - 1) parent.MoveChild(branch, -1);
    }

    private void NodeAdded(Node node)
    {
        if (!InScope(node)) return;
        // The ancestor's Ready signal covers its initial subtree. IsNodeReady is
        // already true inside _Ready, so pending signals own the readiness boundary.
        for (Node? parent = node.GetParent(); parent is not null; parent = parent.GetParent())
            if (_pendingReady.ContainsKey(parent)) return;
        WatchReady(node);
    }

    private void NodeRemoved(Node node)
    {
        if (_suspended.Remove(node, out var state) && IsInstanceValid(node)) state.Restore();
        if (_pendingReady.Remove(node, out var ready)) node.Ready -= ready;
        _backgrounds.Remove(node);
        _leaving.Remove(node);
    }

    private bool InScope(Node node) => node == Root || Root.IsAncestorOf(node) ||
        _backgrounds.Any(root => root == node || root.IsAncestorOf(node));

    private void FocusChanged(Control control)
    {
        if (control is not null && _top is { } top && Within(top.View, control)) top.LastFocus = control;
    }

    private static bool Within(Node parent, Node child) => parent == child || parent.IsAncestorOf(child);
    private bool IsReady(Node node) => node.IsNodeReady() && !_pendingReady.ContainsKey(node);

    private void Refresh()
    {
        if (_exiting) return;
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        try
        {
            do { _refreshAgain = false; RefreshWindows(); } while (_refreshAgain);
        }
        finally { _refreshing = false; }
    }

    private void RefreshWindows()
    {
        var visible = new List<Window>();
        void Visit(Node node)
        {
            if (_leaving.Contains(node)) return;
            if (node is Control view && _windows.TryGetValue(view, out var window) && IsReady(view) && view.IsVisibleInTree())
                visible.Add(window);
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(Root);
        for (int i = 0; i < visible.Count; i++)
        {
            // Reserve local depth for artwork and effects. Window order is owned here.
            visible[i].View.ZAsRelative = false;
            visible[i].View.ZIndex = (i + 1) * 16;
        }
        var previous = _top;
        _top = visible.LastOrDefault();
        if (previous != _top && previous is not null && IsInstanceValid(previous.View))
            previous.View.MouseBehaviorRecursive = previous.MouseBehavior;
        if (previous is null && _top is { } entering) _backgroundFocus = entering.ReturnFocus;
        // A covered page may rebuild ordinary controls after its child window.
        // Keep pointer hit testing aligned with the centrally assigned visual order.
        if (_top is { } front && _leaving.Count == 0) BringForward(front.View);
        RefreshInput();
        RestoreFocus(previous);
    }

    private void RefreshInput()
    {
        var blocked = new HashSet<Node>();
        if (_top is { } owner)
        {
            void Isolate(Node node)
            {
                if (_leaving.Contains(node) || node == this) return;
                if (IsReady(node) && !Within(owner.View, node))
                {
                    blocked.Add(node);
                    if (!_suspended.TryGetValue(node, out var state)) _suspended.Add(node, state = new(node));
                    state.Suspend(node is IModalInputRouter && Within(node, owner.View));
                }
                foreach (var child in node.GetChildren(includeInternal: true)) Isolate(child);
            }
            Isolate(Root);
            foreach (var background in _backgrounds) Isolate(background);
        }
        foreach (var node in _suspended.Keys.Where(node => !blocked.Contains(node)).ToArray())
        {
            var state = _suspended[node]; _suspended.Remove(node);
            if (IsInstanceValid(node)) state.Restore();
        }
        // The top window explicitly starts an enabled mouse branch even when its
        // ancestors are covered. This also blocks clicks outside a small dialog.
        if (_top is { } mouseOwner) mouseOwner.View.MouseBehaviorRecursive = Control.MouseBehaviorRecursiveEnum.Enabled;
    }

    private void RestoreFocus(Window? previous)
    {
        if (_top is { } current && GetViewport().GuiGetFocusOwner() is { } focus && CanFocus(focus) && Within(current.View, focus)) return;
        if (_top is { } active)
        {
            var target = CanFocus(active.LastFocus) ? active.LastFocus
                : CanFocus(previous?.ReturnFocus) && Within(active.View, previous!.ReturnFocus!) ? previous.ReturnFocus
                : Focusable(active.View).FirstOrDefault();
            target?.GrabFocus();
        }
        else if (previous is not null)
        {
            var target = CanFocus(previous.ReturnFocus) ? previous.ReturnFocus : _backgroundFocus;
            if (CanFocus(target)) target!.GrabFocus();
            _backgroundFocus = null;
        }
    }

    private bool CanFocus(Control? control) => IsInstanceValid(control) && control!.IsInsideTree() && IsReady(control) &&
        !control.IsQueuedForDeletion() && control.IsVisibleInTree() && control.FocusMode != Control.FocusModeEnum.None &&
        !_leaving.Any(root => Within(root, control)) &&
        (control is not BaseButton button || !button.Disabled);

    private IEnumerable<Control> Focusable(Node root)
    {
        if (root is Control control && CanFocus(control)) yield return control;
        foreach (var child in root.GetChildren())
            foreach (var candidate in Focusable(child)) yield return candidate;
    }

    public override void _ExitTree()
    {
        _exiting = true;
        GetTree().NodeAdded -= NodeAdded;
        GetTree().NodeRemoved -= NodeRemoved;
        GetViewport().GuiFocusChanged -= FocusChanged;
        foreach (var (node, state) in _suspended)
            if (IsInstanceValid(node)) state.Restore();
        _suspended.Clear();
        if (_top is { } top && IsInstanceValid(top.View)) top.View.MouseBehaviorRecursive = top.MouseBehavior;
        foreach (var (node, ready) in _pendingReady)
            if (IsInstanceValid(node)) node.Ready -= ready;
        _pendingReady.Clear();
        foreach (var window in _windows.Values) window.Dispose();
        _windows.Clear();
    }
}
