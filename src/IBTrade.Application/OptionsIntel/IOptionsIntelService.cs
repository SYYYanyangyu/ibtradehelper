using IBTrade.Application.OptionsIntel.Dtos;

namespace IBTrade.Application.OptionsIntel;

public interface IOptionsIntelService
{
    Task<OptionsContextDto> GetContextAsync(string symbol, string? expiry, CancellationToken ct = default);
    Task<OptionsAnalyzeResult> AnalyzeAsync(OptionsAnalyzeRequest request, CancellationToken ct = default);
    Task<HotUniverseDto> GetHotAsync(string? symbol, CancellationToken ct = default);
}
