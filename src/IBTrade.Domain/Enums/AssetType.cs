namespace IBTrade.Domain.Enums;

/// <summary>资产类型</summary>
public enum AssetType
{
    Stock = 0,   // 股票
    Etf = 1,     // ETF
    Option = 2,  // 期权
    Future = 3,  // 期货
    Forex = 4,   // 外汇
    Bond = 5,    // 债券
    Cash = 6     // 现金
}
