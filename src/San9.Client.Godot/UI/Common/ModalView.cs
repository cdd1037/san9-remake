using Godot;

namespace San9.Client.Godot;

/// <summary>A window whose visual order, input and focus belong to its scene's modal stack.</summary>
public abstract partial class ModalView : Control
{
    public override void _EnterTree() => ModalStack.Find(GetParent())?.Register(this);
}

/// <summary>An ancestor that routes raw input to its current child window.</summary>
public interface IModalInputRouter { }
