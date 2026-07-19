namespace IBTrade.Domain.Enums;

/// <summary>
/// 数据来源常量（入库与展示统一用这些字面量，避免 Flex / Gateway 混写后无法溯源）。
/// </summary>
public static class DataSources
{
    /// <summary>IBKR Flex Query（Token + QueryId，HTTPS 报表）——历史成交 / 官方 NAV / 持仓结构权威。</summary>
    public const string Flex = "Flex";

    /// <summary>本机 IB Gateway socket API——实时市价、下单、K 线。</summary>
    public const string Gateway = "Gateway";

    /// <summary>由当前持仓推算的盘中估算净值（非 IB 官方报表）。</summary>
    public const string Estimate = "Estimate";
}
