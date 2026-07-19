using IBTrade.Application.Ai;
using IBTrade.Application.AutoTrade;
using IBTrade.Application.Backtest;
using IBTrade.Application.Backtest.Strategies;
using IBTrade.Application.IBKR;
using IBTrade.Application.Ingestion;
using IBTrade.Application.MarketData;
using IBTrade.Application.Portfolio;
using IBTrade.Application.Positions;
using Microsoft.Extensions.DependencyInjection;

namespace IBTrade.Application;

/// <summary>
/// 应用层 DI 组装：每层暴露自己的注册扩展方法，Api 只需调用即可。
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IPositionService, PositionService>();
        services.AddScoped<IPortfolioService, PortfolioService>();
        services.AddScoped<IRealizedPnlService, RealizedPnlService>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<ISnapshotService, SnapshotService>();
        services.AddScoped<IDailyPnlService, DailyPnlService>();
        services.AddScoped<IPositionDailyPnlService, PositionDailyPnlService>();
        services.AddScoped<ITwsSyncService, TwsSyncService>();
        // 历史行情服务：短连接拉数，无状态，注册为单例（供回测与自动交易引擎共用）
        services.AddSingleton<IHistoricalDataService, HistoricalDataService>();
        // 回测模块（与看板解耦）：策略以 IStrategy 注册，工厂自动汇总。
        // 策略为无状态计算，注册为单例，供自动交易引擎（同样单例）直接消费。
        services.AddSingleton<IStrategy, SmaCrossStrategy>();
        services.AddSingleton<IStrategy, RsiStrategy>();
        services.AddSingleton<IStrategy, DonchianBreakoutStrategy>();
        services.AddSingleton<IStrategyFactory, StrategyFactory>();
        services.AddScoped<IBacktestService, BacktestService>();
        // AI 策略闭环：启发式顾问（可被 Infrastructure 的 LLM 顾问包装）；管线 Scoped（依赖回测）
        services.AddSingleton<HeuristicAiStrategyAdvisor>();
        services.AddScoped<IAiStrategyPipelineService, AiStrategyPipelineService>();
        // 实时行情推送宿主：单例，维持一条持久连接并自动重连
        services.AddSingleton<TwsLiveFeed>();
        // 自动交易引擎（单例后台循环；依赖下单服务与策略工厂，二者均为单例）
        services.AddSingleton<IAutoTrader, AutoTrader>();
        return services;
    }
}
