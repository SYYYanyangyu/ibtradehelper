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
/// 订阅所有权验证测试
/// </summary>
public class SubscriptionOwnershipTests
{
    private readonly Mock<IPushSubscriptionRepository> _mockRepo;
    private readonly Mock<INotificationHistoryRepository> _mockHistoryRepo;
    private readonly Mock<ILogger<PushNotificationService>> _mockLogger;
    private readonly PushNotificationService _service;
    private const string TestSecret = "test-secret-32-characters-long!!";

    public SubscriptionOwnershipTests()
    {
        _mockRepo = new Mock<IPushSubscriptionRepository>();
        _mockHistoryRepo = new Mock<INotificationHistoryRepository>();
        _mockLogger = new Mock<ILogger<PushNotificationService>>();

        var options = Options.Create(new VapidOptions
        {
            PublicKey = "test-public-key",
            PrivateKey = "test-private-key",
            Subject = "mailto:test@example.com",
            TokenSecret = TestSecret
        });

        _service = new PushNotificationService(
            _mockRepo.Object,
            _mockHistoryRepo.Object,
            options,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task Subscribe_ShouldReturnOwnershipToken()
    {
        // Arrange
        var request = new SubscribeRequest(
            Endpoint: "https://fcm.googleapis.com/test",
            Keys: new PushKeys("test-p256dh", "test-auth")
        );

        _mockRepo.Setup(r => r.GetByEndpointAsync(request.Endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PushSubscription?)null);

        // Act
        var response = await _service.SubscribeAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.OwnershipToken.Should().NotBeNullOrEmpty();
        response.OwnershipToken.Should().Contain(".");
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PushSubscription>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unsubscribe_WithValidToken_ShouldSucceed()
    {
        // Arrange
        var endpoint = "https://fcm.googleapis.com/test";
        var token = SubscriptionToken.Generate(endpoint, TestSecret);
        
        var subscription = new PushSubscription
        {
            Endpoint = endpoint,
            OwnershipToken = token,
            P256dh = "test-p256dh",
            Auth = "test-auth"
        };

        _mockRepo.Setup(r => r.GetByEndpointAsync(endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        // Act
        var act = async () => await _service.UnsubscribeAsync(endpoint, token);

        // Assert
        await act.Should().NotThrowAsync();
        _mockRepo.Verify(r => r.DeleteAsync(endpoint, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unsubscribe_WithInvalidToken_ShouldThrowUnauthorized()
    {
        // Arrange
        var endpoint = "https://fcm.googleapis.com/test";
        var validToken = SubscriptionToken.Generate(endpoint, TestSecret);
        var invalidToken = "invalid-token";

        var subscription = new PushSubscription
        {
            Endpoint = endpoint,
            OwnershipToken = validToken,
            P256dh = "test-p256dh",
            Auth = "test-auth"
        };

        _mockRepo.Setup(r => r.GetByEndpointAsync(endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        // Act
        var act = async () => await _service.UnsubscribeAsync(endpoint, invalidToken);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unsubscribe_CrossSubscriptionAttempt_ShouldFail()
    {
        // Arrange
        var endpoint1 = "https://fcm.googleapis.com/endpoint1";
        var endpoint2 = "https://fcm.googleapis.com/endpoint2";
        var token1 = SubscriptionToken.Generate(endpoint1, TestSecret);

        var subscription2 = new PushSubscription
        {
            Endpoint = endpoint2,
            OwnershipToken = SubscriptionToken.Generate(endpoint2, TestSecret),
            P256dh = "test-p256dh",
            Auth = "test-auth"
        };

        _mockRepo.Setup(r => r.GetByEndpointAsync(endpoint2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription2);

        // Act - 尝试用endpoint1的token删除endpoint2
        var act = async () => await _service.UnsubscribeAsync(endpoint2, token1);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePreferences_WithValidToken_ShouldSucceed()
    {
        // Arrange
        var endpoint = "https://fcm.googleapis.com/test";
        var token = SubscriptionToken.Generate(endpoint, TestSecret);

        var subscription = new PushSubscription
        {
            Endpoint = endpoint,
            OwnershipToken = token,
            P256dh = "test-p256dh",
            Auth = "test-auth",
            EnableScheduledBriefs = true,
            EnableBreakingNews = true
        };

        _mockRepo.Setup(r => r.GetByEndpointAsync(endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var preferences = new NotificationPreferences(
            EnableScheduledBriefs: false,
            EnableBreakingNews: true,
            ScheduledBriefTimes: new List<string> { "08:00" },
            Timezone: "Asia/Shanghai"
        );

        // Act
        await _service.UpdatePreferencesAsync(endpoint, token, preferences);

        // Assert
        _mockRepo.Verify(r => r.UpdateAsync(It.Is<PushSubscription>(s =>
            s.EnableScheduledBriefs == false &&
            s.EnableBreakingNews == true
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePreferences_WithInvalidToken_ShouldThrowUnauthorized()
    {
        // Arrange
        var endpoint = "https://fcm.googleapis.com/test";
        var validToken = SubscriptionToken.Generate(endpoint, TestSecret);

        var subscription = new PushSubscription
        {
            Endpoint = endpoint,
            OwnershipToken = validToken,
            P256dh = "test-p256dh",
            Auth = "test-auth"
        };

        _mockRepo.Setup(r => r.GetByEndpointAsync(endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var preferences = new NotificationPreferences(
            EnableScheduledBriefs: false,
            EnableBreakingNews: true,
            ScheduledBriefTimes: new List<string> { "08:00" },
            Timezone: "Asia/Shanghai"
        );

        // Act
        var act = async () => await _service.UpdatePreferencesAsync(endpoint, "invalid-token", preferences);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _mockRepo.Verify(r => r.UpdateAsync(It.IsAny<PushSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
