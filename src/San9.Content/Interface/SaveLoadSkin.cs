namespace San9.Content.Interface;

public static class SaveLoadSkin
{
    public static IEnumerable<string> RequiredRoles => new[] { "background", "footer", "pages-background",
        "frame-top-left", "frame-top", "frame-top-right", "frame-note-top-right", "frame-left", "frame-right",
        "frame-bottom-left", "frame-bottom", "frame-bottom-right", "title-left", "title-middle", "title-right",
        "historical", "fictional", "difficulty" }.Select(p => "save-load-" + p)
        .Concat(new[] { "save-row", "load-row", "page", "yes", "no", "close", "help" }
            .SelectMany(p => Enumerable.Range(0, 4).Select(f => $"save-load-{p}" + (f == 0 ? "" : $"-{f}"))));
}
