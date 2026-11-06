using IBTrade.Application.Push;

namespace IBTrade.Api.HostedServices;

public class ScheduledPushHostedService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledPushHostedService> _logger;
    private Timer? _timer;

    public ScheduledPushHostedService(
        IServiceProvider serviceProvider,
        ILogger<ScheduledPushHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("定时推送服务已启动");
        _timer = new Timer(CheckAndSendPush, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    private async void CheckAndSendPush(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var shanghaiTime = TimeZoneInfo.ConvertTimeFromUtc(
                now,
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")
            );

            if (shanghaiTime.DayOfWeek == DayOfWeek.Saturday || shanghaiTime.DayOfWeek == DayOfWeek.Sunday)
            {
                return;
            }

            var hour = shanghaiTime.Hour;
            var minute = shanghaiTime.Minute;

            if ((hour == 8 || hour == 21) && minute == 0)
            {
                _logger.LogInformation("触发定时推送: {Time}", shanghaiTime);
                
                using var scope = _serviceProvider.CreateScope();
                var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
                
                await pushService.SendScheduledBriefAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定时推送检查失败");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("定时推送服务已停止");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
