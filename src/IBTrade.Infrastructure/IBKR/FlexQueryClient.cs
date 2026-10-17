using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using IBTrade.Application.IBKR;
using IBTrade.Application.Ingestion.Dtos;
using IBTrade.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.IBKR;

/// <summary>
/// IBKR Flex Web Service 客户端：下载报表 XML 并解析为领域无关的快照。
/// 文档：https://www.interactivebrokers.com/campus/ibkr-api-page/flex-web-service/
/// </summary>
public class FlexQueryClient(HttpClient httpClient, ILogger<FlexQueryClient>? logger = null) : IFlexQueryClient
{
    private const string DefaultBaseUrl =
        "https://ndcdyn.interactivebrokers.com/AccountManagement/FlexWebService";

    /// <summary>下载指定 Flex Query 的报表 XML（两步：SendRequest -> GetStatement）。</summary>
    public async Task<string> DownloadXmlAsync(string token, string queryId, string? baseUrl = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Flex token 为空，请配置 IBKR_FLEX_TOKEN_* 环境变量。", nameof(token));

        var root = baseUrl
                   ?? Environment.GetEnvironmentVariable("IBKR_FLEX_BASE_URL")
                   ?? DefaultBaseUrl;

        // Step 1: SendRequest（含瞬断重试）
        var sendUrl = $"{root}/SendRequest?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(queryId)}&v=3";
        var sendXml = await SendWithRetryAsync(sendUrl, "SendRequest", maxAttempts: 4, ct);
        var refCode = XDocument.Parse(sendXml).Descendants("ReferenceCode").FirstOrDefault()?.Value
                      ?? throw new InvalidOperationException("Flex SendRequest 未返回 ReferenceCode。");

        // Step 2: GetStatement 轮询。勾选 Open Positions + NAV 后报表更大，生成更慢，多轮询。
        var getUrl = $"{root}/GetStatement?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(refCode)}&v=3";
        string lastStmt = "";
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(attempt == 0 ? 2500 : 3500, ct);
            try
            {
                lastStmt = await SendWithRetryAsync(getUrl, "GetStatement", maxAttempts: 3, ct);
            }
            catch (Exception ex) when (IsTransientTransport(ex))
            {
                // 单轮读失败不中断整次摄入（大报表经代理时常见 EOF）
                logger?.LogWarning(ex, "Flex GetStatement 瞬断（轮询 {Attempt}/40），继续重试", attempt + 1);
                continue;
            }

            if (lastStmt.Contains("<FlexQueryResponse", StringComparison.Ordinal))
                return lastStmt;

            // 生成中 / 错误提示：继续轮询
            if (lastStmt.Contains("1019", StringComparison.Ordinal) ||
                lastStmt.Contains("Statement generation in progress", StringComparison.OrdinalIgnoreCase))
                continue;
        }

        var hint = lastStmt.Length > 200 ? lastStmt[..200] : lastStmt;
        throw new InvalidOperationException(
            $"Flex GetStatement 轮询约 2 分钟后报表仍未就绪（大报表或代理较慢时可再试一次）。最后响应片段：{hint}");
    }

    /// <summary>带瞬断重试的 GET；流式读入字符串，避免大包半截 SSL EOF。</summary>
    private async Task<string> SendWithRetryAsync(string url, string step, int maxAttempts, CancellationToken ct)
    {
        Exception? last = null;
        for (var i = 1; i <= maxAttempts; i++)
        {
            try
            {
                using var req = BuildRequest(url);
                using var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                EnsureOk(resp, step);
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync(ct);
            }
            catch (Exception ex) when (IsTransientTransport(ex) && i < maxAttempts)
            {
                last = ex;
                logger?.LogWarning(ex, "Flex {Step} 传输中断，{Delay}s 后重试（{Attempt}/{Max}）",
                    step, i, i, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(i * 2), ct);
            }
        }

        throw new InvalidOperationException(
            $"Flex {step} 多次因网络中断失败（SSL/代理 EOF）。请稍后重试「同步 Flex」；若持续失败可检查代理或缩小报表日期范围。",
            last);
    }

    private static bool IsTransientTransport(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is IOException or HttpRequestException)
                return true;
            if (e.Message.Contains("EOF", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("transport stream", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("connection was closed", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static HttpRequestMessage BuildRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("IBTrade/1.0 (+FlexWebService)");
        return req;
    }

    private static void EnsureOk(HttpResponseMessage resp, string step)
    {
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Flex {step} 失败：HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
    }

    public FlexSnapshot Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var statements = doc.Descendants("FlexStatement").ToList();
        if (statements.Count == 0)
            throw new InvalidOperationException("Flex 报表未包含任何 FlexStatement，请检查 Query 配置。");

        var accounts = new List<FlexAccountDto>();
        var positions = new List<FlexPositionDto>();
        var trades = new List<FlexTradeDto>();
        var cash = new List<FlexCashTransactionDto>();
        var realized = new List<FlexRealizedPnlDto>();
        var dailyNavs = new List<FlexDailyNavDto>();
        var mtmDaily = new List<FlexMtmDailyDto>();

        // Last365 会按日拆成多条 FlexStatement。开仓/账户只能取「每账户最新 toDate」，
        // 否则会把历史上每一天的 OpenPosition 累加成假持仓。
        // 另：子账户若已空仓，IB 可能不再日更该账户报表——其「最新」停在旧日，
        // OpenPosition 会是过期/已平残留；须相对整份报表的最新业务日判定是否过期。
        var reportAsOf = statements.Max(StatementAsOf).Date;
        var latestByAccount = statements
            .GroupBy(s => Attr(s, "accountId"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(StatementAsOf).First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var stmt in latestByAccount.Values)
        {
            var stmtAccountId = Attr(stmt, "accountId");
            var stmtAsOf = StatementAsOf(stmt).Date;
            // 账户最新日落后整份报表超过 5 个自然日 → 视为停更，不计当前开仓
            var openSnapshotFresh = (reportAsOf - stmtAsOf).TotalDays <= 5;

            var acct = stmt.Element("AccountInformation");
            if (acct is not null)
            {
                accounts.Add(new FlexAccountDto(
                    Acct(acct, stmtAccountId),
                    Attr(acct, "currency", "USD"),
                    Attr(acct, "acctAlias")));
            }
            else if (!string.IsNullOrWhiteSpace(stmtAccountId))
            {
                accounts.Add(new FlexAccountDto(stmtAccountId, "USD", null));
            }

            if (!openSnapshotFresh) continue;

            foreach (var e in stmt.Descendants("OpenPosition"))
                TryAddOpenPosition(e, stmtAccountId, positions, asOfDate: reportAsOf);
        }

        // SymbolSummary：按日拆分时 fifo 需跨日累加；未实现只保留「当前仍开仓」标的（取最新日）。
        var openKeys = new HashSet<(string Acct, string Sym)>(
            positions.Select(p => (p.AccountId, p.Symbol)),
            OpenKeyComparer.Instance);
        var fifoByKey = new Dictionary<(string Acct, string Sym), FlexRealizedPnlDto>(OpenKeyComparer.Instance);
        var unrealFromLatest = new Dictionary<(string Acct, string Sym), decimal>(OpenKeyComparer.Instance);

        foreach (var stmt in statements.OrderBy(StatementAsOf))
        {
            var stmtAccountId = Attr(stmt, "accountId");
            var isLatest = latestByAccount.TryGetValue(stmtAccountId, out var latest)
                           && ReferenceEquals(stmt, latest);

            foreach (var e in stmt.Descendants("SymbolSummary"))
            {
                var symbol = Attr(e, "symbol");
                if (string.IsNullOrWhiteSpace(symbol) || IsCurrencySymbol(symbol)) continue;
                var acctId = Acct(e, stmtAccountId);
                var key = (acctId, symbol);
                var fifo = Dec(e, "fifoPnlRealized");
                var mtm = Dec(e, "mtmPnl");

                if (fifoByKey.TryGetValue(key, out var prev))
                {
                    fifoByKey[key] = prev with { RealizedPnl = prev.RealizedPnl + fifo };
                }
                else
                {
                    fifoByKey[key] = new FlexRealizedPnlDto(
                        acctId, symbol, Attr(e, "assetCategory"),
                        Attr(e, "description"), Attr(e, "currency", "USD"),
                        fifo, 0m);
                }

                if (isLatest && openKeys.Contains(key))
                    unrealFromLatest[key] = mtm;
            }
        }

        foreach (var (key, row) in fifoByKey)
        {
            var unreal = unrealFromLatest.TryGetValue(key, out var u) ? u : 0m;
            // 已平仓且期间无已实现的行无展示价值
            if (!openKeys.Contains(key) && row.RealizedPnl == 0m) continue;
            realized.Add(row with { UnrealizedPnl = unreal });
        }

        // 成交 / 现金流 / 逐日净值 / MTM：需要全历史
        foreach (var stmt in statements)
        {
            var stmtAccountId = Attr(stmt, "accountId");

            foreach (var e in stmt.Descendants("Trade"))
            {
                trades.Add(new FlexTradeDto(
                    Acct(e, stmtAccountId), Attr(e, "symbol"), MapAssetType(Attr(e, "assetCategory")),
                    MapSide(Attr(e, "buySell"), Dec(e, "quantity")),
                    Attr(e, "currency", "USD"),
                    Math.Abs(Dec(e, "quantity")), Dec(e, "tradePrice"),
                    Dec(e, "ibCommission"), ParseFlexDateTime(Attr(e, "dateTime"))));
            }

            foreach (var e in stmt.Descendants("CashTransaction"))
            {
                cash.Add(new FlexCashTransactionDto(
                    Acct(e, stmtAccountId), MapCashFlowType(Attr(e, "type")),
                    Dec(e, "amount"), Attr(e, "currency", "USD"),
                    ParseFlexDateTime(Attr(e, "dateTime")), Attr(e, "note")));
            }

            foreach (var e in stmt.Descendants("EquitySummaryByReportDateInBase"))
            {
                var positionsMv = Dec(e, "stock") + Dec(e, "options") + Dec(e, "bonds")
                                  + Dec(e, "commodities") + Dec(e, "notes") + Dec(e, "funds");
                dailyNavs.Add(new FlexDailyNavDto(
                    Acct(e, stmtAccountId),
                    ParseFlexDateTime(Attr(e, "reportDate")),
                    positionsMv,
                    Dec(e, "cash"),
                    Dec(e, "total")));
            }

            foreach (var e in stmt.Descendants("MTMPerformanceSummaryUnderlying"))
            {
                var symbol = Attr(e, "symbol");
                var reportDate = Attr(e, "reportDate");
                if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(reportDate)) continue;
                if (IsCurrencySymbol(symbol) || MapAssetType(Attr(e, "assetCategory")) == AssetType.Cash) continue;
                mtmDaily.Add(new FlexMtmDailyDto(
                    Acct(e, stmtAccountId),
                    ParseFlexDateTime(reportDate),
                    symbol,
                    MapAssetType(Attr(e, "assetCategory")),
                    Dec(e, "multiplier"),
                    Dec(e, "closeQuantity"),
                    Dec(e, "closePrice"),
                    Dec(e, "total")));
            }
        }

        return new FlexSnapshot(accounts, positions, trades, cash, realized, dailyNavs, mtmDaily);
    }

    private sealed class OpenKeyComparer : IEqualityComparer<(string Acct, string Sym)>
    {
        public static readonly OpenKeyComparer Instance = new();
        public bool Equals((string Acct, string Sym) x, (string Acct, string Sym) y) =>
            string.Equals(x.Acct, y.Acct, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Sym, y.Sym, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Acct, string Sym) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Acct),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sym));
    }

    /// <summary>FlexStatement 的业务日：优先 toDate，其次 fromDate。</summary>
    private static DateTime StatementAsOf(XElement stmt)
    {
        var raw = Attr(stmt, "toDate");
        if (string.IsNullOrWhiteSpace(raw)) raw = Attr(stmt, "fromDate");
        return ParseFlexDateTime(raw);
    }

    private static void TryAddOpenPosition(
        XElement e, string stmtAccountId, List<FlexPositionDto> positions, DateTime asOfDate)
    {
        var symbol = Attr(e, "symbol");
        if (string.IsNullOrWhiteSpace(symbol) || IsCurrencySymbol(symbol)) return;

        // OCC：NOK   260702C00014500 → 到期 2026-07-02；过期合约 IB 端已无持仓，Flex 旧日结可能残留
        if (TryParseOccExpiry(symbol, out var expiry) && expiry.Date < asOfDate.Date)
            return;

        var qty = DecAny(e, "position", "quantity");
        if (qty == 0m) return; // 已平仓行偶发残留，不计入当前持仓

        var mult = DecAny(e, "multiplier");
        if (mult == 0m) mult = 1m;
        var markPremium = DecAny(e, "markPrice", "marketPrice", "closePrice");
        var posVal = DecAny(e, "positionValue", "value", "marketValue");
        var avgPremium = DecAny(e, "costBasisPrice", "avgPrice", "averageCost", "averagePrice");
        var costMoney = DecAny(e, "costBasisMoney", "costBasis");

        // 一律存每股权利金；市值 = qty × premium × multiplier
        decimal mark = markPremium;
        if (mark == 0m && qty != 0m && posVal != 0m)
            mark = posVal / (qty * mult);

        decimal avg = avgPremium;
        if (avg == 0m && qty != 0m && costMoney != 0m)
            avg = costMoney / (Math.Abs(qty) * mult);

        positions.Add(new FlexPositionDto(
            Acct(e, stmtAccountId), symbol, MapAssetType(Attr(e, "assetCategory")),
            Attr(e, "currency", "USD"),
            qty, avg, mark, mult));
    }

    private static string Attr(XElement e, string name, string fallback = "") =>
        e.Attribute(name)?.Value ?? fallback;

    private static string Acct(XElement e, string stmtAccountId)
    {
        var id = Attr(e, "accountId");
        return string.IsNullOrWhiteSpace(id) ? stmtAccountId : id;
    }

    private static decimal Dec(XElement e, string name)
    {
        var raw = e.Attribute(name)?.Value;
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    private static decimal DecAny(XElement e, params string[] names)
    {
        foreach (var name in names)
        {
            var v = Dec(e, name);
            if (v != 0m) return v;
        }
        return 0m;
    }

    private static readonly HashSet<string> CurrencySymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "HKD", "CNH", "CNY", "EUR", "GBP", "JPY", "AUD", "CAD", "CHF", "SGD", "KRW", "BASE"
    };

    private static bool IsCurrencySymbol(string symbol) =>
        CurrencySymbols.Contains(symbol.Trim());

    /// <summary>解析 OCC 期权代码中的到期日（YYMMDD）。非期权或无法解析时返回 false。</summary>
    private static bool TryParseOccExpiry(string symbol, out DateTime expiry)
    {
        expiry = default;
        var m = Regex.Match(symbol.Trim(), @"(\d{6})([CP])(\d{8})\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        if (!DateTime.TryParseExact(m.Groups[1].Value, "yyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out expiry))
            return false;
        expiry = DateTime.SpecifyKind(expiry.Date, DateTimeKind.Utc);
        return true;
    }

    private static AssetType MapAssetType(string raw) => raw.Trim().ToUpperInvariant() switch
    {
        "STK" => AssetType.Stock,
        "ETF" => AssetType.Etf,
        "OPT" or "WAR" => AssetType.Option,
        "FUT" => AssetType.Future,
        "CASH" => AssetType.Cash,
        "BOND" => AssetType.Bond,
        "FX" or "FOREX" => AssetType.Forex,
        _ => AssetType.Stock
    };

    private static Side MapSide(string? buySell, decimal quantity)
    {
        if (buySell is not null)
        {
            var s = buySell.Trim().ToUpperInvariant();
            if (s == "SELL") return Side.Sell;
            if (s == "BUY") return Side.Buy;
        }
        return quantity < 0 ? Side.Sell : Side.Buy;
    }

    private static DateTime ParseFlexDateTime(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DateTime.UtcNow;
        if (s.Length >= 15 && s[8] == ';'
            && int.TryParse(s.Substring(0, 4), out var y)
            && int.TryParse(s.Substring(4, 2), out var mo)
            && int.TryParse(s.Substring(6, 2), out var da)
            && int.TryParse(s.Substring(9, 2), out var hh)
            && int.TryParse(s.Substring(11, 2), out var mm)
            && int.TryParse(s.Substring(13, 2), out var ss))
        {
            return new DateTime(y, mo, da, hh, mm, ss, DateTimeKind.Utc);
        }
        if (s.Length >= 8
            && int.TryParse(s.Substring(0, 4), out var y2)
            && int.TryParse(s.Substring(4, 2), out var mo2)
            && int.TryParse(s.Substring(6, 2), out var da2))
        {
            return new DateTime(y2, mo2, da2, 0, 0, 0, DateTimeKind.Utc);
        }
        return DateTime.UtcNow;
    }

    private static CashFlowType MapCashFlowType(string? raw) =>
        (raw ?? "Other").Trim().ToLowerInvariant() switch
        {
            "deposit" or "deposits" or "client cash transfer" or "transfer" or "cash balance adjustment"
                => CashFlowType.Deposit,
            "withdrawal" or "withdrawals" => CashFlowType.Withdrawal,
            "dividend" or "dividends" or "payment in lieu" => CashFlowType.Dividend,
            "interest" or "interest accrued" => CashFlowType.Interest,
            "fee" or "fees" or "brokerage fees" or "commission" or "margin interest" => CashFlowType.Fee,
            _ => CashFlowType.Other
        };
}
