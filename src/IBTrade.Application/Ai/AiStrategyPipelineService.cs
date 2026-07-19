using IBTrade.Application.Ai.Dtos;
using IBTrade.Application.AutoTrade;
using IBTrade.Application.Backtest;
using IBTrade.Application.Backtest.Dtos;
using IBTrade.Application.Backtest.Strategies;
using IBTrade.Application.Orders;
using IBTrade.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IBTrade.Application.Ai;

public interface IAiStrategyPipelineService
{
    Task<AiSuggestResult> SuggestAsync(AiSuggestRequest request, CancellationToken ct = default);
    Task<AiSuggestAndRunResult> SuggestAndRunAsync(AiSuggestAndRunRequest request, CancellationToken ct = default);
    DeployPreflightResult PreflightDeploy(DeployPreflightRequest request);
    DeployDryRunResult DeployDryRun(DeployDryRunRequest request, string? operatorName = null, string? ip = null);
}

/// <summary>
/// AI 策略闭环：建议 → 回测 → 强制演练部署。
/// 安全：不执行 LLM 代码；部署永远 DryRun=true；参数经白名单消毒；部署前检查清单门禁。
/// </summary>
public class AiStrategyPipelineService(
    IAiStrategyAdvisor advisor,
    IBacktestService backtest,
    IStrategyFactory strategies,
    IAutoTrader autoTrader,
    IRiskGuard riskGuard,
    ILogger<AiStrategyPipelineService>? logger = null) : IAiStrategyPipelineService
{
    public Task<AiSuggestResult> SuggestAsync(AiSuggestRequest request, CancellationToken ct = default)
        => advisor.SuggestAsync(request, ct);

    public async Task<AiSuggestAndRunResult> SuggestAndRunAsync(AiSuggestAndRunRequest request, CancellationToken ct = default)
    {
        var suggestion = await advisor.SuggestAsync(request, ct);
        var (strategy, parameters) = StrategyParameterSanitizer.SanitizeStrategy(
            strategies, suggestion.StrategyKey, suggestion.Parameters);

        var fillMode = string.Equals(request.FillMode, "close", StringComparison.OrdinalIgnoreCase)
            ? "close" : "next_open";
        var slippage = Math.Clamp(request.SlippageBps, 0m, 500m);

        var btReq = new BacktestRequest
        {
            Symbol = suggestion.Symbol,
            Strategy = strategy.Key,
            Parameters = parameters,
            Duration = suggestion.Duration,
            BarSize = suggestion.BarSize,
            InitialCapital = request.InitialCapital > 0 ? request.InitialCapital : 100_000m,
            CommissionPerTrade = request.CommissionPerTrade >= 0 ? request.CommissionPerTrade : 1m,
            FillMode = fillMode,
            SlippageBps = slippage
        };

        logger?.LogInformation("AI 回测：{Symbol} {Strategy} source={Source} fill={Fill}",
            btReq.Symbol, btReq.Strategy, suggestion.Source, fillMode);

        var result = await backtest.RunAsync(btReq, ct);
        suggestion.Parameters = parameters;
        suggestion.StrategyKey = strategy.Key;
        suggestion.StrategyName = strategy.Name;

        return new AiSuggestAndRunResult { Suggestion = suggestion, Backtest = result };
    }

    public DeployPreflightResult PreflightDeploy(DeployPreflightRequest request)
        => DeployPreflightEvaluator.Evaluate(request, strategies, autoTrader, riskGuard);

    public DeployDryRunResult DeployDryRun(DeployDryRunRequest request, string? operatorName = null, string? ip = null)
    {
        // 部署前再次跑检查清单（防绕过 UI）
        var preflight = DeployPreflightEvaluator.Evaluate(ToPreflight(request), strategies, autoTrader, riskGuard);
        if (!preflight.CanDeploy)
            throw new InvalidOperationException(
                "部署被检查清单阻断：" + string.Join("；",
                    preflight.Checks.Where(c => c.Level == "block").Select(c => c.Title)));

        if (preflight.HasWarnings && !request.AcknowledgeWarnings)
            throw new InvalidOperationException(
                "存在警告项，请先确认检查清单后再部署（AcknowledgeWarnings=true）。"
                + string.Join("；", preflight.Checks.Where(c => c.Level == "warn").Select(c => c.Title)));

        var symbol = preflight.Symbol;
        var duration = preflight.Duration;
        var barSize = preflight.BarSize;
        var qty = preflight.Quantity;
        var strategyKey = preflight.StrategyKey;
        var parameters = preflight.Parameters;
        var riskMode = preflight.RiskMode;

        var cfg = new AutoTradeSymbolConfig
        {
            Symbol = symbol,
            StrategyKey = strategyKey,
            Parameters = parameters,
            Duration = duration,
            BarSize = barSize,
            Quantity = qty,
            OrderType = OrderType.Market,
            Enabled = true,
            DryRun = true
        };

        autoTrader.UpsertConfig(cfg, operatorName, ip);

        if (request.StartEngine)
        {
            if (!autoTrader.IsRunning)
                autoTrader.Start(operatorName, ip);
        }

        logger?.LogInformation(
            "已部署演练配置：{Symbol} {Strategy} qty={Qty} riskMode={Mode} engineRunning={Running} warningsAck={Ack}",
            symbol, strategyKey, qty, riskMode, autoTrader.IsRunning, request.AcknowledgeWarnings);

        var risk = riskGuard.Settings;
        var envHint = risk.Mode == TradingMode.Live
            ? $"当前风控为 Live{(risk.LiveAcknowledged ? "（已确认）" : "（未确认实盘风险）")}，但本部署仍强制 DryRun，不会发真单。"
            : "当前风控为 Paper（模拟盘）。本部署强制 DryRun，只记信号不发单。";

        var warnHint = preflight.HasWarnings ? "（已确认警告项）" : "";

        return new DeployDryRunResult
        {
            Symbol = symbol,
            StrategyKey = strategyKey,
            Parameters = parameters,
            DryRun = true,
            EngineRunning = autoTrader.IsRunning,
            RiskMode = riskMode,
            LiveAcknowledged = risk.LiveAcknowledged,
            Message = request.StartEngine
                ? $"已部署到量化（强制演练 DryRun）{warnHint}。引擎已启动。{envHint}请到「量化」页确认总开关 Enabled。"
                : $"已部署到量化（强制演练 DryRun）{warnHint}。{envHint}请到「量化」页启动引擎并打开总开关。"
        };
    }

    private static DeployPreflightRequest ToPreflight(DeployDryRunRequest r) => new()
    {
        Symbol = r.Symbol,
        StrategyKey = r.StrategyKey,
        Parameters = r.Parameters,
        Duration = r.Duration,
        BarSize = r.BarSize,
        Quantity = r.Quantity,
        FillMode = r.FillMode,
        SlippageBps = r.SlippageBps,
        TotalReturnPct = r.TotalReturnPct,
        BuyHoldReturnPct = r.BuyHoldReturnPct,
        MaxDrawdownPct = r.MaxDrawdownPct,
        Sharpe = r.Sharpe,
        TradeCount = r.TradeCount,
        BarCount = r.BarCount,
        CostDragPct = r.CostDragPct,
        TotalCost = r.TotalCost
    };
}
