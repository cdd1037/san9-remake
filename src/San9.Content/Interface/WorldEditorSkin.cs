namespace San9.Content.Interface;

public static class WorldEditorSkin
{
    public static readonly string[] ButtonStyles = ["field", "choice", "infantry", "cavalry", "mounted-bow", "crossbow", "naval", "siege", "knowledge", "plot", "strategy", "close", "cancel"];
    public static readonly string[] FrameParts = ["title-end", "title-middle", "bottom-start", "bottom-middle", "left", "right", "top-middle", "bottom-left", "top-left", "bottom-right", "top-right", "bottom-line"];
    public static readonly string[] RequiredRoles = new[] { "editor-background", "editor-panel", "editor-map-frame", "editor-portrait", "editor-radar", "editor-names", "editor-field-value-left", "editor-field-value-middle", "editor-field-value-right", "editor-label-left", "editor-label-middle", "editor-label-right" }
        .Concat(new[] { "left", "middle", "right" }.Select(p => "editor-caption-" + p))
        .Concat(FrameParts.Select(p => "editor-frame-" + p))
        .Concat(new[] { "top-left", "top", "top-right", "left", "right", "bottom-left", "bottom", "bottom-right" }.Select(p => "editor-panel-" + p))
        .Concat(ButtonStyles.SelectMany(s => new[] { "left", "middle", "right" }.SelectMany(p => Enumerable.Range(0, 4).Select(f => $"editor-{s}-{p}" + (f == 0 ? "" : $"-{f}")))))
        .Concat(new[] { "close-icon", "help", "scroll-up", "scroll-down" }.SelectMany(p => Enumerable.Range(0, 4).Select(f => "editor-" + p + (f == 0 ? "" : $"-{f}"))))
        .Concat(new[] { "track-top", "track-middle", "track-bottom", "thumb-top", "thumb-middle", "thumb-bottom" }.Select(p => "editor-scroll-" + p)).ToArray();
}
