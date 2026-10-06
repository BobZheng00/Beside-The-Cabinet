using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Anthropic.Models.Messages;
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

    /// <summary>
    /// The tool a streamed update shows the model beginning to call, before its arguments have all come (the client
    /// only hands the call over once they have): OpenAI's first piece of a tool call, Anthropic's start of a tool-use
    /// block. Null for anything else, and for a client whose updates aren't known here.
    /// </summary>
    public static string? ToolStarting(ChatResponseUpdate update) => update.RawRepresentation switch
    {
        OpenAI.Chat.StreamingChatCompletionUpdate piece => piece.ToolCallUpdates.FirstOrDefault(t => !string.IsNullOrEmpty(t.FunctionName))?.FunctionName,
        RawMessageStreamEvent @event when @event.TryPickContentBlockStart(out var start) && start.ContentBlock.TryPickToolUse(out var use) => use.Name,
        _ => null,
    };

    /// <summary>A server on this PC (Ollama, LM Studio…): local models usually have small context windows.</summary>
    public bool IsLocal => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) && (url.IsLoopback || url.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase));

    /// <summary>How much of the conversation to keep word for word when the player hasn't chosen (see <see cref="Conversation.Window"/>).</summary>
    public int DefaultWindow => IsLocal ? Prompt.LocalWindow : Prompt.DefaultWindow;

    /// <summary>
    /// The client for her replies. Anthropic caches only what it is asked to: automatic caching is turned on (its mark
    /// follows the conversation as it grows; a read costs a tenth of fresh input). The others cache a stable prompt
    /// start by themselves. Upkeep's one-off requests keep the plain client: their prompts are never reused, and
    /// writing them to Anthropic's cache would cost a quarter more.
    /// </summary>
    public IChatClient ForConversation(IChatClient client) => Protocol == Protocol.Anthropic ? new CachedAnthropic(client, Model) : client;

    private sealed class CachedAnthropic(IChatClient inner, string model) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetResponseAsync(messages, Cached(options), cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                       CancellationToken cancellationToken = default) =>
            base.GetStreamingResponseAsync(messages, Cached(options), cancellationToken);

        private ChatOptions Cached(ChatOptions? options)
        {
            var cached = options?.Clone() ?? new ChatOptions();
            var tokens = cached.MaxOutputTokens ?? 1024;
            cached.RawRepresentationFactory = _ => new MessageCreateParams { MaxTokens = tokens, Messages = [], Model = model, CacheControl = new CacheControlEphemeral() };
            return cached;
        }
    }
}

public sealed record Preset(string Name, Protocol Protocol, string BaseUrl, string ModelHint, bool NeedsKey);
