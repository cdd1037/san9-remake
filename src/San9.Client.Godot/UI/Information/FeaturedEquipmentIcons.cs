using Godot;
namespace San9.Client.Godot;

/// <summary>Project-owned atlas; legacy or mod items without a matching cell have no icon.</summary>
internal static class FeaturedEquipmentIcons
{
    private const string Path = "res://assets/equipment/featured-atlas.png";
    private static Dictionary<string, Texture2D>? _icons;
    // Atlas coordinates belong to the artwork, not to the order of content definitions.
    internal static readonly string[] CellIds =
    [
        "featured-xuanwen-ji", "featured-qinggang-jian", "featured-yanling-dao", "featured-cangyun-qiang",
        "featured-chifeng-mao", "featured-huyue-ji", "featured-ningyuan-jian", "featured-qingyu-dao",
        "featured-yueshan-yue", "featured-jingtao-qiang", "featured-shuofeng-ji", "featured-yunwen-jian",
        "featured-huanshou-kui", "featured-yunwen-kui", "featured-xuantie-jia", "featured-qinglin-jia",
        "featured-chijin-jia", "featured-wuzhui", "featured-huacong", "featured-zaoliu",
        "featured-qingzong", "featured-xuecong", "featured-heiliu", "featured-huwen-fu",
        "featured-yunlei-fu", "featured-shanwen-fu", "featured-shuanghu-fu", "featured-yanwei-biao",
        "featured-liuxing-biao", "featured-heiling-zhen", "featured-feishi-suo", "featured-xingjun-ji",
        "featured-shoubei-lue", "featured-junxu-ce", "featured-dili-yao", "featured-jianglue-pian",
        "featured-wenyu-pei", "featured-qingyu-yin", "featured-tongque-zun", "featured-guwen-jing"
    ];

    internal static Texture2D? Find(string definitionId, string name)
    {
        if (definitionId == "featured-liuxing-biao")
        {
            const string dart = "res://assets/equipment/liuxing-biao.png";
            return ResourceLoader.Exists(dart) ? GD.Load<Texture2D>(dart) : null;
        }
        if (_icons is null)
        {
            _icons = new(StringComparer.Ordinal);
            if (ResourceLoader.Exists(Path) && GD.Load<Texture2D>(Path) is { } atlas)
            for (int i = 0; i < CellIds.Length; i++)
            {
                int column = i % 8, row = i / 8;
                int x0 = atlas.GetWidth() * column / 8, x1 = atlas.GetWidth() * (column + 1) / 8;
                int y0 = atlas.GetHeight() * row / 5, y1 = atlas.GetHeight() * (row + 1) / 5;
                _icons.Add(CellIds[i], new AtlasTexture { Atlas = atlas, Region = new(x0 + 1, y0 + 1, x1 - x0 - 2, y1 - y0 - 2) });
            }
        }
        if (_icons.TryGetValue(definitionId, out var icon)) return icon;
        // The name guard prevents a mod that reuses a legacy ID for another object
        // from silently displaying a misleading image.
        string? path = (definitionId, name) switch
        {
            ("legacy-treasure:0", "赤兔马" or "赤兔馬") => "res://assets/equipment/red-hare.png",
            ("legacy-treasure:1", "的卢" or "的盧") => "res://assets/equipment/dilu.png",
            ("legacy-treasure:2", "爪黄飞电" or "爪黃飛電") => "res://assets/equipment/zhaohuang-feidian.png",
            ("legacy-treasure:3", "绝影" or "絕影") => "res://assets/equipment/jueying.png",
            ("legacy-treasure:4", "大宛马" or "大宛馬") => "res://assets/equipment/dayuan-ma.png",
            ("legacy-treasure:5", "四轮车" or "四輪車") => "res://assets/equipment/silun-che.png",
            ("legacy-treasure:6", "七星宝刀" or "七星寶刀") => "res://assets/equipment/qixing-dao.png",
            ("legacy-treasure:7", "倚天剑" or "倚天劍") => "res://assets/equipment/yitian-jian.png",
            ("legacy-treasure:8", "青釭剑" or "青釭劍") => "res://assets/equipment/qinggang-jian.png",
            ("legacy-treasure:9", "方天画戟" or "方天畫戟") => "res://assets/equipment/fangtian-ji.png",
            ("legacy-treasure:10", "青龙偃月刀" or "青龍偃月刀") => "res://assets/equipment/green-dragon-blade.png",
            ("legacy-treasure:11", "蛇矛") => "res://assets/equipment/serpent-spear.png",
            ("legacy-treasure:12", "铁脊蛇矛" or "鐵脊蛇矛") => "res://assets/equipment/tieji-shemao.png",
            ("legacy-treasure:13", "雌雄一对剑" or "雌雄一對劍") => "res://assets/equipment/cixiong-jian.png",
            ("legacy-treasure:14", "古锭刀" or "古錠刀") => "res://assets/equipment/guding-dao.png",
            ("legacy-treasure:15", "三尖刀") => "res://assets/equipment/sanjian-dao.png",
            ("legacy-treasure:16", "双铁戟" or "雙鐵戟") => "res://assets/equipment/shuangtie-ji.png",
            ("legacy-treasure:17", "大斧") => "res://assets/equipment/da-fu.png",
            ("legacy-treasure:18", "铁鞭" or "鐵鞭") => "res://assets/equipment/tie-bian.png",
            ("legacy-treasure:19", "铁蒺藜骨朵" or "鐵蒺藜骨朵") => "res://assets/equipment/jili-guduo.png",
            ("legacy-treasure:20", "流星锤" or "流星錘") => "res://assets/equipment/liuxing-chui.png",
            ("legacy-treasure:21", "眉尖刀") => "res://assets/equipment/meijian-dao.png",
            ("legacy-treasure:22", "铜锤" or "銅錘") => "res://assets/equipment/tong-chui.png",
            ("legacy-treasure:23", "铁锁" or "鐵鎖") => "res://assets/equipment/tie-suo.png",
            ("legacy-treasure:24", "飞刀" or "飛刀") => "res://assets/equipment/fei-dao.png",
            ("legacy-treasure:25", "短戟") => "res://assets/equipment/duan-ji.png",
            ("legacy-treasure:26", "手戟") => "res://assets/equipment/shou-ji.png",
            ("legacy-treasure:27", "袖箭") => "res://assets/equipment/xiu-jian.png",
            ("legacy-treasure:28", "养由基弓" or "養由基弓") => "res://assets/equipment/yangyouji-gong.png",
            ("legacy-treasure:29", "李广弓" or "李廣弓") => "res://assets/equipment/liguang-gong.png",
            ("legacy-treasure:30", "孙子兵法" or "孫子兵法") => "res://assets/equipment/sunzi-bingfa.png",
            ("legacy-treasure:31", "兵法二十四篇") => "res://assets/equipment/bingfa-ershisi.png",
            ("legacy-treasure:32", "六韬" or "六韜") => "res://assets/equipment/liu-tao.png",
            ("legacy-treasure:33", "三略") => "res://assets/equipment/san-lue.png",
            ("legacy-treasure:34", "司马法" or "司馬法") => "res://assets/equipment/sima-fa.png",
            ("legacy-treasure:35", "吴子" or "吳子") => "res://assets/equipment/wu-zi.png",
            ("legacy-treasure:36", "孙膑兵法" or "孫臏兵法") => "res://assets/equipment/sunbin-bingfa.png",
            ("legacy-treasure:37", "魏公子兵法") => "res://assets/equipment/weigongzi-bingfa.png",
            _ => null
        };
        return path is not null && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }
}
