using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.Backtest.Strategies;

/// <summary>每根 K 线的交易信号。</summary>
public enum SignalType
{
    Hold = 0,
    Buy = 1,
    Sell = 2
}

/// <summary>
/// 交易策略抽象。策略只负责“看数据、出信号”，不关心资金/撮合（由回测引擎负责）。
/// 新增策略只需实现本接口并在 StrategyFactory 注册，与看板、引擎均解耦。
/// </summary>
public interface IStrategy
{
    /// <summary>策略标识（与请求 strategy 字段对应）。</summary>
    string Key { get; }

    /// <summary>可读名称。</summary>
    string Name { get; }

    /// <summary>参数元数据（用于前端按当前策略动态渲染参数输入框）。</summary>
    IReadOnlyList<StrategyParam> Parameters { get; }

    /// <summary>
    /// 基于完整 K 线序列生成信号数组（与 candles 等长、逐根对齐）。
    /// parameters 为策略参数（可空，取默认值）。
    /// </summary>
    IReadOnlyList<SignalType> GenerateSignals(
        IReadOnlyList<CandleDto> candles,
        IReadOnlyDictionary<string, double>? parameters);
}
