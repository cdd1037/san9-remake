namespace San9.Content.Interface;

public static class DeploymentSkin
{
    public static IReadOnlyList<string> RequiredRoles { get; } = new[] {
        "cutout-top-left", "cutout-top-right", "cutout-bottom-left", "top-edge", "left-edge", "right-edge",
        "choice-normal-left", "choice-normal-middle", "choice-normal-right",
        "choice-hover-left", "choice-hover-middle", "choice-hover-right",
        "choice-selected-left", "choice-selected-middle", "choice-selected-right",
        "background", "top-left", "top-right", "bottom-left", "bottom-right", "edge",
        "title-left", "title-middle", "title-right", "footer-left", "footer-middle", "footer-right",
        "label-left", "label-middle", "label-right", "field", "field-rim",
        "roster-header-left", "roster-header-middle", "roster-header-right", "column-top", "column-middle", "column-bottom", "divider",
        "facility-left", "facility-middle", "facility-right", "mode-left", "mode-middle", "mode-right",
        "number-background", "number-field-left", "number-field-middle", "number-field-right",
        "number-title-left", "number-title-middle", "number-title-right", "number-slider-fill", "number-slider-thumb"
    }.Select(s => "deployment-" + s).Concat(
        new[] { "edit-left", "edit-middle", "edit-right", "accept-left", "accept-middle", "accept-right",
            "cancel-left", "cancel-middle", "cancel-right", "close", "help", "number-accept", "number-cancel",
            "number-back", "number-clear", "number-min", "number-max", "number-minus", "number-plus", "number-close", "number-help", "number-00", "number-0",
            "number-1", "number-2", "number-3", "number-4", "number-5", "number-6", "number-7", "number-8", "number-9" }
        .SelectMany(s => Enumerable.Range(0, 4).Select(f => "deployment-" + s + (f == 0 ? "" : $"-{f}")))).ToArray();
}
