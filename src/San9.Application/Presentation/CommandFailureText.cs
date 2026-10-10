using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class CommandFailureText
{
    public static string Describe(CommandRejection reason) => reason switch
    {
        CommandRejection.None => "",
        CommandRejection.NotStable or CommandRejection.WrongSequence => "局势已变化，请重新选择命令。",
        CommandRejection.NotActiveController => "该军团已委任，无法直接下令。请先恢复直接控制。",
        CommandRejection.PersonUnavailable or CommandRejection.UnknownPerson => "执行武将不在本设施或已经行动。",
        CommandRejection.InsufficientFunds => "资金或兵粮不足。",
        CommandRejection.TreasureUnavailable or CommandRejection.UnknownTreasure => "宝物已不在可用持有人手中。",
        CommandRejection.NoDestination => "目标没有有效的到达地点。",
        CommandRejection.InvalidCorps => "请选择有效军团、辖区与方针；新设或变更必须保留君主所在地及直属都市，并有可任太守的武将。",
        CommandRejection.ResourceOverflow => "接收军团的钱粮超过容量。",
        CommandRejection.SettlementConfused => "来源设施正在混乱。",
        CommandRejection.DuplicateCommandId => "这项命令已经提交。",
        CommandRejection.DecisionPending => "请先完成当前谈判。",
        _ => "请选择符合计略范围、外交关系与当前状态的目标。"
    };
}
