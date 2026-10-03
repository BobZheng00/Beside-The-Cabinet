using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;

namespace InsertCoin.Mind;

/// <summary>The protocol a provider speaks. Most third-party and local servers speak OpenAI's.</summary>
public enum Protocol { OpenAi, Anthropic }

/// <summary>
/// Where her replies come from: the user's own provider, server address and model. The key is kept apart, encrypted
/// (see <see cref="KeyStore"/>). Nothing here depends on one provider or model.
/// </summary>
public sealed record Provider(Protocol Protocol, string BaseUrl, string Model)
{
    /// <summary>Starting points for the settings form. "Other" takes any OpenAI-compatible server.</summary>
    public static readonly Preset[] Presets =
    [
        new("OpenAI", Protocol.OpenAi, "https://api.openai.com/v1", "", NeedsKey: true),
        new("Anthropic", Protocol.Anthropic, "https://api.anthropic.com", "claude-haiku-4-5", NeedsKey: true),
        new("OpenRouter", Protocol.OpenAi, "https://openrouter.ai/api/v1", "", NeedsKey: true),
        new("DeepSeek", Protocol.OpenAi, "https://api.deepseek.com/v1", "deepseek-chat", NeedsKey: true),
        new("Qwen (DashScope)", Protocol.OpenAi, "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", NeedsKey: true),
        new("Ollama (this PC)", Protocol.OpenAi, "http://localhost:11434/v1", "", NeedsKey: false),
        new("LM Studio (this PC)", Protocol.OpenAi, "http://localhost:1234/v1", "", NeedsKey: false),
        new("Other (OpenAI-compatible)", Protocol.OpenAi, "", "", NeedsKey: true),
    ];

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>A provider only counts once it says where to go and which model to ask.</summary>
    public static Provider? From(string? protocol, string? baseUrl, string? model) =>
        string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out _)
            ? null
            : new Provider(Enum.TryParse<Protocol>(protocol, ignoreCase: true, out var p) ? p : Protocol.OpenAi, baseUrl.Trim(), model.Trim());

    // One retry: someone is looking at thinking dots, so a broken setup should say so in seconds, not after a minute of backoff.
    public IChatClient Connect(string? key) => Protocol switch
    {
        Protocol.Anthropic => new AnthropicClient { ApiKey = key, BaseUrl = BaseUrl.TrimEnd('/'), Timeout = Timeout, MaxRetries = 1 }.AsIChatClient(Model),
        // local servers want no key, but the client insists on one
        _ => new OpenAIClient(new ApiKeyCredential(string.IsNullOrEmpty(key) ? "none" : key),
                              new OpenAIClientOptions { Endpoint = new Uri(BaseUrl), NetworkTimeout = Timeout, RetryPolicy = new ClientRetryPolicy(maxRetries: 1) })
            .GetChatClient(Model).AsIChatClient(),
    };
}

public sealed record Preset(string Name, Protocol Protocol, string BaseUrl, string ModelHint, bool NeedsKey);
