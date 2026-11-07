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
/// SSRF防护测试 - 订阅端点必须拒绝危险目标
/// </summary>
public class SSRFValidationTests
{
    private readonly Mock<IPushSubscriptionRepository> _mockRepo;
    private readonly Mock<INotificationHistoryRepository> _mockHistoryRepo;
    private readonly Mock<ILogger<PushNotificationService>> _mockLogger;
    private readonly PushNotificationService _service;

    public SSRFValidationTests()
    {
        _mockRepo = new Mock<IPushSubscriptionRepository>();
        _mockHistoryRepo = new Mock<INotificationHistoryRepository>();
        _mockLogger = new Mock<ILogger<PushNotificationService>>();

        var options = Options.Create(new VapidOptions
        {
            PublicKey = "test-public-key",
            PrivateKey = "test-private-key",
            Subject = "mailto:test@example.com",
            TokenSecret = "test-secret-32-characters-long!!"
        });

        _service = new PushNotificationService(
            _mockRepo.Object,
            _mockHistoryRepo.Object,
            options,
            _mockLogger.Object
        );
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/test", "非HTTPS协议")]
    [InlineData("https://localhost/push", "loopback主机")]
    [InlineData("https://127.0.0.1/push", "loopback IP")]
    [InlineData("https://192.168.1.1/push", "私有IP")]
    [InlineData("https://10.0.0.1/push", "私有IP")]
    [InlineData("https://172.16.0.1/push", "私有IP")]
    [InlineData("https://[::1]/push", "IPv6 loopback")]
    [InlineData("https://[fe80::1]/push", "IPv6 link-local")]
    [InlineData("https://evil.com/push", "非白名单域名")]
    [InlineData("https://fcm.googleapis.com.evil.com/push", "域名后缀伪造")]
    [InlineData("https://user:pass@fcm.googleapis.com/push", "userinfo")]
    [InlineData("https://fcm.googleapis.com:8080/push", "非标准端口")]
    public async Task Subscribe_ShouldRejectDangerousEndpoints(string endpoint, string reason)
    {
        // Arrange
        var request = new SubscribeRequest(
            Endpoint: endpoint,
            Keys: new PushKeys("test-p256dh", "test-auth")
        );

        _mockRepo.Setup(r => r.GetByEndpointAsync(endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PushSubscription?)null);

        // Act
        var act = async () => await _service.SubscribeAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>($"应拒绝 {reason}");
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PushSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/test")]
    [InlineData("https://android.googleapis.com/gcm/send/test")]
    [InlineData("https://updates.push.services.mozilla.com/push/v1/test")]
    [InlineData("https://web.push.apple.com/test")]
    [InlineData("https://wns2-db5p.notify.windows.com/test")]
    public async Task Subscribe_ShouldAcceptTrustedProviders(string endpoint)
    {
        // Arrange
        var request = new SubscribeRequest(
            Endpoint: endpoint,
            Keys: new PushKeys("test-p256dh", "test-auth")
        );

        _mockRepo.Setup(r => r.GetByEndpointAsync(endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PushSubscription?)null);

        // Act
        var act = async () => await _service.SubscribeAsync(request);

        // Assert
        await act.Should().NotThrowAsync($"应允许可信推送服务 {endpoint}");
    }
}
