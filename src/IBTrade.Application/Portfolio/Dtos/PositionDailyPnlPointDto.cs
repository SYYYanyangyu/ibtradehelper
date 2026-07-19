namespace IBTrade.Application.Portfolio.Dtos;

/// <summary>某日、某只标的的盈亏贡献点。</summary>
public record PositionDailyPnlPointDto(
    /// <summary>标的代码</summary>
    string Symbol,
    /// <summary>资产类型（字符串，便于前端直接展示）</summary>
    string AssetType,
    /// <summary>当日市值</summary>
    decimal MarketValue,
    /// <summary>前一日市值</summary>
    decimal PrevMarketValue,
    /// <summary>当日贡献 = 当日市值 − 前一日市值（对持有股即当日盈亏主要来源）</summary>
    decimal DailyContribution,
    /// <summary>当日未实现盈亏</summary>
    decimal UnrealizedPnl,
    /// <summary>贡献占当日总盈亏绝对值的比例（0..1），用于条形宽度</summary>
    decimal Share);

/// <summary>某日的个股盈亏归因结果：含当日总贡献与各标的明细。计算全部在后端完成，前端只渲染。</summary>
public record PositionDailyPnlDto(
    /// <summary>归因日期（UTC 当日）</summary>
    DateTime DateUtc,
    /// <summary>当日总贡献 = 各标的贡献之和（持仓市值当日变动合计）</summary>
    decimal TotalContribution,
    /// <summary>按贡献降序排列的逐标的明细</summary>
    IReadOnlyList<PositionDailyPnlPointDto> Points);
