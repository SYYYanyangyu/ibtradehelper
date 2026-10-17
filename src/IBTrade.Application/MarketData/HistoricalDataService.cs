using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using IBTrade.Application.IBKR;
using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.MarketData;

/// <summary>
/// 历史行情服务实现：通过 ITwsClientFactory 建立一条短连接向 IB Gateway 拉取历史 K 线，拉完即断。
/// 使用独立 clientId 区间（ClientId+2 起），并通过 clientId 池 + 信号量限制并发，
/// 避免多个并发历史请求（回测/自动交易叠加）复用同一 clientId 而被 Gateway 互相踢掉。
/// 该模块与看板/组合完全独立，是量化交易（回测/策略）的数据入口。
/// </summary>
public class HistoricalDataService(TwsOptions options, ITwsClientFactory clientFactory) : IHistoricalDataService
{
    /// <summary>可用 clientId 池：从 max(ClientId+2, OrderClientId+1) 起，避开实时推送(0)/实时同步(ClientId+1)/下单持久连接(OrderClientId)。</summary>
    private readonly ConcurrentQueue<int> _clientIdPool =
        new(Enumerable.Range(0, PoolSize).Select(i => Math.Max(options.ClientId + 2, options.OrderClientId + 1) + i));

    /// <summary>并发上限：同时最多 PoolSize 条历史连接；超出则排队等待空闲 clientId。</summary>
    private readonly SemaphoreSlim _poolGate = new(PoolSize, PoolSize);

    private const int PoolSize = 4;

    public async Task<IReadOnlyList<CandleDto>> GetBarsAsync(
        string symbol, string? exchange, string? currency, string duration, string barSize, CancellationToken ct)
    {
        Exception? lastError = null;
        var errors = new List<Exception>();

        // 占用一个 clientId（并发受信号量限制），用完后归还。保证任意时刻不存在两个请求共用同一 clientId。
        await _poolGate.WaitAsync(ct);
        if (!_clientIdPool.TryDequeue(out var clientId))
            clientId = Math.Max(options.ClientId + 2, options.OrderClientId + 1); // 极端兜底：池耗尽时退化（不应发生，因信号量已限流）
        try
        {
            foreach (var port in options.PortsFor(options.Mode))
            {
                try
                {
                    using var client = clientFactory.Create(options.Host, port, clientId);
                    var bars = await client.GetHistoricalBarsAsync(symbol, exchange, currency, duration, barSize, null, ct);
                    return bars.Select(b => new CandleDto(b.Time, b.Open, b.High, b.Low, b.Close, b.Volume)).ToList();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                    lastError = ex;
                }
            }
        }
        finally
        {
            _clientIdPool.Enqueue(clientId);
            _poolGate.Release();
        }

        // 优先暴露“端口已开放但 API 握手失败”——这通常是 Gateway 已启动却未登录/未授权的症状，
        // 比“端口无响应”更关键，避免被最后一个端口的失败提示掩盖真因。
        var meaningful = errors.FirstOrDefault(e => e.Message.Contains("IB API 握手失败"));
        var report = meaningful ?? lastError;
        var reason = report is null ? "" : $" 原因：{report.Message}";
        throw new InvalidOperationException(
            $"无法连接到 {options.Mode} 端口（{string.Join(", ", options.PortsFor(options.Mode))}）拉取历史数据。Paper 只连模拟口。{reason}");
    }
}
