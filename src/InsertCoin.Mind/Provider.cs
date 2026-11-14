using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using OpenAI;

namespace InsertCoin.Mind;

/// <summary>
/// The protocol a provider speaks. Most third-party and local servers speak OpenAI's; the player's ChatGPT plan is
/// signed in to rather than given a key (see <see cref="ChatGptPlan"/>).
/// </summary>
public enum Protocol { OpenAi, Anthropic, ChatGpt }

/// <summary>
/// Where her replies come from: the user's own provider, server address and model. The key is kept apart, encrypted
/// (see <see cref="KeyStore"/>). Nothing here depends on one provider or model.
/// </summary>
public sealed record Provider(Protocol Protocol, string BaseUrl, string Model)
{
    /// <summary>Starting points for the settings form. "Other" takes any OpenAI-compatible server.</summary>
    public static readonly Preset[] Presets =
    [
        new("OpenAI", Protocol.OpenAi, OpenAiApi, "gpt-6-luna", NeedsKey: true, "https://platform.openai.com/api-keys"),
        new("ChatGPT plan", Protocol.ChatGpt, ChatGptPlan.Api, "", NeedsKey: false),
        new("Anthropic", Protocol.Anthropic, "https://api.anthropic.com", "claude-haiku-5-5", NeedsKey: true, "https://platform.claude.com/settings/keys"),
        new("OpenRouter", Protocol.OpenAi, "https://openrouter.ai/api/v1", "openai/gpt-6-luna", NeedsKey: true, "https://openrouter.ai/settings/keys"),
        new("DeepSeek", Protocol.OpenAi, "https://api.deepseek.com/v1", "deepseek-flash", NeedsKey: true, "https://platform.deepseek.com/api_keys"),
        new("Qwen (DashScope)", Protocol.OpenAi, "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", NeedsKey: true, "https://bailian.console.aliyun.com/"),
        new("Ollama (this PC)", Protocol.OpenAi, "http://localhost:11434/v1", "", NeedsKey: false, "https://ollama.com/download"),
        new("LM Studio (this PC)", Protocol.OpenAi, "http://localhost:1234/v1", "", NeedsKey: false, "https://lmstudio.ai/"),
        new("Other (OpenAI-compatible)", Protocol.OpenAi, "", "", NeedsKey: true),
    ];

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>OpenAI's own API: her replies go through its Responses API, with its web search (see <see cref="OwnSearch"/>).</summary>
    public const string OpenAiApi = "https://api.openai.com/v1";

    /// <summary>Test runs: a stand-in that answers in place of <see cref="OpenAiApi"/> (tools/dev/mock_chatgpt.py at /openai/v1); null for OpenAI itself.</summary>
    public static string? OpenAiAt { get; set; }

    /// <summary>OpenAI's own API, with a key (not the ChatGPT plan, nor another server speaking its protocol).</summary>
    public bool IsOpenAi => Protocol == Protocol.OpenAi && BaseUrl.TrimEnd('/') == OpenAiApi;

    /// <summary>
    /// Whose web search she uses when the provider runs it itself, in a word for the player ("Anthropic's"); null when
    /// this app can't use the provider's own (a SearXNG can still give her one).
    /// </summary>
    public string? OwnSearch => Protocol == Protocol.Anthropic ? "Anthropic's" : Protocol == Protocol.ChatGpt || IsOpenAi ? "OpenAI's" : null;

    /// <summary>A provider only counts once it says where to go and which model to ask.</summary>
    public static Provider? From(string? protocol, string? baseUrl, string? model) =>
        string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out _)
            ? null
            : new Provider(Enum.TryParse<Protocol>(protocol, ignoreCase: true, out var p) ? p : Protocol.OpenAi, baseUrl.Trim(), model.Trim());

    /// <summary>What to tell the player when <see cref="From"/> gave nothing: the field that's missing.</summary>
    public static string Missing(string? baseUrl, string? model) =>
        (Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out _), string.IsNullOrWhiteSpace(model)) switch
        {
            (true, true) => "Fill in the model first.",
            (false, false) => "Fill in the server first.",
            _ => "Fill in the server and the model first.",
        };

    // One retry: someone is looking at thinking dots, so a broken setup should say so in seconds, not after a minute of backoff.
    public IChatClient Connect(string? key, ChatGptPlan? plan = null) => Protocol switch
    {
        Protocol.ChatGpt => (plan ?? throw new InvalidOperationException("The ChatGPT plan needs its sign-in.")).Client(Model),
        Protocol.Anthropic => new AnthropicClient { ApiKey = key, BaseUrl = BaseUrl.TrimEnd('/'), Timeout = Timeout, MaxRetries = 1 }.AsIChatClient(Model),
#pragma warning disable OPENAI001                           // the Responses client is marked experimental
        _ when IsOpenAi => new Unstored(OpenAi(key).GetResponsesClient().AsIChatClient(Model)),
#pragma warning restore OPENAI001
        _ => WithoutThinking(ToolsWithoutReasoning(WithoutOwnSearch(OpenAi(key).GetChatClient(Model).AsIChatClient()))),
    };

    /// <summary>
    /// Through the chat API no provider's own search is offered (the client would send it as web_search_options, which a
    /// strict server turns down and another may run, billed to the player unasked): the backup behind the ChatGPT plan
    /// is asked as the plan is, search and all.
    /// </summary>
    private static IChatClient WithoutOwnSearch(IChatClient client) =>
        client.AsBuilder().ConfigureOptions(options =>
        {
            if (options.Tools?.Any(t => t is HostedWebSearchTool) == true)
                options.Tools = options.Tools.Where(t => t is not HostedWebSearchTool).ToList() is { Count: > 0 } rest ? rest : null;
        }).Build();

    /// <summary>
    /// Whether what the provider's web search brought back was a page it read (OpenAI's reasoning models open a page, or
    /// look within one, as steps of a search) rather than a search: <paramref name="page"/> is that page. OpenAI charges
    /// for its searches, not for those steps.
    /// </summary>
    public static bool ReadsAPage(WebSearchToolResultContent found, out Uri? page)
    {
#pragma warning disable OPENAI001
        page = found.RawRepresentation switch
        {
            OpenAI.Responses.WebSearchCallResponseItem { Action: OpenAI.Responses.WebSearchOpenPageAction open } => open.Uri,
            OpenAI.Responses.WebSearchCallResponseItem { Action: OpenAI.Responses.WebSearchFindInPageAction find } => find.Uri,
            _ => null,
        };
        return found.RawRepresentation is OpenAI.Responses.WebSearchCallResponseItem { Action: OpenAI.Responses.WebSearchOpenPageAction or OpenAI.Responses.WebSearchFindInPageAction };
#pragma warning restore OPENAI001
    }

    /// <summary>
    /// DeepSeek's small model thinks unless told not to, and then wants its thinking sent back with every later request
    /// offering tools (a 400 otherwise, its docs say), which this client can't do. Every other model's requests stay as they were.
    /// </summary>
    private IChatClient WithoutThinking(IChatClient client) =>
        Model != "deepseek-flash" ? client
        : client.AsBuilder().ConfigureOptions(options => options.RawRepresentationFactory = _ =>
        {
            var request = new OpenAI.Chat.ChatCompletionOptions();
#pragma warning disable SCME0001                            // the patch for a field the client doesn't know is marked experimental
            request.Patch.Set("$.thinking"u8, BinaryData.FromString("""{"type":"disabled"}"""));
#pragma warning restore SCME0001
            return request;
        }).Build();

    /// <summary>
    /// On the chat API, OpenAI's newest small model calls tools only with reasoning off (OpenAI's page for it), so a
    /// request offering tools says so, through any other server. Every other model's requests stay as they were. OpenAI
    /// itself is asked through the Responses API (#266), where its models call tools while reasoning.
    /// </summary>
    private IChatClient ToolsWithoutReasoning(IChatClient client) =>
        Model.Split('/')[^1] != "gpt-6-luna" ? client
        : client.AsBuilder().ConfigureOptions(options =>
        {
            if (options.Tools is { Count: > 0 })
                options.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None };
        }).Build();

    // local servers want no key, but the client insists on one
    private OpenAIClient OpenAi(string? key) =>
        new(new ApiKeyCredential(string.IsNullOrEmpty(key) ? "none" : key),
            new OpenAIClientOptions { Endpoint = new Uri(IsOpenAi && OpenAiAt is { } at ? at : BaseUrl), NetworkTimeout = Timeout, RetryPolicy = new ClientRetryPolicy(maxRetries: 1) });

    /// <summary>
    /// Whether the provider searches the web itself for this model, asked once with its search tool (a search, so it
    /// costs a little; only when the player tests). Anthropic's and OpenAI's go through the API her replies use. Another
    /// OpenAI-compatible one is asked through OpenAI's Responses API, the only one with a search tool for most models:
    /// her replies there use the chat API, which has none (2026-10-11: the player's provider searched with a GPT model
    /// through the first, not the second), so for those this says only that the provider could.
    /// </summary>
    public async Task<bool> SearchesItself(string? key, CancellationToken cancel)
    {
#pragma warning disable OPENAI001                           // the Responses client is marked experimental
        using var client = Protocol == Protocol.Anthropic || IsOpenAi ? Connect(key) : OpenAi(key).GetResponsesClient().AsIChatClient(Model);
#pragma warning restore OPENAI001
        try
        {
            var response = await client.GetResponseAsync("What is the top news headline today? Search the web for it.",
                                                         new ChatOptions { Tools = [new HostedWebSearchTool()], MaxOutputTokens = 2000 }, cancel);
            return response.Messages.SelectMany(m => m.Contents).Any(c => c is WebSearchToolCallContent or WebSearchToolResultContent);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;                                   // no such API at this server, or its search turned down: either way, none
        }
    }

    /// <summary>
    /// The tool a streamed update shows the model beginning to call, before its arguments have all come (the client
    /// only hands the call over once they have): OpenAI's first piece of a tool call, Anthropic's start of a tool-use
    /// block. Null for anything else, and for a client whose updates aren't known here.
    /// </summary>
    public static string? ToolStarting(ChatResponseUpdate update) => update.RawRepresentation switch
    {
        OpenAI.Chat.StreamingChatCompletionUpdate piece => piece.ToolCallUpdates.FirstOrDefault(t => !string.IsNullOrEmpty(t.FunctionName))?.FunctionName,
#pragma warning disable OPENAI001
        OpenAI.Responses.StreamingResponseOutputItemAddedUpdate { Item: OpenAI.Responses.FunctionCallResponseItem call } => call.FunctionName,
#pragma warning restore OPENAI001
        RawMessageStreamEvent @event when @event.TryPickContentBlockStart(out var start) && start.ContentBlock.TryPickToolUse(out var use) => use.Name,
        _ => null,
    };

    /// <summary>What she answers through, in a few words: the server's host, or the player's ChatGPT plan.</summary>
    public string Through => Protocol == Protocol.ChatGpt ? "your ChatGPT plan"
                             : Uri.TryCreate(BaseUrl, UriKind.Absolute, out var server) ? server.Host : BaseUrl;

    /// <summary>Its name, for the player: its preset's ("OpenRouter", "Ollama"), or else the server's host, or "the model on this PC".</summary>
    public string Name => Presets.FirstOrDefault(p => p.Protocol == Protocol && p.BaseUrl == BaseUrl)?.Name.Split(" (")[0] ?? (IsLocal ? "the model on this PC" : Through);

    /// <summary>A server on this PC (Ollama, LM Studio…): local models usually have small context windows. Never the ChatGPT plan, even at a stand-in.</summary>
    public bool IsLocal => Protocol != Protocol.ChatGpt && Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) && (url.IsLoopback || url.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase));

    /// <summary>How much of the conversation to keep word for word when the player hasn't chosen (see <see cref="Conversation.Window"/>).</summary>
    public int DefaultWindow => IsLocal ? Prompt.LocalWindow : Prompt.DefaultWindow;

    /// <summary>
    /// The client for her replies. Anthropic caches only what it is asked to: automatic caching is turned on (its mark
    /// follows the conversation as it grows; a read costs a tenth of fresh input). The others cache a stable prompt
    /// start by themselves. Upkeep's one-off requests keep the plain client: their prompts are never reused, and
    /// writing them to Anthropic's cache would cost a quarter more.
    /// </summary>
    public IChatClient ForConversation(IChatClient client) => Protocol == Protocol.Anthropic ? new CachedAnthropic(client, Model) : client;

    /// <summary>
    /// OpenAI's Responses API asked as the chat API was: nothing kept at OpenAI (the chat API keeps nothing unless asked).
    /// So its reasoning between her tool calls comes back encrypted, to be sent again (OpenAI's stateless use), and its
    /// web search names the pages it found. A model that doesn't reason turns the encrypted reasoning down: it is asked
    /// again without it, and never again by this client.
    /// </summary>
    private sealed class Unstored(IChatClient inner) : DelegatingChatClient(inner)
    {
        private bool _reasons = true;

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.GetResponseAsync(messages, Shaped(options), cancellationToken);
            }
            catch (ClientResultException e) when (_reasons && WithoutReasoning(e))
            {
                return await base.GetResponseAsync(messages, Shaped(options), cancellationToken);
            }
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                         [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var mayRetry = _reasons; ; mayRetry = false)
            {
                await using var updates = base.GetStreamingResponseAsync(messages, Shaped(options), cancellationToken).GetAsyncEnumerator(cancellationToken);
                bool started = false, again = false;
                while (true)
                {
                    try
                    {
                        if (!await updates.MoveNextAsync())
                            break;
                    }
                    catch (ClientResultException e) when (mayRetry && !started && WithoutReasoning(e))
                    {
                        again = true;
                        break;
                    }
                    started = true;
                    yield return updates.Current;
                }
                if (!again)
                    yield break;
            }
        }

        private bool WithoutReasoning(ClientResultException e)
        {
            if (e.Status != 400 || !e.Message.Contains("encrypted content", StringComparison.OrdinalIgnoreCase) || !e.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase))
                return false;
            _reasons = false;
            return true;
        }

#pragma warning disable OPENAI001                           // the Responses API's types are marked experimental
        private ChatOptions Shaped(ChatOptions? options)
        {
            var shaped = options?.Clone() ?? new ChatOptions();
            var reasons = _reasons;
            shaped.RawRepresentationFactory = _ =>
            {
                var request = new OpenAI.Responses.CreateResponseOptions { StoredOutputEnabled = false };
                request.IncludedProperties.Add(OpenAI.Responses.IncludedResponseProperty.WebSearchCallActionSources);
                if (reasons)
                    request.IncludedProperties.Add(OpenAI.Responses.IncludedResponseProperty.ReasoningEncryptedContent);
                return request;
            };
            return shaped;
        }
#pragma warning restore OPENAI001
    }

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

/// <param name="Site">Where to get what it needs: the provider's page of API keys, or a server's download page; empty for none.</param>
public sealed record Preset(string Name, Protocol Protocol, string BaseUrl, string ModelHint, bool NeedsKey, string Site = "")
{
    /// <summary>What the Model box says while it is empty: a server on this PC only has the models downloaded into it.</summary>
    public string ModelPlaceholder =>
        ModelHint.Length > 0 ? ModelHint
        : new Provider(Protocol, BaseUrl, ModelHint).IsLocal ? $"the name of a model already downloaded into {Name.Split(" (")[0]}"
        : "the model's name at this provider";
}
