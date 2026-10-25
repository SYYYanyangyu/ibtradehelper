using IBTrade.Domain.Enums;

namespace IBTrade.Application.IBKR;

/// <summary>从 TWS / IB Gateway 拉取的实时持仓（领域无关）。</summary>
public class TwsPositionDto
{
    public string Symbol { get; set; } = "";
    public AssetType AssetType { get; set; } = AssetType.Stock;
    public string Currency { get; set; } = "USD";
    public decimal Quantity { get; set; }
    public decimal AvgCost { get; set; }
    public decimal MarketPrice { get; set; }
    public decimal Multiplier { get; set; } = 1m;
    public decimal MarketValue { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
}

/// <summary>一次实时拉取的账户快照（领域无关）。</summary>
public class TwsSnapshot
{
    public string AccountId { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public decimal NetLiquidation { get; set; }
    public decimal CashBalance { get; set; }
    public List<TwsPositionDto> Positions { get; set; } = new();

    /// <summary>实际连上的 API 端口（7497/7496=TWS，4002/4001=Gateway）。</summary>
    public int ConnectedPort { get; set; }

    /// <summary>连接来源标识：TWS 或 Gateway。</summary>
    public string Source { get; set; } = "TWS";
}

/// <summary>单个标的的实时行情（按最新价重算）。</summary>
public class TwsLivePosition
{
    public string Symbol { get; set; } = "";
    public AssetType AssetType { get; set; } = AssetType.Stock;
    public string Currency { get; set; } = "USD";
    public decimal Quantity { get; set; }
    public decimal AvgCost { get; set; }
    public decimal LastPrice { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal MarketValue { get; set; }
    public decimal UnrealizedPnl { get; set; }
    /// <summary>相对同步时快照价的变动（最新价 - 快照价）。</summary>
    public decimal Change { get; set; }
}

/// <summary>实时推送快照：连接不中断，价格变动即刷新。</summary>
public class TwsLiveSnapshot
{
    public string AccountId { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public decimal NetLiquidation { get; set; }
    public decimal CashBalance { get; set; }
    public int ConnectedPort { get; set; }
    public string Source { get; set; } = "TWS";
    /// <summary>最近一次行情 tick 的 UTC 时间，用于前端判断是否“仍在跳动”。</summary>
    public DateTime LastTickUtc { get; set; }
    public List<TwsLivePosition> Positions { get; set; } = new();
}

/// <summary>TWS 客户端抽象（实现于基础设施层，避免 Application 依赖具体 socket 实现）。</summary>
public interface ITwsClient : ITwsNewsClient, IDisposable
{
    Task<TwsSnapshot> PullSnapshotAsync(CancellationToken ct, TimeSpan? connectTimeout = null, TimeSpan? dataTimeout = null);

    /// <summary>建立持久连接并订阅实时行情，返回初始快照（连接保持不关闭）。</summary>
    Task<TwsSnapshot> StartLiveAsync(CancellationToken ct, TimeSpan? connectTimeout = null, TimeSpan? dataTimeout = null);

    /// <summary>读取当前实时快照；未启动或已断开返回 null。</summary>
    TwsLiveSnapshot? GetLiveSnapshot();

    /// <summary>停止实时推送并断开。</summary>
    void StopLive();

    /// <summary>实时模式下连接是否已丢失（供自动重连判断）。</summary>
    bool IsConnectionLost();

    /// <summary>
    /// 拉取历史 K 线（日/分钟），通过 reqHistoricalData。
    /// 建立连接后请求，数据返回即断开，不影响实时推送。
    /// </summary>
    Task<IReadOnlyList<TwsCandleDto>> GetHistoricalBarsAsync(
        string symbol,
        string? exchange = null,
        string? currency = "USD",
        string duration = "1 Y",
        string barSize = "1 day",
        string? endDateTime = null,
        CancellationToken ct = default,
        TimeSpan? connectTimeout = null,
        TimeSpan? dataTimeout = null,
        string? secType = null,
        string? whatToShow = null);
}

/// <summary>创建 <see cref="ITwsClient"/> 的工厂（实现于基础设施层）。</summary>
public interface ITwsNewsClient
{
    /// <summary>当前账号可用的 API 新闻源（reqNewsProviders）。</summary>
    Task<IReadOnlyList<TwsNewsProviderDto>> GetNewsProvidersAsync(CancellationToken ct);

    /// <summary>某正股/ETF 的历史头条（reqContractDetails 取 conId → reqHistoricalNews），新到旧。</summary>
    Task<IReadOnlyList<TwsNewsHeadlineDto>> GetHistoricalNewsAsync(
        string symbol, string providerCodes, int maxResults, CancellationToken ct);

    /// <summary>
    /// 订阅各新闻源的全市场快讯（NEWS 合约 + tickNews），保持连接直到取消或断线才返回。
    /// onError 收到单个新闻源的订阅错误（例如没开通）。
    /// </summary>
    Task RunNewsTapeAsync(IReadOnlyList<string> providerCodes, Action<TwsNewsHeadlineDto> onNews,
        Action<string> onError, CancellationToken ct);
}

public class TwsNewsProviderDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public class TwsNewsHeadlineDto
{
    /// <summary>IBKR 返回的原始时间串（时区以 Gateway 返回为准，由上层换算）。</summary>
    public string RawTime { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ArticleId { get; set; } = "";
    public string Headline { get; set; } = "";
}

public interface ITwsClientFactory
{
    ITwsClient Create(string host, int port, int clientId);
}

/// <summary>从 TWS / IB Gateway 拉取的单根历史 K 线（领域无关，对应 IBApi.Bar）。</summary>
public class TwsCandleDto
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}
