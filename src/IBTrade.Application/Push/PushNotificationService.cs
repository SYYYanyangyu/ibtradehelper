using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBTrade.Application.Push.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;
using DomainPushSubscription = IBTrade.Domain.Entities.PushSubscription;
using WebPushSubscription = WebPush.PushSubscription;

namespace IBTrade.Application.Push;

public class VapidOptions
{
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string Subject { get; set; } = "mailto:admin@example.com";
}

public class PushNotificationService : IPushNotificationService
{
    private readonly IPushSubscriptionRepository _subscriptionRepo;
    private readonly INotificationHistoryRepository _historyRepo;
    private readonly VapidOptions _vapidOptions;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        IPushSubscriptionRepository subscriptionRepo,
        INotificationHistoryRepository historyRepo,
        IOptions<VapidOptions> vapidOptions,
        ILogger<PushNotificationService> logger)
    {
        _subscriptionRepo = subscriptionRepo;
        _historyRepo = historyRepo;
        _vapidOptions = vapidOptions.Value;
        _logger = logger;
    }

    public Task<string> GetVapidPublicKeyAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_vapidOptions.PublicKey))
        {
            throw new InvalidOperationException("VAPID 公钥未配置。请在 appsettings.json 中设置 Vapid:PublicKey");
        }
        return Task.FromResult(_vapidOptions.PublicKey);
    }

    public async Task SubscribeAsync(SubscribeRequest request, CancellationToken ct = default)
    {
        var existing = await _subscriptionRepo.GetByEndpointAsync(request.Endpoint, ct);
        if (existing != null)
        {
            existing.IsActive = true;
            existing.FailureCount = 0;
            existing.LastErrorMessage = null;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _subscriptionRepo.UpdateAsync(existing, ct);
            _logger.LogInformation("重新激活推送订阅: {Endpoint}", request.Endpoint);
            return;
        }

        var subscription = new DomainPushSubscription
        {
            Endpoint = request.Endpoint,
            P256dh = request.Keys.P256dh,
            Auth = request.Keys.Auth,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _subscriptionRepo.AddAsync(subscription, ct);
        _logger.LogInformation("新增推送订阅: {Endpoint}", request.Endpoint);
    }

    public async Task UnsubscribeAsync(string endpoint, CancellationToken ct = default)
    {
        await _subscriptionRepo.DeleteAsync(endpoint, ct);
        _logger.LogInformation("删除推送订阅: {Endpoint}", endpoint);
    }

    public async Task<NotificationPreferences> GetPreferencesAsync(string endpoint, CancellationToken ct = default)
    {
        var subscription = await _subscriptionRepo.GetByEndpointAsync(endpoint, ct);
        if (subscription == null)
        {
            throw new InvalidOperationException("订阅不存在");
        }

        return new NotificationPreferences(
            subscription.EnableScheduledBriefs,
            subscription.EnableBreakingNews,
            subscription.ScheduledBriefTimes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            subscription.Timezone
        );
    }

    public async Task UpdatePreferencesAsync(string endpoint, NotificationPreferences preferences, CancellationToken ct = default)
    {
        var subscription = await _subscriptionRepo.GetByEndpointAsync(endpoint, ct);
        if (subscription == null)
        {
            throw new InvalidOperationException("订阅不存在");
        }

        subscription.EnableScheduledBriefs = preferences.EnableScheduledBriefs;
        subscription.EnableBreakingNews = preferences.EnableBreakingNews;
        subscription.ScheduledBriefTimes = string.Join(",", preferences.ScheduledBriefTimes);
        subscription.Timezone = preferences.Timezone;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        await _subscriptionRepo.UpdateAsync(subscription, ct);
        _logger.LogInformation("更新推送偏好: {Endpoint}", endpoint);
    }

    public async Task SendNotificationAsync(SendPushRequest request, CancellationToken ct = default)
    {
        var subscriptions = request.Type switch
        {
            "scheduled_brief" => await _subscriptionRepo.GetScheduledBriefSubscriptionsAsync(ct),
            "breaking_news" => await _subscriptionRepo.GetBreakingNewsSubscriptionsAsync(ct),
            _ => await _subscriptionRepo.GetActiveSubscriptionsAsync(ct)
        };

        if (!subscriptions.Any())
        {
            _logger.LogWarning("没有活跃的订阅可推送");
            return;
        }

        var contentHash = ComputeContentHash(request.Title, request.Body);
        
        var recentHistory = await _historyRepo.GetRecentAsync(24, ct);
        if (recentHistory.Any(h => h.ContentHash == contentHash))
        {
            _logger.LogInformation("跳过重复推送: {Title}", request.Title);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            title = request.Title,
            body = request.Body,
            url = request.Url,
            tag = request.Tag,
            important = request.Important
        });

        var successCount = 0;
        var failureCount = 0;

        foreach (var subscription in subscriptions)
        {
            try
            {
                await SendPushToSubscriptionAsync(subscription, payload, ct);
                successCount++;
                
                subscription.LastNotifiedUtc = DateTime.UtcNow;
                subscription.FailureCount = 0;
                subscription.LastErrorMessage = null;
                await _subscriptionRepo.UpdateAsync(subscription, ct);
            }
            catch (WebPushException ex)
            {
                failureCount++;
                _logger.LogWarning(ex, "推送失败: {Endpoint}, 状态码: {StatusCode}", subscription.Endpoint, ex.StatusCode);

                subscription.FailureCount++;
                subscription.LastErrorMessage = ex.Message;

                if (ex.StatusCode == System.Net.HttpStatusCode.Gone || subscription.FailureCount >= 5)
                {
                    subscription.IsActive = false;
                    _logger.LogInformation("禁用失败订阅: {Endpoint}", subscription.Endpoint);
                }

                await _subscriptionRepo.UpdateAsync(subscription, ct);
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(ex, "推送异常: {Endpoint}", subscription.Endpoint);
            }
        }

        var history = new NotificationHistory
        {
            Type = request.Type ?? "manual",
            Title = request.Title,
            Body = request.Body,
            Url = request.Url,
            Tag = request.Tag,
            SentAtUtc = DateTime.UtcNow,
            SubscriptionsSent = subscriptions.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            ContentHash = contentHash
        };

        await _historyRepo.AddAsync(history, ct);
        _logger.LogInformation("推送完成: {Title}, 成功 {Success}/{Total}", request.Title, successCount, subscriptions.Count);
    }

    public async Task SendScheduledBriefAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("开始发送定时金融简报");
        
        var request = new SendPushRequest(
            "金融简报已更新",
            "查看最新的市场动态和重要资讯",
            "/",
            "scheduled-brief",
            false,
            "scheduled_brief"
        );

        await SendNotificationAsync(request, ct);
    }

    public async Task SendScheduledBriefWithContentAsync(string briefSummary, CancellationToken ct = default)
    {
        _logger.LogInformation("开始发送定时金融简报（带内容摘要）");
        
        var request = new SendPushRequest(
            "金融简报已更新",
            briefSummary,
            "/",
            "scheduled-brief",
            false,
            "scheduled_brief"
        );

        await SendNotificationAsync(request, ct);
    }

    public async Task SendBreakingNewsAsync(string title, string body, string? url = null, CancellationToken ct = default)
    {
        _logger.LogInformation("发送突发新闻: {Title}", title);
        
        var request = new SendPushRequest(
            title,
            body,
            url,
            "breaking-news",
            true,
            "breaking_news"
        );

        await SendNotificationAsync(request, ct);
    }

    public Task<int> GetActiveSubscriptionCountAsync(CancellationToken ct = default)
    {
        return _subscriptionRepo.CountActiveSubscriptionsAsync(ct);
    }

    private async Task SendPushToSubscriptionAsync(DomainPushSubscription subscription, string payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_vapidOptions.PrivateKey))
        {
            throw new InvalidOperationException("VAPID 私钥未配置。请在 appsettings.json 中设置 Vapid:PrivateKey");
        }

        var pushSubscription = new WebPushSubscription(
            subscription.Endpoint,
            subscription.P256dh,
            subscription.Auth
        );

        var vapidDetails = new VapidDetails(
            _vapidOptions.Subject,
            _vapidOptions.PublicKey,
            _vapidOptions.PrivateKey
        );

        var webPushClient = new WebPushClient();
        await webPushClient.SendNotificationAsync(pushSubscription, payload, vapidDetails, ct);
    }

    private static string ComputeContentHash(string title, string body)
    {
        var combined = $"{title}|{body}";
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
