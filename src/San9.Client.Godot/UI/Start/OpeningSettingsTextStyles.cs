using Godot;

namespace San9.Client.Godot;

/// <summary>Opening settings text roles and interaction colors verified in RE-SAN9PK-0071.</summary>
internal static class OpeningSettingsTextStyles
{
    private static GameTextStyle Outlined(int size, string foreground, string outline) =>
        new(size, new(foreground), GameTextEffect.Outline, new(outline));

    internal static GameTextStyle Heading => Outlined(24, "f0f0f0", "404040");
    internal static GameTextStyle Field => Outlined(24, "40f8b0", "002878");
    internal static GameTextStyle Value => Outlined(24, "f0f0f0", "002878");
    internal static GameTextStyle DialogTitle => Outlined(24, "f8f8b0", "b82000");
    internal static GameTextStyle DialogValue => Outlined(16, "f0f0f0", "b82000");

    internal static GameTextStyle Choice(bool highlighted) => highlighted
        ? Outlined(24, "f8f850", "b82000") : Outlined(24, "e0c850", "983810");

    internal static GameTextStyle Combat(bool hovered, bool pressed, bool disabled) => disabled
        ? Outlined(24, hovered ? "000000" : "a4a4a4", hovered ? "f0f0f0" : "404040")
        : pressed ? Outlined(24, "f8f850", "5828c8") : Outlined(24, "e0c850", "983810");

    internal static GameTextStyle Confirm(bool hovered, bool pressed, bool disabled) =>
        Outlined(24, disabled ? hovered ? "000000" : "a4a4a4" : hovered || pressed ? "f0f0f0" : "d8d8d8",
            disabled ? hovered ? "f0f0f0" : "000000" : hovered || pressed ? "002878" : "000000");

    internal static GameTextStyle EditButton(bool hovered, bool pressed, bool disabled) =>
        Outlined(16, disabled ? hovered ? "000000" : "a4a4a4" : hovered ? "f0f0f0" : "d8d8d8",
            disabled ? hovered ? "f06850" : "a4a4a4" : hovered ? "603000" : "000000");
}
