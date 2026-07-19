using IBTrade.Domain.Enums;

namespace IBTrade.Application.Ingestion;

/// <summary>入库用例：拉取 Flex 报表并写入数据库。</summary>
public interface IIngestionService
{
    /// <summary>按指定环境摄入一个 Flex Query（queryId 可空，空时使用该环境的默认 queryId 环境变量）。</summary>
    Task IngestAsync(TradeEnv env, string? queryId = null, CancellationToken ct = default);

    /// <summary>依次摄入所有已配置默认 queryId 的环境（Live / Paper）。</summary>
    Task IngestAllAsync(CancellationToken ct = default);
}
