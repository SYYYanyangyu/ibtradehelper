using IBTrade.Application.Orders;

namespace IBTrade.Application.AutoTrade;

/// <summary>
/// 自动交易引擎抽象（应用层）：订阅标的 → 周期拉历史 K 线 → 跑策略 → 持仓去抖 → 过风控下单/演练。
/// 实现于同层（AutoTrader），仅依赖接口，不触碰具体 socket。
/// </summary>
public interface IAutoTrader
{
    bool IsRunning { get; }

    /// <summary>风控（与手动下单共用同一套，实盘硬拦截）。</summary>
    IRiskGuard Risk { get; }

    AutoTradeSettings Settings { get; }

    IReadOnlyList<AutoTradeSymbolConfig> GetConfigs();

    void UpsertConfig(AutoTradeSymbolConfig cfg, string? operatorName = null, string? ip = null);

    void RemoveConfig(string symbol, string? operatorName = null, string? ip = null);

    void UpdateSettings(AutoTradeSettings s, string? operatorName = null, string? ip = null);

    /// <summary>启动后台轮询循环（总开关关闭时循环空转）。</summary>
    void Start(string? operatorName = null, string? ip = null);

    /// <summary>停止后台循环。</summary>
    void Stop(string? operatorName = null, string? ip = null);

    /// <summary>立即对单个标的评估一次（便于验证；是否真实下单取决于该标的 DryRun 设置）。</summary>
    Task EvaluateNowAsync(string symbol, CancellationToken ct = default, string? operatorName = null, string? ip = null);

    /// <summary>手动操作审计入口（如下单/撤单），写入与自动交易同源的审计事件流。</summary>
    void RecordAudit(AutoTradeEventType type, string message, string? ip = null, string? op = null);

    IReadOnlyList<AutoTradeEvent> GetEvents(int max = 100);

    IReadOnlyList<AutoTradeSymbolRuntime> GetRuntime();
}
