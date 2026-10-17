namespace IBTrade.Application.OptionsIntel.Dtos;

public record OptionsContextDto
{
    public required string Symbol { get; init; }
    public string? CompanyName { get; init; }
    public string AsOfUtc { get; init; } = DateTime.UtcNow.ToString("o");
    public UnderlyingQuoteDto? Underlying { get; init; }
    public required IReadOnlyList<string> Expiries { get; init; }
    public string? SelectedExpiry { get; init; }
    public required IReadOnlyList<OptionRowDto> Chain { get; init; }
    public ChainStatsDto? Stats { get; init; }
    public required IReadOnlyList<EarningsRowDto> Earnings { get; init; }
    public required IReadOnlyList<NewsItemDto> News { get; init; }
    public required IReadOnlyList<FedEventDto> Fed { get; init; }
    public required IReadOnlyList<TreasuryRateDto> Treasury { get; init; }
    public required IReadOnlyList<PredictionMarketDto> PredictionMarkets { get; init; }
    public required IReadOnlyList<DataSourceStatusDto> Sources { get; init; }
    public HotUniverseDto? Hot { get; init; }
}

public record HotNameDto(
    int Rank,
    string Symbol,
    string? Name,
    decimal? Last,
    decimal? ChangePct,
    string Kind,
    string Bucket);

public record HotUniverseDto(
    string AsOf,
    string Rule,
    string SourceUrl,
    IReadOnlyList<HotNameDto> Names,
    bool CurrentIsHot,
    int? CurrentRank,
    DataSourceStatusDto Source);

public record UnderlyingQuoteDto(
    string Symbol,
    string? CompanyName,
    decimal? Last,
    decimal? Change,
    decimal? ChangePct,
    string? Volume,
    string? MarketStatus,
    string? LastTrade,
    string? Exchange);

public record OptionRowDto(
    string Expiry,
    decimal Strike,
    OptionSideDto Call,
    OptionSideDto Put);

public record OptionSideDto(
    decimal? Last,
    decimal? Change,
    decimal? Bid,
    decimal? Ask,
    int? Volume,
    int? OpenInterest);

public record ChainStatsDto(
    int RowCount,
    int TotalRows,
    decimal? AtmStrike,
    long CallVolume,
    long PutVolume,
    long CallOpenInterest,
    long PutOpenInterest,
    decimal? PutCallVolumeRatio,
    decimal? PutCallOiRatio);

public record EarningsRowDto(
    string FiscalQuarter,
    string DateReported,
    decimal? Eps,
    decimal? Consensus,
    decimal? SurprisePct);

public record NewsItemDto(
    string Title,
    string? Url,
    string? Published,
    string Source);

public record FedEventDto(
    string Kind,
    string Title,
    string? Date,
    string? Url,
    bool Upcoming);

public record TreasuryRateDto(
    string Security,
    decimal? RatePct,
    string? AsOf);

public record PredictionMarketDto(
    string Question,
    decimal? Probability,
    string? Url,
    string Venue,
    string? EndDate,
    decimal? PreviousProbability = null,
    decimal? Change = null,
    decimal? Volume = null);

public record DataSourceStatusDto(
    string Id,
    string Label,
    string Status,
    string? Message,
    string? Url);

public record OptionsAnalyzeRequest
{
    public string Symbol { get; set; } = "";
    public string? Expiry { get; set; }
    public string? Prompt { get; set; }
}

public record OptionsAnalyzeResult
{
    public required string Symbol { get; init; }
    public string Source { get; init; } = "heuristic";
    public double Confidence { get; init; }
    public required string Bias { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Drivers { get; init; }
    public required IReadOnlyList<string> Risks { get; init; }
    public string? SuggestedFocus { get; init; }
    public required OptionsContextDto Context { get; init; }
}
