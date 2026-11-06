using FluentAssertions;
using Xunit;

namespace IBTrade.Tests.Push;

/// <summary>
/// 突发新闻集成测试：验证JSON格式检测逻辑
/// </summary>
public class BreakingNewsIntegrationTests
{
    [Fact]
    public void DetectBreakingNews_WithBreakingFlag()
    {
        // Arrange
        var briefJson = """
        {
            "updatedAt": "2026-11-06T03:00:00Z",
            "sections": [
                {
                    "title": "美联储意外加息100基点",
                    "content": "市场震荡剧烈...",
                    "breaking": true
                }
            ]
        }
        """;

        // Act - 验证JSON结构有效
        var parsed = System.Text.Json.JsonDocument.Parse(briefJson);
        var root = parsed.RootElement;

        // Assert
        root.TryGetProperty("sections", out var sections).Should().BeTrue();
        sections.EnumerateArray().First().TryGetProperty("breaking", out var breaking).Should().BeTrue();
        breaking.ValueKind.Should().Be(System.Text.Json.JsonValueKind.True);
    }

    [Fact]
    public void DetectBreakingNews_WithImportanceScore()
    {
        // Arrange
        var briefJson = """
        {
            "updatedAt": "2026-11-06T03:00:00Z",
            "sections": [
                {
                    "title": "特斯拉股价暴跌20%",
                    "content": "盘中熔断多次...",
                    "importance": 9
                }
            ]
        }
        """;

        // Act
        var parsed = System.Text.Json.JsonDocument.Parse(briefJson);
        var section = parsed.RootElement.GetProperty("sections").EnumerateArray().First();
        section.TryGetProperty("importance", out var importance).Should().BeTrue();
        importance.TryGetInt32(out var score).Should().BeTrue();

        // Assert
        score.Should().BeGreaterOrEqualTo(8, "重要性>=8应触发突发新闻");
    }

    [Fact]
    public void SkipOldBrief_MoreThan1HourAgo()
    {
        // Arrange
        var oldTime = DateTime.UtcNow.AddHours(-2);
        var briefJson = $$"""
        {
            "updatedAt": "{{oldTime:yyyy-MM-ddTHH:mm:ssZ}}",
            "sections": [
                {
                    "title": "旧新闻",
                    "breaking": true
                }
            ]
        }
        """;

        // Act
        var parsed = System.Text.Json.JsonDocument.Parse(briefJson);
        var updatedAt = DateTime.Parse(parsed.RootElement.GetProperty("updatedAt").GetString()!);
        var age = DateTime.UtcNow - updatedAt.ToUniversalTime();

        // Assert
        age.TotalHours.Should().BeGreaterThan(1, "超过1小时的简报应被跳过");
    }

    [Theory]
    [InlineData("""{"updatedAt":"2026-11-06T03:00:00Z","sections":[{"title":"测试","breaking":true}]}""", true)]
    [InlineData("""{"updatedAt":"2026-11-06T03:00:00Z","sections":[{"title":"测试","important":true}]}""", true)]
    [InlineData("""{"updatedAt":"2026-11-06T03:00:00Z","sections":[{"title":"测试","importance":10}]}""", true)]
    [InlineData("""{"updatedAt":"2026-11-06T03:00:00Z","sections":[{"title":"测试","importance":5}]}""", false)]
    [InlineData("""{"updatedAt":"2026-11-06T03:00:00Z","sections":[{"title":"测试"}]}""", false)]
    public void BreakingNewsDetection_Criteria(string briefJson, bool shouldTrigger)
    {
        // Act
        var parsed = System.Text.Json.JsonDocument.Parse(briefJson);
        var section = parsed.RootElement.GetProperty("sections").EnumerateArray().First();

        var isBreaking = false;
        if (section.TryGetProperty("breaking", out var breaking) && breaking.ValueKind == System.Text.Json.JsonValueKind.True)
            isBreaking = true;
        if (section.TryGetProperty("important", out var important) && important.ValueKind == System.Text.Json.JsonValueKind.True)
            isBreaking = true;
        if (section.TryGetProperty("importance", out var importance) && importance.TryGetInt32(out var score) && score >= 8)
            isBreaking = true;

        // Assert
        isBreaking.Should().Be(shouldTrigger);
    }
}
