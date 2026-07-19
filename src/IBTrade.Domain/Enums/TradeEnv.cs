namespace IBTrade.Domain.Enums;

/// <summary>交易数据来源环境：真实账户(Live) 或 模拟账户(Paper)。</summary>
public enum TradeEnv
{
    /// <summary>真实账户（实盘）</summary>
    Live,

    /// <summary>模拟账户（Paper Trading）</summary>
    Paper
}
