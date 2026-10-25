using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.MarketData;

public interface IOfficialIndexQuotes
{
    Task<(IReadOnlyList<CandleDto> Bars, string? Error)> GetFredAsync(string seriesId, CancellationToken ct);
}
