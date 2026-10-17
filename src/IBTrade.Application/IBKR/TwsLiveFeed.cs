using Microsoft.Extensions.Logging;

namespace IBTrade.Application.IBKR;

/// <summary>
/// 实时行情推送宿主（单例）：维持一条到 TWS / IB Gateway 的持久连接并订阅持仓实时行情，
/// 价格一动即更新内存快照，供前端高频轮询读取。连接意外断开时自动重连。
/// 与 <see cref="TwsSyncService"/>（按需拉一次入库）互补：前者喂看板的“实时跳动”，后者落库做历史。
/// </summary>
public class TwsLiveFeed(
    TwsOptions options,
    ITwsClientFactory clientFactory,
    ILogger<TwsLiveFeed>? logger = null) : IDisposable
{
    private readonly object _gate = new();
    private ITwsClient? _client;
    private CancellationTokenSource? _monitorCts;
    private bool _running;

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public string? LastError { get; private set; }

    /// <summary>启动实时推送：依次尝试候选端口，连上即用，并启动自动重连监控。</summary>
    public async Task<TwsSnapshot> StartAsync(CancellationToken ct = default)
    {
        Exception? lastError = null;
        foreach (var port in options.PortsFor(options.Mode))
        {
            try
            {
                logger?.LogInformation("实时推送：尝试连接端口 {Port}…", port);
                var client = clientFactory.Create(options.Host, port, options.ClientId);
                var snap = await client.StartLiveAsync(ct);
                lock (_gate)
                {
                    _client?.Dispose();
                    _client = client;
                    _running = true;
                    LastError = null;
                }
                StartMonitor();
                logger?.LogInformation("实时推送已通过端口 {Port}（{Source}）启动", port, snap.Source);
                return snap;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger?.LogWarning(ex, "实时推送：端口 {Port} 连接失败，尝试下一个候选端口", port);
            }
        }

        LastError = lastError?.Message;
        throw new InvalidOperationException(
            $"实时推送无法连接到 {options.Mode} 端口（{string.Join(", ", options.PortsFor(options.Mode))}）。Paper 只连模拟口。",
            lastError);
    }

    /// <summary>读取当前实时快照；未启动/已断开返回 null。</summary>
    public TwsLiveSnapshot? GetSnapshot()
    {
        lock (_gate)
        {
            return _client?.GetLiveSnapshot();
        }
    }

    /// <summary>停止实时推送并断开。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _monitorCts?.Cancel();
            _monitorCts?.Dispose();
            _monitorCts = null;
            _client?.StopLive();
            _client?.Dispose();
            _client = null;
        }
        logger?.LogInformation("实时推送已停止");
    }

    /// <summary>后台监控：连接丢失则自动重连（保持 clientId 不变，TWS 会复用同一会话）。</summary>
    private void StartMonitor()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                bool lost;
                lock (_gate) lost = _client?.IsConnectionLost() ?? false;
                if (!lost) continue;

                logger?.LogWarning("实时推送连接已断开，尝试自动重连…");
                try
                {
                    var snap = await ReconnectAsync(token);
                    logger?.LogInformation("实时推送已自动重连（端口 {Port}）", snap.ConnectedPort);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "实时推送自动重连失败，将在 5 秒后重试");
                }
            }
        }, token);
    }

    /// <summary>用新客户端重建持久连接（旧连接已不可用时调用）。</summary>
    private async Task<TwsSnapshot> ReconnectAsync(CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var port in options.PortsFor(options.Mode))
        {
            try
            {
                var client = clientFactory.Create(options.Host, port, options.ClientId);
                var snap = await client.StartLiveAsync(ct);
                lock (_gate)
                {
                    _client?.Dispose();
                    _client = client;
                    LastError = null;
                }
                return snap;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        throw new InvalidOperationException(
            $"自动重连失败（{string.Join(", ", options.PortsFor(options.Mode))}）。", lastError);
    }

    public void Dispose() => Stop();
}
