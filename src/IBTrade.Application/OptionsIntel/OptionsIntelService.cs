using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Application.OptionsIntel;

public class OptionsIntelService(
    IOptionsChainClient chain,
    ICompanyFundamentalsClient company,
    IMacroDataClient macro,
    INewsFeedClient news,
    IPredictionMarketClient prediction,
    IHotOptionsUniverse hot,
    IOptionsAiAdvisor advisor,
    ILogger<OptionsIntelService>? logger = null) : IOptionsIntelService
{
    private static readonly ConcurrentDictionary<string, (DateTime Exp, OptionsContextDto Ctx)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    public async Task<OptionsContextDto> GetContextAsync(string symbol, string? expiry, CancellationToken ct = default)
    {
        var sym = NormalizeSymbol(symbol);
        var expKey = string.IsNullOrWhiteSpace(expiry) ? "_" : expiry.Trim();
        var cacheKey = $"{sym}|{expKey}";
        if (Cache.TryGetValue(cacheKey, out var hit) && hit.Exp > DateTime.UtcNow)
            return hit.Ctx;

        var ctx = await FetchAsync(sym, expiry, ct);
        Cache[cacheKey] = (DateTime.UtcNow.Add(Ttl), ctx);
        return ctx;
    }

    public async Task<OptionsAnalyzeResult> AnalyzeAsync(OptionsAnalyzeRequest request, CancellationToken ct = default)
    {
        var ctx = await GetContextAsync(request.Symbol, request.Expiry, ct);
        return await advisor.AnalyzeAsync(ctx, request, ct);
    }

    public Task<HotUniverseDto> GetHotAsync(string? symbol, CancellationToken ct = default)
        => hot.GetAsync(string.IsNullOrWhiteSpace(symbol) ? null : NormalizeSymbol(symbol), ct);

    private async Task<OptionsContextDto> FetchAsync(string symbol, string? expiry, CancellationToken ct)
    {
        OptionsChainSnapshot? chainSnap = null;
        CompanySnapshot? companySnap = null;
        MacroSnapshot? macroSnap = null;
        IReadOnlyList<NewsItemDto> newsItems = [];
        DataSourceStatusDto? newsSrc = null;
        IReadOnlyList<PredictionMarketDto> preds = [];
        DataSourceStatusDto? predSrc = null;

        var chainTask = chain.GetAsync(symbol, expiry, ct);
        var companyTask = company.GetAsync(symbol, ct);
        var macroTask = macro.GetAsync(ct);
        var newsTask = news.GetAsync(symbol, null, ct);
        var predTask = prediction.SearchAsync(symbol, null, ct);
        var hotTask = hot.GetAsync(symbol, ct);
        try { await Task.WhenAll(chainTask, companyTask, macroTask, newsTask, predTask, hotTask); }
        catch { /* 单个失败在各自 await 中处理 */ }

        try { chainSnap = await chainTask; }
        catch (Exception ex) { logger?.LogWarning(ex, "期权链失败"); }
        try { companySnap = await companyTask; }
        catch (Exception ex) { logger?.LogWarning(ex, "公司数据失败"); }
        try { macroSnap = await macroTask; }
        catch (Exception ex) { logger?.LogWarning(ex, "宏观数据失败"); }
        try
        {
            var n = await newsTask;
            newsItems = n.Items;
            newsSrc = n.Source;
        }
        catch (Exception ex) { logger?.LogWarning(ex, "新闻失败"); }
        try
        {
            var p = await predTask;
            preds = p.Markets;
            predSrc = p.Source;
        }
        catch (Exception ex) { logger?.LogWarning(ex, "预测市场失败"); }

        var q1 = chainSnap?.Underlying;
        var q2 = companySnap?.Quote;
        UnderlyingQuoteDto? quote = q1 is null && q2 is null
            ? null
            : new UnderlyingQuoteDto(
                symbol,
                q2?.CompanyName ?? q1?.CompanyName,
                q2?.Last ?? q1?.Last,
                q2?.Change ?? q1?.Change,
                q2?.ChangePct ?? q1?.ChangePct,
                q2?.Volume ?? q1?.Volume,
                q2?.MarketStatus ?? q1?.MarketStatus,
                q2?.LastTrade ?? q1?.LastTrade,
                q2?.Exchange ?? q1?.Exchange);
        var companyName = quote?.CompanyName;

        var sources = new List<DataSourceStatusDto>();
        if (chainSnap is not null) sources.Add(chainSnap.Source);
        if (companySnap is not null)
        {
            sources.Add(companySnap.QuoteSource);
            sources.Add(companySnap.EarningsSource);
        }
        if (macroSnap is not null)
        {
            sources.Add(macroSnap.FedSource);
            sources.Add(macroSnap.TreasurySource);
        }
        if (newsSrc is not null) sources.Add(newsSrc);
        if (predSrc is not null) sources.Add(predSrc);
        HotUniverseDto? hotSnap = null;
        try { hotSnap = await hotTask; }
        catch (Exception ex) { logger?.LogWarning(ex, "热门榜失败"); }
        if (hotSnap is not null) sources.Add(hotSnap.Source);

        return new OptionsContextDto
        {
            Symbol = symbol,
            CompanyName = companyName,
            Underlying = quote,
            Expiries = chainSnap?.Expiries ?? [],
            SelectedExpiry = chainSnap?.SelectedExpiry,
            Chain = chainSnap?.Rows ?? [],
            Stats = chainSnap?.Stats,
            Earnings = companySnap?.Earnings ?? [],
            News = newsItems,
            Fed = macroSnap?.Fed ?? [],
            Treasury = macroSnap?.Treasury ?? [],
            PredictionMarkets = preds,
            Sources = sources,
            Hot = hotSnap
        };
    }

    internal static string NormalizeSymbol(string? symbol)
    {
        var s = (symbol ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(s, @"^[A-Z][A-Z0-9.\-]{0,11}$"))
            throw new ArgumentException("标的代码无效，例如 AAPL / NVDA / MRVL。");
        return s;
    }
}
