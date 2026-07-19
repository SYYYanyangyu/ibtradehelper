using IBTrade.Application.Ai.Dtos;
using IBTrade.Application.AutoTrade;
using IBTrade.Application.Backtest.Strategies;
using IBTrade.Application.Orders;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.Ai;

/// <summary>
/// 部署前检查：消毒入参 + 环境/回测质量门禁。
/// block 阻止部署；warn 需用户确认；pass/info 仅提示。
/// </summary>
public static class DeployPreflightEvaluator
{
    public static DeployPreflightResult Evaluate(
        DeployPreflightRequest request,
        IStrategyFactory strategies,
        IAutoTrader autoTrader,
        IRiskGuard riskGuard)
    {
        var checks = new List<DeployCheckItem>();
        string symbol = "";
        string duration = "";
        string barSize = "";
        decimal qty = 0;
        IStrategy? strategy = null;
        Dictionary<string, double> parameters = new();

        // ---- 硬校验（block）----
        try
        {
            symbol = StrategyParameterSanitizer.SanitizeSymbol(request.Symbol);
            checks.Add(Pass("symbol", "标的代码合法", symbol));
        }
        catch (ArgumentException ex)
        {
            checks.Add(Block("symbol", "标的代码非法", ex.Message));
        }

        try
        {
            duration = StrategyParameterSanitizer.SanitizeDuration(request.Duration);
            barSize = StrategyParameterSanitizer.SanitizeBarSize(request.BarSize);
            checks.Add(Pass("horizon", "回看区间 / K 线合法", $"{duration} · {barSize}"));
        }
        catch (ArgumentException ex)
        {
            checks.Add(Block("horizon", "区间或 K 线非法", ex.Message));
        }

        try
        {
            qty = StrategyParameterSanitizer.SanitizeQuantity(request.Quantity <= 0 ? 100m : request.Quantity);
            checks.Add(Pass("quantity", "演练数量合法", $"{qty:F0} 股/笔"));
        }
        catch (ArgumentException ex)
        {
            checks.Add(Block("quantity", "数量非法", ex.Message));
        }

        try
        {
            (strategy, parameters) = StrategyParameterSanitizer.SanitizeStrategy(
                strategies, request.StrategyKey, request.Parameters);
            checks.Add(Pass("strategy", "策略在白名单内", $"{strategy.Name} ({strategy.Key})"));

            // 参数是否被钳制
            if (request.Parameters is not null)
            {
                var clamped = false;
                foreach (var kv in parameters)
                {
                    if (request.Parameters.TryGetValue(kv.Key, out var raw)
                        && Math.Abs(raw - kv.Value) > 1e-9)
                    {
                        clamped = true;
                        break;
                    }
                }
                if (clamped)
                    checks.Add(Warn("params_clamped", "参数已按白名单钳制",
                        "部分参数超出 Min/Max 或快慢线不一致，已自动修正。部署将使用钳制后参数。"));
                else
                    checks.Add(Pass("params", "参数在允许范围内", FormatParams(parameters)));
            }
            else
            {
                checks.Add(Info("params", "使用策略默认参数", FormatParams(parameters)));
            }
        }
        catch (ArgumentException ex)
        {
            checks.Add(Block("strategy", "策略不可用", ex.Message));
        }

        // ---- 安全承诺 ----
        checks.Add(Pass("dry_run", "强制演练 DryRun", "本入口永远 DryRun=true，不会从部署动作发真单。"));

        var risk = riskGuard.Settings;
        if (risk.Mode == TradingMode.Live)
        {
            checks.Add(Warn("risk_live", "当前风控为 Live",
                risk.LiveAcknowledged
                    ? "风控已切到实盘且已确认。部署仍强制 DryRun；关闭 DryRun 前请三思。"
                    : "风控为 Live 但未确认实盘风险。部署仍强制 DryRun；真实下单会被风控拦截。"));
        }
        else
        {
            checks.Add(Pass("risk_paper", "当前风控为 Paper", "模拟盘模式，与演练部署匹配。"));
        }

        // ---- 引擎环境（info/warn，不 block）----
        var settings = autoTrader.Settings;
        if (!autoTrader.IsRunning)
            checks.Add(Info("engine_stopped", "引擎未启动", "部署后请到「量化」页启动引擎；否则不会评估信号。"));
        else if (!settings.Enabled)
            checks.Add(Warn("master_off", "引擎在跑但总开关关闭", "启动后仍会空转。部署后请打开量化页总开关。"));
        else
            checks.Add(Pass("engine", "引擎已运行且总开关开启",
                $"轮询 {settings.PollIntervalSeconds}s · 已收盘K线={(settings.UseCompletedBarsOnly ? "开" : "关")}"));

        if (settings.UseCompletedBarsOnly)
            checks.Add(Info("bar_close", "量化侧仅用已收盘 K 线", "与回测「次日开盘」一致方向：避免未收盘假信号。"));

        // ---- 回测质量（有摘要才查）----
        if (request.BarCount is int bars)
        {
            if (bars < 30)
                checks.Add(Warn("bars_few", "回测样本偏少", $"仅 {bars} 根 K 线，统计意义有限。"));
            else
                checks.Add(Pass("bars", "回测样本充足", $"{bars} 根 K 线"));
        }
        else
        {
            checks.Add(Warn("no_backtest", "未附带回测摘要", "建议先跑回测再部署，便于质量门禁。"));
        }

        if (request.TradeCount is int trades)
        {
            if (trades == 0)
                checks.Add(Warn("no_trades", "回测区间无成交", "策略可能长期无信号；演练部署后也可能长期沉默。"));
            else if (trades < 3)
                checks.Add(Warn("trades_few", "成交笔数很少", $"仅 {trades} 笔，过拟合/偶然性风险更高。"));
            else
                checks.Add(Pass("trades", "有足够成交样本", $"{trades} 笔"));
        }

        if (request.MaxDrawdownPct is decimal dd && dd >= 25m)
            checks.Add(Warn("drawdown", "最大回撤偏高", $"回撤 {dd:F1}% ≥ 25%，偏稳健场景请再确认。"));

        if (request.Sharpe is decimal sharpe && sharpe < 0)
            checks.Add(Warn("sharpe", "夏普为负", $"Sharpe={sharpe:F2}，风险调整后收益不佳。"));

        if (request.TotalReturnPct is decimal ret && request.BuyHoldReturnPct is decimal bh
            && ret + 5m < bh)
        {
            checks.Add(Warn("underperform", "策略显著弱于买入持有",
                $"策略 {ret:F1}% vs 买入持有 {bh:F1}%。仍可演练，但请降低预期。"));
        }

        if (!string.IsNullOrWhiteSpace(request.FillMode)
            && request.FillMode.Equals("close", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(Warn("fill_close", "回测用了当日收盘成交",
                "存在同 bar 前瞻偏差。建议改用「次日开盘」后再评估。"));
        }
        else if (!string.IsNullOrWhiteSpace(request.FillMode))
        {
            var slip = request.SlippageBps ?? 5m;
            checks.Add(Info("fill_model", "回测撮合：次日开盘", $"滑点 {slip} bps（单边）。"));
        }

        if (request.CostDragPct is decimal drag)
        {
            if (drag >= 5m)
                checks.Add(Warn("cost_drag", "交易成本拖累偏高",
                    $"成本约占初始资金 {drag:F2}%（合计 {request.TotalCost ?? 0m:F2}）。高频/小资金时影响更大。"));
            else if (request.TotalCost is decimal tc && tc > 0)
                checks.Add(Pass("cost", "交易成本已计入回测",
                    $"合计约 {tc:F2}，拖累 {drag:F2}% 初始资金。"));
        }

        var blocked = checks.Any(c => c.Level == "block");
        var warned = checks.Any(c => c.Level == "warn");

        return new DeployPreflightResult
        {
            CanDeploy = !blocked,
            HasWarnings = warned,
            Summary = blocked
                ? "存在阻断项，无法部署。请先修正。"
                : warned
                    ? "可以部署，但有警告项，请确认后再继续（强制演练）。"
                    : "检查通过，可部署到量化（强制演练 DryRun）。",
            Checks = checks,
            Symbol = symbol,
            StrategyKey = strategy?.Key ?? (request.StrategyKey ?? ""),
            StrategyName = strategy?.Name ?? "",
            Parameters = parameters,
            Duration = duration,
            BarSize = barSize,
            Quantity = qty,
            RiskMode = risk.Mode.ToString(),
            LiveAcknowledged = risk.LiveAcknowledged,
            EngineRunning = autoTrader.IsRunning,
            MasterEnabled = settings.Enabled,
            CompletedBarsOnly = settings.UseCompletedBarsOnly
        };
    }

    private static string FormatParams(Dictionary<string, double> p)
        => p.Count == 0 ? "（无）" : string.Join(", ", p.Select(kv => $"{kv.Key}={kv.Value}"));

    private static DeployCheckItem Pass(string id, string title, string detail)
        => new() { Id = id, Level = "pass", Title = title, Detail = detail };

    private static DeployCheckItem Warn(string id, string title, string detail)
        => new() { Id = id, Level = "warn", Title = title, Detail = detail };

    private static DeployCheckItem Block(string id, string title, string detail)
        => new() { Id = id, Level = "block", Title = title, Detail = detail };

    private static DeployCheckItem Info(string id, string title, string detail)
        => new() { Id = id, Level = "info", Title = title, Detail = detail };
}
