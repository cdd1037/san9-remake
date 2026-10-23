using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Interface;

public static class StartMenuPresentationCompiler
{
    public static StartMenuPresentation Compile(TextureLibrary library, TextCatalog text)
    {
        string Asset(string archive, int image) => library.Archives.Single(a => a.Id == archive)
            .Variants.Single(v => v.ImageIndex == image).Image.Id;
        StartMenuSprite Sprite(int image, int x, int y, int columns = 1) => new(Asset("g_start", image), x, y, columns);
        string Hint(int id)
        {
            var nodes = text.Messages[$"messages.{id}"].Nodes;
            if (nodes.Any(n => n is not MessageLiteral)) throw new InvalidDataException("Opening menu hint must be static text.");
            return MessageMarkup.Plain(string.Concat(nodes.Cast<MessageLiteral>().Select(n => n.BbCode)));
        }
        // RE-0200: CStartupView composition and CStartupMainDlg control/illustration bindings.
        int[][] glints = [
            [864,372,-1,-1,712,300,-1,-1,608,368,604,200,-1,-1,524,308,724,132,720,312],
            [668,304,-1,-1,740,280,-1,-1,808,232,608,184,-1,-1,732,268,548,296,-1,-1],
            [652,280,-1,-1,584,228,-1,-1,564,340,712,272,-1,-1,672,108,536,104,-1,-1],
            [756,296,-1,-1,696,440,-1,-1,696,344,-1,-1,-1,-1,648,99,608,224,-1,-1],
            [668,374,-1,-1,515,518,-1,-1,507,170,595,52,820,291,565,354,635,189,458,252],
            [807,294,-1,-1,-1,-1,-1,-1,569,430,461,207,-1,-1,775,380,576,327,843,369],
            [836,308,756,484,620,232,824,212,732,184,-1,-1,-1,-1,628,348,668,96,-1,-1],
            [756,228,-1,-1,692,152,-1,-1,596,208,752,264,-1,-1,552,140,632,320,-1,-1],
            [896,448,828,496,740,460,-1,-1,656,256,616,396,-1,-1,552,308,696,100,-1,-1]
        ];
        int[] glintImages = [41,41,40,40,42,42,42,43,44,44];
        int[] glintColumns = [6,6,6,6,4,4,4,8,8,8];
        (string Id, string Label, int Hint, string Archive, int Image)[] rows = [
            ("new-game", "重新开始游戏", 8058, "g_start", 45),
            ("load-game", "载入游戏资料", 8060, "g_start", 46),
            ("tutorial", "进入游戏教学", 8061, "g_start", 47),
            ("chronicles", "进入磨练史话", 8556, "g_strtpk", 1),
            ("create-officer", "登录新武将", 8062, "g_start", 48),
            ("edit-officers", "编辑武将档", 8557, "g_strtpk", 0),
            ("gallery", "影像陈列廊", 8064, "g_start", 49),
            ("music", "背景音乐厅", 8065, "g_start", 50),
            ("exit", "结束本游戏", 8066, "g_start", 51)
        ];
        var entries = rows.Select((row, index) => new StartMenuEntry(row.Id, row.Label, Hint(row.Hint),
            Sprite(27 + index, 0, 180 + 42 * index, 4),
            new([Asset(row.Archive, row.Image)], 8, 452, 96, .25),
            Enumerable.Range(0, 10).Where(i => glints[index][2 * i] >= 0)
                .Select(i => new StartMenuAnimation([Asset("g_start", glintImages[i])], glintColumns[i], glints[index][2*i], glints[index][2*i+1], .2, .5f)).ToArray())).ToArray();
        return new([Sprite(18, 504, 472), Sprite(19, 0, 376)],
            [new(Enumerable.Range(0, 10).Select(i => Asset("g_start", i)).ToArray(), 1, 0, 56, .2),
             new(Enumerable.Range(10, 8).Select(i => Asset("g_start", i)).ToArray(), 1, 352, 556, .2)],
            [Sprite(38, 448, 48), new(Asset("g_locspk", 0), 0, 0), Sprite(20, 0, 640)],
            Sprite(23, 920, 8, 4), Sprite(24, 968, 8, 4), Hint(8057), entries);
    }
}
