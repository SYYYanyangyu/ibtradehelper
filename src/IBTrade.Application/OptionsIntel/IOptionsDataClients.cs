using IBTrade.Application.OptionsIntel.Dtos;

namespace IBTrade.Application.OptionsIntel;

public record OptionsChainSnapshot(
    UnderlyingQuoteDto? Underlying,
    IReadOnlyList<string> Expiries,
    string? SelectedExpiry,
    IReadOnlyList<OptionRowDto> Rows,
    ChainStatsDto Stats,
    DataSourceStatusDto Source);

public record CompanySnapshot(
    UnderlyingQuoteDto? Quote,
    IReadOnlyList<EarningsRowDto> Earnings,
    DataSourceStatusDto QuoteSource,
    DataSourceStatusDto EarningsSource);

public record MacroSnapshot(
    IReadOnlyList<FedEventDto> Fed,
    IReadOnlyList<TreasuryRateDto> Treasury,
    DataSourceStatusDto FedSource,
    DataSourceStatusDto TreasurySource);

public interface IHotOptionsUniverse
{
    Task<HotUniverseDto> GetAsync(string? symbol, CancellationToken ct = default);
}

public interface IOptionsChainClient
{
    Task<OptionsChainSnapshot> GetAsync(string symbol, string? expiry, CancellationToken ct = default);
}

public interface ICompanyFundamentalsClient
{
    Task<CompanySnapshot> GetAsync(string symbol, CancellationToken ct = default);
}

public interface IMacroDataClient
{
    Task<MacroSnapshot> GetAsync(CancellationToken ct = default);
}

public interface INewsFeedClient
{
    Task<(IReadOnlyList<NewsItemDto> Items, DataSourceStatusDto Source)> GetAsync(
        string symbol, string? companyName, CancellationToken ct = default);
}

public interface IPredictionMarketClient
{
    Task<(IReadOnlyList<PredictionMarketDto> Markets, DataSourceStatusDto Source)> SearchAsync(
        string symbol, string? companyName, CancellationToken ct = default);
}

public interface IOptionsAiAdvisor
{
    Task<OptionsAnalyzeResult> AnalyzeAsync(
        OptionsContextDto context, OptionsAnalyzeRequest request, CancellationToken ct = default);
}
