using IBTrade.Application.Portfolio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IBTrade.Api.HostedServices;

/// <summary>
/// 每日净值快照后台服务。默认不启用，设环境变量 <c>DAILY_SNAPSHOT__ENABLED=true</c> 开启。
/// 每个交易日临近收盘（UTC 21:00 之后）自动抓取一次快照，积累"每天赚多少"的历史曲线。
/// </summary>
public class DailySnapshotHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DailySnapshotHostedService> logger) : BackgroundService
{
    // 美东收盘 16:00 ET ≈ 21:00 UTC（冬令时）/ 20:00 UTC（夏令时），取 21:00 留余量
    private static readonly TimeSpan CaptureAfterUtc = new(21, 0, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTime.UtcNow;
                if (now.TimeOfDay >= CaptureAfterUtc && IsWeekday(now))
                {
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var svc = scope.ServiceProvider.GetRequiredService<ISnapshotService>();
                        await svc.CaptureTodayAsync(null);
                        logger.LogInformation("每日净值快照已生成");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "每日净值快照生成失败");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 服务关闭，正常退出
        }
    }

    private static bool IsWeekday(DateTime dt) =>
        dt.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
