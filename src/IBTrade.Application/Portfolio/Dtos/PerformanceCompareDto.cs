namespace IBTrade.Application.Portfolio.Dtos;

public record PerformanceSeriesDto(
    string Id,
    string Label,
    decimal? PeriodReturnPct,
    string Status,
    string? Message);

public record PerformancePointDto(
    string Date,
    decimal? Portfolio,
    decimal? Hsi,
    decimal? Spx,
    decimal? Dji,
    decimal? Ixic);

public record PerformanceCompareDto(
    string Range,
    string AsOfUtc,
    string PortfolioLabel,
    IReadOnlyList<PerformanceSeriesDto> Series,
    IReadOnlyList<PerformancePointDto> Points,
    string? FromDate = null,
    string? ToDate = null,
    decimal? StartNav = null,
    decimal? EndNav = null,
    string? Method = null);
