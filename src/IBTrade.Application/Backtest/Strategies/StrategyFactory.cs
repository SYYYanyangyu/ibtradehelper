namespace IBTrade.Application.Backtest.Strategies;

/// <summary>策略工厂：按 key 取策略。新增策略只需在 DI 注册 IStrategy 实现即可自动纳入。</summary>
public record StrategyInfo(string Key, string Name, IReadOnlyList<StrategyParam> Parameters);

public interface IStrategyFactory
{
    IStrategy Get(string key);
    IReadOnlyList<StrategyInfo> List();
}

public class StrategyFactory(IEnumerable<IStrategy> strategies) : IStrategyFactory
{
    private readonly Dictionary<string, IStrategy> _map =
        strategies.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

    public IStrategy Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) key = "sma_cross";
        if (_map.TryGetValue(key, out var s)) return s;
        throw new ArgumentException($"未知策略 '{key}'。可用：{string.Join(", ", _map.Keys)}");
    }

    public IReadOnlyList<StrategyInfo> List()
        => _map.Values.Select(s => new StrategyInfo(s.Key, s.Name, s.Parameters)).ToList();
}
