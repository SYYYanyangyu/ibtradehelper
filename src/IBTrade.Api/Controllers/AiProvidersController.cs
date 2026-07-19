using IBTrade.Application.Ai;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>LLM 厂商/模型目录：查看占位配置、切换厂商、拉取模型列表。</summary>
[ApiController]
[Route("api/ai/providers")]
public class AiProvidersController(ILlmProviderCatalog catalog) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(catalog.GetCatalog());

    [HttpPost("{providerId}/refresh-models")]
    public async Task<IActionResult> Refresh(string providerId, CancellationToken ct)
    {
        try
        {
            var (provider, warning) = await catalog.RefreshModelsAsync(providerId, ct);
            return Ok(new { provider, warning, catalog = catalog.GetCatalog() });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    public record SelectBody(string? ProviderId, string? Model);

    /// <summary>切换当前厂商和/或模型（进程内生效，不写回密钥文件）。</summary>
    [HttpPost("select")]
    public IActionResult Select([FromBody] SelectBody body)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(body.ProviderId))
                catalog.SetActiveProvider(body.ProviderId!);
            if (!string.IsNullOrWhiteSpace(body.Model))
            {
                var active = catalog.GetCatalog().ActiveProvider;
                var pid = string.IsNullOrWhiteSpace(body.ProviderId) ? active : body.ProviderId!;
                catalog.SetModel(pid, body.Model!);
            }
            return Ok(catalog.GetCatalog());
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
