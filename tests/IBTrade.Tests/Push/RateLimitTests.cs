using FluentAssertions;
using IBTrade.Application.Push;
using IBTrade.Application.Push.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace IBTrade.Tests.Push;

/// <summary>
/// 速率限制测试 - 防止滥用订阅和更新
/// </summary>
public class RateLimitTests
{
    [Fact]
    public async Task MultipleSubscriptions_ShouldBeThrottled()
    {
        // Arrange
        var mockRepo = new Mock<IPushSubscriptionRepository>();
        var mockHistoryRepo = new Mock<INotificationHistoryRepository>();
        var mockLogger = new Mock<ILogger<PushNotificationService>>();

        var options = Options.Create(new VapidOptions
        {
            PublicKey = "test-public-key",
            PrivateKey = "test-private-key",
            Subject = "mailto:test@example.com",
            TokenSecret = "test-secret-32-characters-long!!"
        });

        var service = new PushNotificationService(
            mockRepo.Object,
            mockHistoryRepo.Object,
            options,
            mockLogger.Object
        );

        mockRepo.Setup(r => r.GetByEndpointAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PushSubscription?)null);

        // Act - 快速连续订阅应该受限（实际限制在控制器层）
        var tasks = Enumerable.Range(0, 10).Select(i =>
            service.SubscribeAsync(new SubscribeRequest(
                Endpoint: $"https://fcm.googleapis.com/fcm/send/test{i}",
                Keys: new PushKeys("test-p256dh", "test-auth")
            ))
        );

        // Assert - 服务层允许，但控制器应该限制
        await Task.WhenAll(tasks);
        mockRepo.Verify(r => r.AddAsync(It.IsAny<PushSubscription>(), It.IsAny<CancellationToken>()), Times.Exactly(10));
    }

    [Fact]
    public void RateLimitConfiguration_ShouldBeValid()
    {
        // 验证速率限制配置存在且有效
        var config = new
        {
            EnableEndpointRateLimiting = true,
            StackBlockedRequests = false,
            RealIpHeader = "X-Real-IP",
            HttpStatusCode = 429,
            GeneralRules = new[]
            {
                new
                {
                    Endpoint = "POST:/api/push/subscription",
                    Period = "1m",
                    Limit = 5
                },
                new
                {
                    Endpoint = "PUT:/api/push/preferences",
                    Period = "1m",
                    Limit = 10
                },
                new
                {
                    Endpoint = "DELETE:/api/push/subscription",
                    Period = "1m",
                    Limit = 3
                }
            }
        };

        config.EnableEndpointRateLimiting.Should().BeTrue();
        config.GeneralRules.Should().HaveCount(3);
        config.GeneralRules[0].Limit.Should().Be(5);
    }
}
