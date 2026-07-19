using IBTrade.Domain.Common;

namespace IBTrade.Domain.Entities;

/// <summary>
/// 数据通道最近一次成功状态（Flex 摄入 / Gateway 同步），供看板展示「数据从哪来、多久前更新」。
/// </summary>
public class DataFeedStatus : Entity
{
    /// <summary>通道：Flex / Gateway</summary>
    public required string Feed { get; set; }

    /// <summary>Flex 时为 Live/Paper；Gateway 通常为空（连的是当前登录会话）。</summary>
    public string? Environment { get; set; }

    public DateTime LastSuccessUtc { get; set; }

    public string? AccountId { get; set; }

    public int? ConnectedPort { get; set; }

    /// <summary>简要说明，如持仓数、合并模式等。</summary>
    public string? Message { get; set; }
}
