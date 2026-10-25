namespace IBTrade.Application.MarketData;

/// <summary>官方宏观日历。</summary>
public interface IMacroCalendar
{
    /// <summary>美联储官网 FOMC 会议日程中每次会议的最后一天（议息决议日），含今年与明年已公布的。</summary>
    Task<(IReadOnlyList<DateOnly> Dates, string? Error)> GetFomcDecisionDatesAsync(CancellationToken ct);
}
