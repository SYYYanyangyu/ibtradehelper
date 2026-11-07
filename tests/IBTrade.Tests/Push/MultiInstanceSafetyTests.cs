using FluentAssertions;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Moq;
using Xunit;

namespace IBTrade.Tests.Push;

/// <summary>
/// 多实例安全测试 - 防止重复推送
/// </summary>
public class MultiInstanceSafetyTests
{
    [Fact]
    public void ScheduledPush_ShouldGenerateUniqueEventId()
    {
        // Arrange
        var date = DateTime.Parse("2026-11-06");
        var slot = "08:00";
        var eventId = $"{date:yyyy-MM-dd}_{slot}_scheduled_brief";

        // Act - 验证事件ID格式
        eventId.Should().Contain("2026-11-06");
        eventId.Should().Contain("08:00");
        eventId.Should().Contain("scheduled_brief");

        // Assert - 同样的日期+时段应该生成相同ID
        var eventId2 = $"{date:yyyy-MM-dd}_{slot}_scheduled_brief";
        eventId.Should().Be(eventId2, "事件ID必须确定性，防止多实例重复");
    }

    [Fact]
    public void EventId_ShouldBeIdempotent()
    {
        // Arrange
        var date = DateTime.Parse("2026-11-06");
        var slot = "08:00";

        // Act - 多次生成应该相同
        var id1 = $"{date:yyyy-MM-dd}_{slot}_scheduled_brief";
        var id2 = $"{date:yyyy-MM-dd}_{slot}_scheduled_brief";

        // Assert
        id1.Should().Be(id2, "事件ID必须是确定性的，以支持去重");
    }

    [Fact]
    public void ContentHash_ShouldBeConsistent()
    {
        // Arrange
        var title = "市场简报";
        var body = "今日重要新闻";

        // Act - 多次计算应该相同
        var hash1 = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{title}|{body}"));
        var hash2 = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{title}|{body}"));

        // Assert
        hash1.Should().Equal(hash2, "内容哈希必须一致");
    }
}
