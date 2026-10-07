using San9.Application.Persistence;

namespace San9.Client.Godot.Persistence;

public static class SaveFailureMessages
{
    public static string Describe(SaveException error) => error.Failure switch
    {
        SaveFailure.NotStable => "当前状态尚未稳定，请结束交互后再试。",
        SaveFailure.ContentMismatch => "存档与当前剧本内容不匹配。",
        SaveFailure.UnsupportedVersion => "存档版本不受支持。",
        SaveFailure.InvalidFormat or SaveFailure.InvalidState => "存档损坏或状态无效。",
        SaveFailure.IoFailure when error.InnerException is FileNotFoundException or DirectoryNotFoundException => "存档文件已不存在，请重新选择。",
        _ => "无法读写存档文件，请检查磁盘和访问权限。",
    };
}
