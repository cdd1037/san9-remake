using System.Text.RegularExpressions;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

/// <summary>Source-specific editorial decisions, before any target script conversion.</summary>
internal static class OriginalTextEditorial
{
    private sealed record Edit(string Expected, string Canonical, bool Whole)
    {
        public Regex? TermPattern { get; } = Whole ? null : SourceTextTerms.Pattern(Expected);
    }
    private static readonly HashSet<int> ReviewedPeople = [];
    private static readonly Dictionary<(string Catalog, int Id), List<Edit>> Rules = BuildRules();

    public static void ValidatePersonReferences(int id, int family, int given, int courtesy)
    {
        if (ReviewedPeople.Contains(id) && (family != id || given != id || courtesy != id))
            throw new InvalidDataException($"Reviewed person {id} has different name resource references.");
    }

    public static MessageDefinition Apply(MessageDefinition source, string catalog, int? messageId)
    {
        if (messageId is not int id || !Rules.TryGetValue((catalog, id), out var edits)) return source;
        foreach (var edit in edits)
        {
            if (edit.Whole)
            {
                string? original = source.Nodes switch
                {
                    [] or [MessageReturn] => "",
                    [MessageLiteral text] => MessageMarkup.Plain(text.BbCode),
                    [MessageLiteral text, MessageReturn] => MessageMarkup.Plain(text.BbCode),
                    _ => null
                };
                if (original != edit.Expected)
                    throw new InvalidDataException($"Editorial source mismatch: {catalog}.{id}.");
                bool hasText = source.Nodes.FirstOrDefault() is MessageLiteral || edit.Canonical.Length > 0;
                MessageNode[] textNodes = hasText ? [new MessageLiteral(MessageMarkup.Escape(edit.Canonical))] : [];
                source = new(source.Nodes.LastOrDefault() is MessageReturn ? [.. textNodes, new MessageReturn()] : textNodes);
            }
            else
            {
                int count = 0;
                source = MessageTextRuns.Map(source, text => SourceTextTerms.Replace(text, edit.TermPattern!, edit.Canonical, ref count));
                if (count != 1) throw new InvalidDataException($"Expected one editorial term '{edit.Expected}' in {catalog}.{id}, found {count}.");
            }
        }
        return source;
    }

    private static Dictionary<(string, int), List<Edit>> BuildRules()
    {
        Dictionary<(string, int), List<Edit>> rules = [];
        void Add(string catalog, int id, string expected, string canonical, bool whole = true)
        {
            if (!rules.TryGetValue((catalog, id), out var edits)) rules.Add((catalog, id), edits = []);
            edits.Add(new(expected, canonical, whole));
        }
        void Person(int id, string originalSurname, string originalGiven, string originalCourtesy,
            string? surname = null, string? given = null, string? courtesy = null)
        {
            ReviewedPeople.Add(id);
            foreach (var (catalog, start) in new[] { ("messages", 1678), ("base-messages", 1610) })
            {
                Add(catalog, start + 4 * id, originalSurname, surname ?? originalSurname);
                Add(catalog, start + 4 * id + 1, originalGiven, given ?? originalGiven);
                Add(catalog, start + 4 * id + 2, originalCourtesy, courtesy ?? originalCourtesy);
            }
        }
        Person(33, "王", "戎", "濬沖");
        Person(35, "王", "濬", "士治");
        Person(47, "王", "淩", "彥雲");
        Person(54, "華", "覈", "永先");
        Person(67, "樂", "綝", "");
        Person(89, "何", "曾", "潁考", courtesy: "穎考");
        Person(120, "麴", "義", "");
        Person(131, "橋", "蕤", "");
        Person(133, "橋", "瑁", "元偉");
        Person(153, "吳", "懿", "子遠");
        Person(162, "高", "昇", "");
        Person(226, "脩", "允", "");
        Person(231, "脩", "則", "");
        Person(239, "朱", "儁", "公偉");
        Person(247, "荀", "顗", "景倩");
        Person(266, "鍾", "繇", "元常");
        Person(282, "徐", "盛", "文嚮");
        Person(292, "秦", "宓", "子勑");
        Person(348, "孫", "乾", "公祐");
        Person(360, "孫", "綝", "子通");
        Person(368, "孫", "𩅦", "");
        Person(395, "張", "郃", "儁艾", courtesy: "儁乂");
        Person(400, "張", "緝", "敬仲");
        Person(416, "張", "飛", "翼德", courtesy: "益德");
        Person(477, "滕", "脩", "");
        Person(478, "陶", "濬", "");
        Person(491, "寗", "隨", "");
        Person(510, "潘", "濬", "承明");
        Person(516, "糜", "竺", "子仲", surname: "麋");
        Person(518, "糜", "芳", "子方", surname: "麋");
        Person(522, "馮", "紞", "少胄");
        Person(524, "傅士", "仁", "君義", surname: "士");
        Person(526, "傅", "巽", "公梯", courtesy: "公悌");
        Person(527, "傅", "彤", "", given: "肜");
        Person(536, "忙", "牙長", "");
        Person(537, "逢", "紀", "元圖");
        Person(553, "毛", "炅", "");
        Person(602, "劉", "璝", "");
        Person(612, "劉", "丞", "", given: "承");
        Person(615, "劉", "璿", "文衡");
        Person(645, "泠", "苞", "", surname: "冷");
        Person(679, "華", "陀", "", given: "佗");
        Person(686, "大", "喬", "", surname: "", given: "大喬");
        Person(687, "小", "喬", "", surname: "", given: "小喬");
        Person(689, "馬", "雲騄", "");
        foreach (var (id, original, canonical) in new[]
        {
            (5207, "建議校尉", "建義校尉"), (1464, "孫濱兵法", "孫臏兵法"),
            (1520, "博奕論", "博弈論"), (1564, "龍方壼", "龍方壺"),
            (1566, "金象嵌壼", "金象嵌壺"), (214, "壼關", "壺關"),
            (434, "末蘆", "末盧"), (436, "烏鼠山", "鳥鼠山")
        }) Add("messages", id, original, canonical);
        Add("messages", 7677,
            "戰國時代，齊國兵法家\n孫濱所著的兵法書。孫\n濱亦作「孫臏」，據說\n孫濱是孫武的子孫。",
            "戰國時代，齊國兵法家\n孫臏所著的兵法書。孫\n臏亦作「孫濱」，據說\n孫臏是孫武的子孫。");
        Add("messages", 7728, "嵌上金象的青銅壺，並\n繪上代表吉祥的鳥類。",
            "採用金鑲嵌工藝的青銅壺，並\n繪上代表吉祥的鳥類。");
        foreach (int id in new[] { 7836, 7837 }) Add("messages", id, "庶殿門", "諸殿門", false);
        Add("messages", 7705, "博奕", "博弈", false);
        Add("messages", 7684, "作法自弊", "作法自斃", false);
        foreach (int id in new[] { 7122, 7123 }) Add("messages", id, "驃駻", "剽悍", false);
        foreach (int id in new[] { 7032, 7033 }) Add("base-messages", id, "驃駻", "剽悍", false);
        Add("messages", 9603, "張翼德", "張益德", false);
        // Explicit biography references found during the name audit; not a global name replacement.
        foreach (var (id, original, canonical) in new[]
        {
            (1, "翼德", "益德"), (159, "糜竺", "麋竺"), (163, "糜竺", "麋竺"),
            (163, "傅士仁", "士仁"), (178, "傅士仁", "士仁"), (430, "傅彤", "傅肜")
        }) Add("biographies", id, original, canonical, false);
        return rules;
    }
}
