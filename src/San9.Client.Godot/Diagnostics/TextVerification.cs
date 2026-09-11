using Godot;
using San9.Content.Packages;
using San9.Content.Text;
using San9.Content.Images;

namespace San9.Client.Godot.Diagnostics;

public partial class TextVerification : Control
{
    public ClientContentPackage Content { get; init; } = null!;
    public IReadOnlyDictionary<string, PngImage> Images { get; init; } = null!;
    public string Output { get; init; } = "";

    public override async void _Ready()
    {
        try
        {
            Size = new(1024,768);
            AddChild(new ColorRect { Size = Size, Color = new("182025") });
            Dictionary<string, ImageTexture> textures = [];
            foreach (var pair in Content.Skin.Roles.Where(p => p.Key.StartsWith("text-icon-",StringComparison.Ordinal)))
            {
                var pixels = Images[pair.Value];
                using var image = Image.CreateFromData(pixels.Width,pixels.Height,false,Image.Format.Rgba8,pixels.Pixels);
                textures.Add(pair.Key,ImageTexture.CreateFromImage(image));
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
                "[outline_size=1][outline_color=#98B0F8A6]蓝色描边[/outline_color][/outline_size]，显式换行如下：\n" + MessageMarkup.Escape("参数里的[b]必须按普通文字显示。") + "\n[font_size=24]字号切换[/font_size]结束。",GameTextStyles.CatalogRow);
            var font = GetThemeFont("font");
            IEnumerable<string> Literals(IEnumerable<MessageNode> nodes) => nodes.SelectMany(n => n switch
            {
                MessageLiteral t => new[]{t.BbCode,t.SourceBbCode},
                MessageConditional c => Literals(c.Body), MessageChoice c => c.Choices.SelectMany(Literals), _ => []
            });
            var characters=Content.Text.Entries.Values.SelectMany(t=>new[]{t.Text,t.SourceText})
                .Concat(Content.Text.Messages.Values.SelectMany(m=>Literals(m.Nodes)))
                .SelectMany(s=>s.EnumerateRunes()).Where(r=>!System.Text.Rune.IsControl(r)).Distinct().ToArray();
            var missing=characters.Where(r=>!font.HasChar(r.Value)).Select(r=>new {Code=$"U+{r.Value:X}",Character=r.ToString()}).ToArray();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output,"font-coverage.json"),System.Text.Json.JsonSerializer.Serialize(new {Characters=characters.Length,Missing=missing},PackageFiles.JsonOptions),System.Text.Encoding.UTF8);
            if (missing.Length != 0) throw new InvalidDataException($"Modern font coverage has {missing.Length} missing characters.");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            foreach(var label in labels)
                if(label.GetContentHeight() <= 0 || label.GetParsedText().Contains("icons/",StringComparison.Ordinal)) throw new InvalidDataException("Native image BBCode did not resolve.");
            if(DisplayServer.GetName()!="headless")
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var screenshot=GetViewport().GetTexture().GetImage();
                screenshot.SavePng(Path.Combine(Output,"text-layout.png"));
            }
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
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (DisplayServer.GetName() != "headless")
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var screenshot = GetViewport().GetTexture().GetImage();
                    screenshot.SavePng(Path.Combine(Output, $"styles-{size}-{page.index}.png"));
                }
                RemoveChild(sheet); sheet.Free();
            }
            GD.Print($"TEXT_VERIFICATION_OK characters={characters.Length} missing={missing.Length}");
            GetTree().Quit();
        }
        catch(Exception e) { GD.PushError($"TEXT_VERIFICATION_FAILED {e}"); GetTree().Quit(1); }
    }
}
