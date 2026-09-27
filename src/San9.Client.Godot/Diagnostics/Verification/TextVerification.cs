using Godot;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Content.Images;

namespace San9.Client.Godot.Diagnostics;

public partial class TextVerification : Control
{
    public ClientContentPackage Content { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    private TextureScope? _scope;
    public override void _ExitTree() => _scope?.Dispose();
    public string Output { get; init; } = "";

    public override async void _Ready()
    {
        try
        {
            Size = new(1024,768);
            AddChild(new ColorRect { Size = Size, Color = new("182025") });
            var textures = _scope = Service.CreateScope();
            textures.Request(Service.Roles.Keys.Where(k => k.StartsWith("text-icon-", StringComparison.Ordinal)));
            while (!textures.IsReady)
            {
                if (textures.Error is { } error) throw error;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var labels = new List<GameRichTextLabel>();
            int y = 28;
            foreach (int size in new[] {12,16,24})
            {
                var label = new GameRichTextLabel { Position = new(28,y), Size = new(960,100), Textures = textures };
                AddChild(label); labels.Add(label);
                label.Present($"{size}px　[img]icons/city[/img]洛阳现有[color=#F8F8B0]32000[/color]名士兵。伷 璝 寗 勑 𩅦 儁\n" +
                    string.Join("　",new[]{"arrow-up-right","arrow-down-right","crossed-arrows","empty-square","question","turn-arrow","book","waves","circle"}.Select(id=>$"[img]icons/{id}[/img]")),
                    GameTextStyles.For(GameTextRole.Resource) with { FontSize=size });
                y+=112;
            }
            var wrap = new GameRichTextLabel { Position=new(28,380),Size=new(400,180),Textures=textures,AutowrapMode=TextServer.AutowrapMode.WordSmart };
            AddChild(wrap);
            wrap.Present("[img]icons/city[/img]洛阳：[color=#88F880]这是一段用于检查自动换行与行内图标的说明文字。[/color]" +
                "[preset=blue-outline]蓝色描边[/preset]，显式换行如下：\n" + MessageMarkup.Escape("参数里的[b]必须按普通文字显示。") + "\n[font_size=24]字号切换[/font_size]结束。",GameTextStyles.CatalogRow);
            var font = GetThemeFont("font");
            var characters=Content.Text.Entries.Values
                .Concat(MessageGraph.Walk(Content.Text.Messages.Values.SelectMany(m => m.Nodes))
                    .OfType<MessageLiteral>().Select(literal => literal.BbCode))
                .SelectMany(s=>s.EnumerateRunes()).Where(r=>!System.Text.Rune.IsControl(r)).Distinct().ToArray();
            int[] acceptedMissing = [0x20860, 0x29166, 0x2AEFA, 0x2B623];
            var missing=characters.Where(r=>!font.HasChar(r.Value)).Select(r=>new {
                Code=$"U+{r.Value:X}",Character=r.ToString(),Accepted=acceptedMissing.Contains(r.Value) }).ToArray();
            var unexpected = missing.Where(glyph => !glyph.Accepted).ToArray();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output,"font-coverage.json"),System.Text.Json.JsonSerializer.Serialize(new {Characters=characters.Length,Missing=missing},PackageFiles.JsonOptions),System.Text.Encoding.UTF8);
            if (unexpected.Length != 0) throw new InvalidDataException($"Modern font coverage has {unexpected.Length} unaccepted missing characters.");
            await VerificationSupport.Frames(this, 2);
            foreach(var label in labels)
                if(label.GetContentHeight() <= 0 || label.GetParsedText().Contains("icons/",StringComparison.Ordinal)) throw new InvalidDataException("Native image BBCode did not resolve.");
            if (DisplayServer.GetName() != "headless")
                await VerificationSupport.Capture(this, Path.Combine(Output, "text-layout.png"), Service);
            foreach (var child in GetChildren().OfType<CanvasItem>()) child.Hide();
            var samples = Enum.GetValues<GameTextRole>().Select(role => (Name: role.ToString(), Style: GameTextStyles.For(role)))
                .Concat(Enum.GetValues<GameButtonTextRole>().SelectMany(role => Enumerable.Range(0, 8).Select(state =>
                    (Name: $"{role}/{state}", Style: GameTextStyles.Button(role, (state & 1) != 0, (state & 2) != 0, (state & 4) != 0)))))
                .Concat(Enumerable.Range(0, 16).Select(state => (Name: $"HelpTopic/{state}", Style: GameTextStyles.HelpTopic(
                    (state & 1) != 0, (state & 2) != 0, (state & 4) != 0, (state & 8) != 0)))).ToArray();
            foreach (int size in new[] {12, 16, 24})
            foreach (var page in samples.Chunk(18).Select((rows, index) => (rows, index)))
            {
                var sheet = new TextStyleSheet { Size = Size, Samples = page.rows, FontSize = size, Textures = textures };
                AddChild(sheet);
                await VerificationSupport.Frames(this, 2);
                if (DisplayServer.GetName() != "headless")
                    await VerificationSupport.Capture(this, Path.Combine(Output, $"styles-{size}-{page.index}.png"), Service);
                RemoveChild(sheet); sheet.Free();
            }
            GD.Print($"TEXT_VERIFICATION_OK characters={characters.Length} missing={missing.Length} accepted={missing.Length - unexpected.Length} unexpected={unexpected.Length}");
            GetTree().Quit();
        }
        catch(Exception e) { GD.PushError($"TEXT_VERIFICATION_FAILED {e}"); GetTree().Quit(1); }
    }
}
