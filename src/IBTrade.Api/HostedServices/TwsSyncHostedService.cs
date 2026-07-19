using IBTrade.Application.IBKR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IBTrade.Api.HostedServices;

/// <summary>
/// TWS 实时同步后台服务。仅当 TWS__ENABLED=true 时注册（见 Program.cs）。
/// 启动后先同步一次，之后每隔 RefreshMinutes 分钟轮询拉取实时持仓/净值。
///
/// 注意：ITwsSyncService 注册为 Scoped（其依赖的仓储均为 Scoped），而本服务是 Singleton（BackgroundService）。
/// 因此每个同步周期都必须新建一个 IServiceScope 来解析，绝不能把 Scoped 服务直接注入到 Singleton 中
/// （否则 DI 容器在启动期就会抛出“无法从根提供器解析 Scoped 服务”而崩溃）。
/// </summary>
public class TwsSyncHostedService(ILogger<TwsSyncHostedService> logger, IServiceScopeFactory scopeFactory, TwsOptions options)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.RefreshMinutes));

        // 启动后稍等，避免与 Web 主机争抢资源
        try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        await TrySync(logger, stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TrySync(logger, stoppingToken);
        }
    }

    private async Task TrySync(ILogger<TwsSyncHostedService> logger, CancellationToken ct)
    {
        try
        {
            // 每个周期在独立作用域内解析 Scoped 的 ITwsSyncService，避免 Singleton 持有 Scoped 服务。
            await using var scope = scopeFactory.CreateAsyncScope();
            var sync = scope.ServiceProvider.GetRequiredService<ITwsSyncService>();
            var snap = await sync.SyncAsync(ct);
            logger.LogInformation("TWS 实时同步完成：账户 {Account} 持仓 {Count} 笔", snap.AccountId, snap.Positions.Count);
        }
        catch (Exception ex)
        {
            // 网络/登录问题不应拖垮宿主；记录后下次重试
            logger.LogWarning(ex, "TWS 实时同步失败（将在下个周期重试）");
        }
    }
}
