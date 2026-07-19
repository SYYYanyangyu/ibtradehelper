using IBTrade.Application.Ai.Dtos;

namespace IBTrade.Application.Ai;

/// <summary>
/// AI 策略顾问：把自然语言映射到<strong>已注册白名单策略 + 合法参数</strong>。
/// 禁止生成/执行任意代码；有 LLM 密钥时仅用于选策略与填参，仍经消毒层。
/// </summary>
public interface IAiStrategyAdvisor
{
    Task<AiSuggestResult> SuggestAsync(AiSuggestRequest request, CancellationToken ct = default);
}
