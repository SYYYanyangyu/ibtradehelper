using IBTrade.Application.IBKR;
using IBTrade.Application.MarketData;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IBTrade.Api.HostedServices;

/// <summary>
/// 常驻订阅 IBKR 各新闻源的全市场快讯，写入 MarketNewsService 的滚动缓冲；断线后 30 秒重连。
/// 只连当前模式（Paper / Live）对应的端口。
/// </summary>
public class NewsTapeHostedService(
    MarketNewsService news, ITwsClientFactory factory, TwsOptions options, ILogger<NewsTapeHostedService> logger)
    : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 快讯（NEWS 合约）的源代码与 reqNewsProviders 的历史源不同，Gateway 只接受这些；
    /// 没开通的会逐个报错并显示在状态里。
    /// </summary>
    private static readonly string[] TapeCodes = ["BRFG", "DJNL", "BZ", "FLY"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            string? error = null;
            try
            {
                foreach (var port in options.PortsFor(options.Mode))
                {
                    try
                    {
                        using var client = factory.Create(options.Host, port, options.NewsClientId);
                        news.SetTapeState(true, null);
                        logger.LogInformation("新闻快讯已订阅（端口 {Port}）：{Codes}", port, string.Join(",", TapeCodes));
                        await client.RunNewsTapeAsync(TapeCodes, news.Ingest, news.AddTapeProviderError, stoppingToken);
                        error = "连接断开，稍后重连";
                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        error = ex.GetBaseException().Message;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
            }

            news.SetTapeState(false, error);
            if (error is not null) logger.LogWarning("新闻快讯：{Error}", error);
            try { await Task.Delay(RetryDelay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
        news.SetTapeState(false, null);
    }
}
