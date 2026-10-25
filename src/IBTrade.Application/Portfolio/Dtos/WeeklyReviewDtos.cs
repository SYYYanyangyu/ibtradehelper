namespace IBTrade.Application.Portfolio.Dtos;

public record ReviewViolationDto(string Key, string Label, int Count, int Wins, decimal Net);

public record ReviewTradeDto(
    long OpenTradeId,
    string Symbol,
    DateTime OpenedAtUtc,
    DateTime ClosedAtUtc,
    decimal Premium,
    decimal Net,
    IReadOnlyList<string> Violations);

public record ReviewWeekDto(
    string WeekStart,
    string WeekEnd,
    int Trades,
    int Wins,
    decimal WinRate,
    decimal Net,
    decimal AvgWin,
    decimal AvgLoss,
    decimal? ProfitFactor,
    decimal Expectancy,
    decimal MaxDrawdown,
    int CompliantTrades,
    decimal CompliantNet,
    decimal CompliantWinRate,
    int ViolatingTrades,
    decimal ViolatingNet,
    decimal? AccountTradingPnl,
    decimal? AccountReturnPct,
    decimal? BenchmarkReturnPct,
    IReadOnlyList<ReviewViolationDto> Violations,
    IReadOnlyList<ReviewTradeDto> WorstViolations);

public record WeeklyReviewDto(
    DateTime AsOfUtc,
    string Benchmark,
    string RulesSummary,
    IReadOnlyList<string> DataNotes,
    IReadOnlyList<ReviewViolationDto> PeriodViolations,
    IReadOnlyList<ReviewWeekDto> Weeks);
