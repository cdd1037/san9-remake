namespace San9.Importer.San9PkTc101.Text;

public static class NameLocalizer
{
    public static string ToSimplified(string source)
    {
        const string traditional = "帶樂漁陽縣間橋頓鉅馬門壺關長東萊臨廣陰潁譙內農晉陳倉隴張烏尋陸漢麥華羅興軍劍閣壽綿蘭溫臺蒼蘆無薊鄴黨許廬吳會寧雲蠻韓劉備孫堅袁紹術謙瓚騰黃祖嚴顏趙劉呂鳳龔鮑闕闞賈鄧鍾韋";
        const string simplified =  "带乐渔阳县间桥顿巨马门壶关长东莱临广阴颍谯内农晋陈仓陇张乌寻陆汉麦华罗兴军剑阁寿绵兰温台苍芦无蓟邺党许庐吴会宁云蛮韩刘备孙坚袁绍术谦瓒腾黄祖严颜赵刘吕凤龚鲍阙阚贾邓钟韦";
        return new string(source.Select(c => traditional.IndexOf(c) is int i && i >= 0 ? simplified[i] : c).ToArray()).Replace('楊', '杨').Replace('魯', '鲁');
    }
}
