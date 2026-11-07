using FluentAssertions;
using Xunit;

namespace IBTrade.Tests.Push;

/// <summary>
/// 工作日上海时区调度测试
/// </summary>
public class SchedulingTests
{
    [Theory]
    [InlineData(2026, 11, 10, 8, 0, true)]   // 周一 08:00
    [InlineData(2026, 11, 10, 21, 0, true)]  // 周一 21:00
    [InlineData(2026, 11, 11, 8, 0, true)]   // 周二 08:00
    [InlineData(2026, 11, 13, 8, 0, true)]   // 周四 08:00
    [InlineData(2026, 11, 14, 8, 0, false)]  // 周六 08:00 - 应跳过
    [InlineData(2026, 11, 15, 21, 0, false)] // 周日 21:00 - 应跳过
    [InlineData(2026, 11, 10, 9, 0, false)]  // 周一 09:00 - 非触发时间
    [InlineData(2026, 11, 10, 7, 59, false)] // 周一 07:59 - 非触发时间
    public void ShouldTrigger_WeekdayScheduling(int year, int month, int day, int hour, int minute, bool expected)
    {
        // Arrange
        var shanghaiTime = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var testTime = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));
        var localTime = TimeZoneInfo.ConvertTime(testTime, shanghaiTime);

        // Act
        var isWeekday = localTime.DayOfWeek != DayOfWeek.Saturday && localTime.DayOfWeek != DayOfWeek.Sunday;
        var isScheduledTime = (localTime.Hour == 8 || localTime.Hour == 21) && localTime.Minute == 0;
        var shouldTrigger = isWeekday && isScheduledTime;

        // Assert
        shouldTrigger.Should().Be(expected, 
            $"{localTime:yyyy-MM-dd HH:mm} ({localTime.DayOfWeek}) should{(expected ? "" : " not")} trigger");
    }

    [Fact]
    public void TimeZoneConversion_ShanghaiToUtc()
    {
        // Arrange
        var shanghaiTime = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var shanghai8am = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Unspecified);

        // Act
        var utcTime = TimeZoneInfo.ConvertTimeToUtc(shanghai8am, shanghaiTime);

        // Assert
        utcTime.Hour.Should().Be(0, "上海时间08:00应转换为UTC 00:00");
    }

    [Fact]
    public void EventId_ShouldBeUnique()
    {
        // Arrange
        var date = new DateTime(2026, 11, 10);
        var slot = "08:00";

        // Act
        var eventId1 = $"{date:yyyy-MM-dd}_{slot}_scheduled_brief";
        var eventId2 = $"{date:yyyy-MM-dd}_{slot}_scheduled_brief";
        var eventId3 = $"{date:yyyy-MM-dd}_21:00_scheduled_brief";

        // Assert
        eventId1.Should().Be(eventId2, "相同日期和时段应生成相同的事件ID");
        eventId1.Should().NotBe(eventId3, "不同时段应生成不同的事件ID");
    }
}
