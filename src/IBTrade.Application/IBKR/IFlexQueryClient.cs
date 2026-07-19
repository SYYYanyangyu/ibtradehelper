using IBTrade.Application.Ingestion.Dtos;

namespace IBTrade.Application.IBKR;

/// <summary>
/// Flex 客户端接口（定义于 Application 层，实现于 Infrastructure 层）。
/// 这样 Application 的入库用例只依赖接口，不依赖具体的 HTTP/XML 实现，
/// 保持分层单向依赖（Infrastructure → Application）。
/// </summary>
public interface IFlexQueryClient
{
    Task<string> DownloadXmlAsync(string token, string queryId, string? baseUrl = null, CancellationToken ct = default);
    FlexSnapshot Parse(string xml);
}
