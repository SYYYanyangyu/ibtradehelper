using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.MarketData;

/// <summary>历史行情服务接口：向 IB Gateway 拉取历史 K 线，供回测/策略使用（与看板无关）。</summary>
public interface IHistoricalDataService
{
    Task<IReadOnlyList<CandleDto>> GetBarsAsync(
        string symbol,
        string? exchange = null,
        string? currency = "USD",
        string duration = "1 Y",
        string barSize = "1 day",
        CancellationToken ct = default);
}
