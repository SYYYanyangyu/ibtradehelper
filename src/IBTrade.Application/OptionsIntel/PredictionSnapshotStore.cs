using System.Collections.Concurrent;
using System.Text.Json;
using IBTrade.Application.OptionsIntel.Dtos;

namespace IBTrade.Application.OptionsIntel;

/// <summary>记录预测市场价格，计算涨跌。不编造盘口，只对比两次真实拉取。</summary>
public class PredictionSnapshotStore
{
    private readonly ConcurrentDictionary<string, decimal> _last = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    public PredictionSnapshotStore()
    {
        var dir = Environment.GetEnvironmentVariable("IBTRADE_DATA_DIR");
        var baseDir = string.IsNullOrWhiteSpace(dir) ? AppContext.BaseDirectory : dir!;
        _path = Path.Combine(baseDir, "prediction-snapshots.json");
        Load();
    }

    public IReadOnlyList<PredictionMarketDto> Apply(IEnumerable<PredictionMarketDto> markets)
    {
        var list = new List<PredictionMarketDto>();
        foreach (var m in markets)
        {
            if (m.Probability is not { } p)
            {
                list.Add(m);
                continue;
            }
            decimal? prev = null;
            decimal? chg = null;
            if (_last.TryGetValue(Key(m), out var old))
            {
                prev = old;
                chg = p - old;
            }
            _last[Key(m)] = p;
            list.Add(m with { PreviousProbability = prev, Change = chg });
        }
        Save();
        return list;
    }

    private static string Key(PredictionMarketDto m) => $"{m.Venue}|{m.Question}";

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var map = JsonSerializer.Deserialize<Dictionary<string, decimal>>(File.ReadAllText(_path));
            if (map is null) return;
            foreach (var kv in map) _last[kv.Key] = kv.Value;
        }
        catch { /* 损坏则从空开始 */ }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_last, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch { /* 忽略磁盘失败 */ }
    }
}
