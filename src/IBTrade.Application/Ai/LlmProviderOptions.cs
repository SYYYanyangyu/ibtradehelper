namespace IBTrade.Application.Ai;

/// <summary>多厂商 LLM 配置（Key 占位，部署时自行填写）。</summary>
public class LlmOptions
{
    public const string SectionName = "IBTRADE_AI";

    /// <summary>当前启用的厂商：openai / deepseek / qwen / claude</summary>
    public string ActiveProvider { get; set; } = "openai";

    public Dictionary<string, LlmProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class LlmProviderOptions
{
    public string DisplayName { get; set; } = "";

    /// <summary>API Key（占位为空；也可用环境变量覆盖）。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>OpenAI 兼容根路径，或 Anthropic 根路径。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>openai_compatible | anthropic</summary>
    public string ApiStyle { get; set; } = "openai_compatible";

    /// <summary>当前选用模型。</summary>
    public string Model { get; set; } = "";

    /// <summary>已知/缓存的模型列表；Refresh 成功会覆盖。</summary>
    public List<string> Models { get; set; } = new();

    /// <summary>环境变量名（优先于配置文件中的 ApiKey）。</summary>
    public string? ApiKeyEnv { get; set; }
}
