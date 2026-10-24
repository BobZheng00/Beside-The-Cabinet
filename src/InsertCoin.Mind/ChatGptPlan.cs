using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;

#pragma warning disable OPENAI001                           // the Responses API's types are marked experimental

namespace InsertCoin.Mind;

/// <summary>Why she waits instead of running on the player's ChatGPT plan.</summary>
public enum PlanStop
{
    /// <summary>The plan's limit for this app is reached: she tries again now and then, and whenever the player writes to her.</summary>
    Limit,
    /// <summary>The account can't share its plan with apps: until they sign in with another.</summary>
    NotEligible,
    /// <summary>Signed out (never signed in, disconnected in ChatGPT, the sign-in lapsed): until they sign in again.</summary>
    SignedOut,
}

/// <summary>A request not made, or turned down, because she waits on the plan (see <see cref="ChatGptPlan.Stopped"/>). Its message is the plain line.</summary>
/// <param name="told">This request stopped the plan, so the player is being told already (see <see cref="ChatGptPlan.Stopping"/>).</param>
public sealed class PlanStopped(PlanStop why, bool told = false) : Exception(ChatGptPlan.Plainly(why))
{
    public PlanStop Why => why;
    public bool Told => told;
}

/// <summary>
/// ChatGPT turned a request on the plan down for its sign-in or permission (401 or 403 with no code that says more),
/// even with a fresh token: passing as far as can be told, so the sign-in is kept, and said in the plan's words.
/// </summary>
public sealed class PlanRefused(int status, Exception inner)
    : HttpRequestException("ChatGPT turned this request down. Try again later; if it keeps happening, Disconnect and sign in again in Settings → Mind.",
                           inner, (HttpStatusCode)status);

/// <summary>Signing in with ChatGPT didn't finish; the message says why, plainly.</summary>
public sealed class PlanSignInFailed(string message) : Exception(message);

/// <summary>
/// The player's ChatGPT plan, which her mind can run on instead of an API key (spec 2026-10-18-chatgpt-plan.md): Sign in
/// with ChatGPT in their own browser (OpenID Connect with PKCE, the answer coming back to this PC on 127.0.0.1), then
/// every request through OpenAI's Responses API with their access token, within the plan's rules (see <see cref="Client"/>).
/// The tokens and the account's email are kept encrypted (<c>keep</c>, a <see cref="KeyStore"/>'s), never logged; the
/// app's client id, which OpenAI issues at the first sign-in, and this installation's host id are no secret and sit in
/// a file of their own, kept when the player disconnects so that signing in again doesn't register the app again.
/// </summary>
public sealed class ChatGptPlan
{
    /// <summary>OpenAI's sign-in. Dev: a stand-in (tools/dev/mock_chatgpt.py), so that testing never needs a real account.</summary>
    public static string Issuer { get; set; } = "https://auth.openai.com";

    /// <summary>Where requests on the plan go. Dev: the stand-in.</summary>
    public static string Api { get; set; } = "https://api.openai.com/v1";

    /// <summary>ChatGPT's own page of usage, where the player sees and sets this app's limit.</summary>
    public const string ManageUsage = "https://chatgpt.com/settings/usage";

    /// <summary>How often she tries the plan again once its limit is reached, unless the player writes to her first.</summary>
    public static readonly TimeSpan TryAgainAfter = TimeSpan.FromMinutes(15);

    private const string AppName = "Beside the Cabinet";
    private const string Resource = "https://api.openai.com/v1";        // the real one, even at a stand-in: it names the API, not a place
    private const string Scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    private const string PlanScope = "chatgpt.tokens.use.direct";
    private static readonly TimeSpan Ahead = TimeSpan.FromMinutes(5);  // refreshed this long before the access token lapses
    private static readonly ChatRole Developer = new("developer");

    private readonly Action<string?> _keep;
    private readonly string _idsFile;
    private readonly HttpMessageHandler? _handler;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _refreshing = new(1, 1);        // one refresh at a time: each replaces the refresh token
    private Tokens? _tokens;
    private DateTimeOffset _tryAgainAt;

    private sealed record Tokens(string Access, string Refresh, DateTimeOffset Expires, string? Email);
    private sealed record Ids(string Host, string? Client);

    /// <param name="kept">The tokens as kept (encrypted, by <paramref name="keep"/>); null: none.</param>
    /// <param name="keep">Keeps the tokens, or forgets them when given null.</param>
    /// <param name="handler">Tests: a stand-in for every request, sign-in and Responses alike.</param>
    public ChatGptPlan(string? kept, Action<string?> keep, string idsFile, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        _keep = keep;
        _idsFile = idsFile;
        _handler = handler;
        _clock = clock ?? TimeProvider.System;
        _http = handler is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : new HttpClient(handler, disposeHandler: false);
        try
        {
            _tokens = kept is null ? null : JsonSerializer.Deserialize<Tokens>(kept);
        }
        catch (JsonException)
        {
            _tokens = null;
        }
        Stopped = _tokens is null ? PlanStop.SignedOut : null;
    }

    /// <summary>Signed in: the plan can be asked (unless <see cref="Stopped"/> says otherwise).</summary>
    public bool Connected => _tokens is not null;

    /// <summary>The signed-in account's email, to show which account it is; null when ChatGPT didn't say.</summary>
    public string? Email => _tokens?.Email;

    /// <summary>Why she waits instead of using the plan; null while it works.</summary>
    public PlanStop? Stopped { get; private set; }

    /// <summary>The plan just stopped answering, for this reason: the player is told once, plainly (not again until it worked in between).</summary>
    public event Action<PlanStop>? Stopping;

    /// <summary>How things stand changed: signed in or out, stopped, or working again (after <see cref="Stopping"/>, when both come).</summary>
    public event Action? Changed;

    /// <summary>What a request she couldn't make says, in the app's words: the plain line, with where to put it right.</summary>
    public static string Plainly(PlanStop why) => why switch
    {
        PlanStop.Limit => "Usage limit reached: review your plan or this app's limit in ChatGPT settings (Manage usage, in Settings → Mind).",
        PlanStop.NotEligible => "This ChatGPT account can't use its plan in apps. Sign in with another, or use an API key, in Settings → Mind.",
        _ => "Signed out of ChatGPT: sign in again in Settings → Mind (Continue with ChatGPT).",
    };

    /// <summary>The player wrote to her: once the limit was reached, their message tries the plan again rather than waiting.</summary>
    public void TryAgain()
    {
        if (Stopped == PlanStop.Limit)
            _tryAgainAt = default;
    }

    // ---- Signing in -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Signs in: <paramref name="browse"/> opens OpenAI's sign-in in the player's browser, they sign in and allow plan
    /// usage, and the browser comes back to a listener on 127.0.0.1 here. Throws <see cref="PlanSignInFailed"/> with what
    /// to say when it didn't finish.
    /// </summary>
    public async Task SignIn(Func<Uri, Task> browse, CancellationToken cancel)
    {
        var ids = LoadIds();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/auth/callback";
        string state = Random(), nonce = Random(), verifier = Random();
        var query = new Dictionary<string, string>
        {
            ["client_id"] = ids.Client ?? "dynamic_agent_client", ["response_type"] = "code", ["redirect_uri"] = redirect, ["scope"] = Scopes,
            ["resource"] = Resource, ["state"] = state, ["nonce"] = nonce, ["code_challenge_method"] = "S256",
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["ext_agent_host_id"] = ids.Host,
        };
        if (ids.Client is null)
            query["agent_name_hint"] = AppName;                    // only when registering
        var callback = Callback(listener, state, ids.Client, cancel);
        await browse(new Uri($"{Issuer}/api/accounts/authorize?{string.Join("&", query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"))}"));
        var (code, client) = await callback;
        if (client != ids.Client)
            SaveIds(ids with { Client = client });             // kept before the code is used: a failed exchange doesn't register the app again
        JsonNode answer;
        try
        {
            answer = await Token(new()
            {
                ["grant_type"] = "authorization_code", ["client_id"] = client, ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirect,
                ["resource"] = Resource,
            }, cancel);
        }
        catch (Exception e) when (e is TokenRefused || e is HttpRequestException { StatusCode: { } status } && (int)status is >= 400 and < 500)
        {
            throw new PlanSignInFailed("ChatGPT didn't accept the sign-in. Continue with ChatGPT to try again.");
        }
        // granted scopes are listed unless they are all that was asked for (RFC 6749, 5.1)
        if ((string?)answer["scope"] is { } scope && !scope.Split(' ').Contains(PlanScope))
            throw new PlanSignInFailed("Signed in, but plan usage wasn't allowed. Continue with ChatGPT again, and allow it.");
        _tokens = Read(answer, EmailIn((string?)answer["id_token"], nonce));
        Keep();
        Stopped = null;
        _tryAgainAt = default;
        Changed?.Invoke();
    }

    /// <summary>
    /// Waits for the browser to come back: each connection on its own (a browser may open one and send nothing), until
    /// one brings this sign-in's answer.
    /// </summary>
    private static async Task<(string Code, string Client)> Callback(TcpListener listener, string state, string? saved, CancellationToken cancel)
    {
        var came = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var stop = cancel.Register(() => came.TrySetCanceled(cancel));
        _ = Task.Run(async () =>
        {
            while (!came.Task.IsCompleted)
            {
                TcpClient connection;
                try
                {
                    connection = await listener.AcceptTcpClientAsync(cancel);
                }
                catch (Exception)
                {
                    return;                                         // stopped: signed in, or given up
                }
                _ = Serve(connection);
            }
        }, CancellationToken.None);
        return await came.Task;

        async Task Serve(TcpClient connection)
        {
            using var _ = connection;
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            wait.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var asked = await reader.ReadLineAsync(wait.Token);                 // GET /auth/callback?… HTTP/1.1
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(wait.Token)))
                {
                }
                var target = asked?.Split(' ') is ["GET", var path, _] && path.StartsWith('/') ? new Uri("http://127.0.0.1" + path) : null;
                if (target?.AbsolutePath != "/auth/callback")
                {
                    await Answer(stream, "404 Not Found", "Nothing here.", wait.Token);
                    return;
                }
                var query = HttpUtility.ParseQueryString(target.Query);
                if (query["state"] != state)
                {
                    await Answer(stream, "400 Bad Request", $"This isn't the sign-in {AppName} started. Go back to the app and try again.", wait.Token);
                    return;
                }
                if (query["error"] is { } error)
                {
                    await Answer(stream, "200 OK", $"Sign-in didn't finish. You can close this tab and go back to {AppName}.", wait.Token);
                    came.TrySetException(new PlanSignInFailed(error == "access_denied" ? "Sign-in was turned down in the browser." : $"Sign-in didn't finish ({error})."));
                    return;
                }
                var client = query["client_id"] ?? saved;
                // the app registered already: an answer for another registration isn't this sign-in's
                if (query["code"] is not { Length: > 0 } code || client is null or "dynamic_agent_client" || (saved is not null && client != saved))
                {
                    await Answer(stream, "200 OK", $"Sign-in didn't finish. You can close this tab and go back to {AppName}.", wait.Token);
                    came.TrySetException(new PlanSignInFailed("ChatGPT didn't finish registering the app. Try again."));
                    return;
                }
                await Answer(stream, "200 OK", $"Signed in. You can close this tab and go back to {AppName}.", wait.Token);
                came.TrySetResult((code, client));
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or UriFormatException)
            {
                // a connection that sent nothing, or went away: the next one may bring the answer
            }
        }
    }

    private static async Task Answer(Stream stream, string status, string words, CancellationToken cancel)
    {
        var page = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>{AppName}</title>" +
                                          $"<p style=\"font:17px system-ui;max-width:32rem;margin:18vh auto\">{WebUtility.HtmlEncode(words)}</p>");
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {page.Length}\r\n" +
                                           "Cache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancel);
        await stream.WriteAsync(page, cancel);
    }

    /// <summary>
    /// The email in the ID token, read but not verified: it came straight from OpenAI's token endpoint over TLS, which
    /// OpenID Connect allows in place of checking its signature (Core 1.0, 3.1.3.7), and only the email is shown from it.
    /// </summary>
    private static string? EmailIn(string? idToken, string nonce)
    {
        if (idToken?.Split('.') is not [_, var payload, _])
            return null;
        JsonNode? claims;
        try
        {
            claims = JsonNode.Parse(FromBase64Url(payload));
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return null;
        }
        if ((string?)claims?["nonce"] is { } said && said != nonce)
            throw new PlanSignInFailed("ChatGPT's answer didn't match this sign-in. Try again.");
        return (string?)claims?["email"];
    }

    /// <summary>Forgets the sign-in here and tells ChatGPT to end it; false when ChatGPT couldn't be told (the player should disconnect the app there).</summary>
    public async Task<bool> Disconnect(CancellationToken cancel = default)
    {
        var tokens = _tokens;
        Forget();
        Stopped = PlanStop.SignedOut;                              // their choice: nothing to tell them
        Changed?.Invoke();
        if (tokens is null || LoadIds().Client is not { } client)
            return true;
        try
        {
            using var response = await _http.PostAsync($"{Issuer}/api/accounts/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = tokens.Refresh, ["token_type_hint"] = "refresh_token", ["client_id"] = client,
            }), cancel);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    // ---- Tokens -------------------------------------------------------------------------------------------------------------

    /// <summary>The access token, refreshed when it is about to lapse (or, <paramref name="fresh"/>, now). A refresh ChatGPT turns down signs the player out.</summary>
    private async Task<string> Access(bool fresh, CancellationToken cancel)
    {
        await _refreshing.WaitAsync(cancel);
        try
        {
            if (_tokens is not { } tokens)
                throw Stop(PlanStop.SignedOut);
            if (!fresh && tokens.Expires - Ahead > _clock.GetUtcNow())
                return tokens.Access;
            JsonNode answer;
            try
            {
                answer = await Token(new()
                {
                    ["grant_type"] = "refresh_token", ["client_id"] = LoadIds().Client ?? "", ["refresh_token"] = tokens.Refresh, ["resource"] = Resource,
                }, cancel);
            }
            catch (TokenRefused)
            {
                Forget();                                           // a refresh token that's no good: only signing in again helps
                throw Stop(PlanStop.SignedOut);
            }                                                       // anything else (a 429, the network) is passing: the sign-in kept
            _tokens = Read(answer, tokens.Email);
            Keep();
            return _tokens.Access;
        }
        finally
        {
            _refreshing.Release();
        }
    }

    /// <summary>ChatGPT's token endpoint no longer takes this grant (OpenAI's Errors and recovery: a refresh token that's no good).</summary>
    private sealed class TokenRefused(string code) : Exception($"ChatGPT no longer accepts this sign-in ({code}).");

    /// <summary>The codes that mean a grant is no good for good; any other refusal is passing.</summary>
    private static readonly string[] Lapsed =
        ["invalid_grant", "invalid_refresh_token", "token_expired", "refresh_token_expired", "refresh_token_invalidated", "refresh_token_reused"];

    private async Task<JsonNode> Token(Dictionary<string, string> form, CancellationToken cancel)
    {
        using var response = await _http.PostAsync($"{Issuer}/api/accounts/oauth/token", new FormUrlEncodedContent(form), cancel);
        var body = await response.Content.ReadAsStringAsync(cancel);
        JsonNode? answer = null;
        try
        {
            answer = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
        }
        if ((int)response.StatusCode is >= 400 and < 500 && CodeIn(answer) is { } code && Lapsed.Contains(code))
            throw new TokenRefused(code);
        response.EnsureSuccessStatusCode();                         // anything else (a 429, a 408, a 5xx): passing, the sign-in kept
        return answer ?? throw new HttpRequestException("ChatGPT's sign-in answered with something unreadable.");
    }

    private Tokens Read(JsonNode answer, string? email) =>
        (string?)answer["access_token"] is { Length: > 0 } access && ((string?)answer["refresh_token"] ?? _tokens?.Refresh) is { Length: > 0 } refresh
            ? new Tokens(access, refresh, _clock.GetUtcNow().AddSeconds((double?)answer["expires_in"] ?? 3600), email)
            : throw new PlanSignInFailed("ChatGPT sent back an incomplete sign-in. Try again.");

    private void Keep() => _keep(JsonSerializer.Serialize(_tokens));

    private void Forget()
    {
        _tokens = null;
        _keep(null);
    }

    private Ids LoadIds()
    {
        try
        {
            if (JsonSerializer.Deserialize<Ids>(File.ReadAllText(_idsFile)) is { Host.Length: > 0 } ids)
                return ids;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
        }
        var made = new Ids($"urn:uuid:{Guid.NewGuid()}", null);   // made once for this installation
        SaveIds(made);
        return made;
    }

    private void SaveIds(Ids ids)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_idsFile))!);
        File.WriteAllText(_idsFile, JsonSerializer.Serialize(ids));
    }

    // ---- Models and requests ------------------------------------------------------------------------------------------------

    /// <summary>The models the player's account offers apps, in ChatGPT's order: each one's name to send, and to show.</summary>
    public async Task<IReadOnlyList<(string Slug, string Name)>> Models(CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Api}/models");
        request.Headers.Authorization = new("Bearer", await Access(fresh: false, cancel));
        using var response = await _http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        var listed = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel))?["models"]?.AsArray() ?? [];
        return [.. listed.Where(m => (string?)m?["visibility"] == "list" && (string?)m?["slug"] is { Length: > 0 })
                         .Select(m => ((string)m!["slug"]!, (string?)m["display_name"] ?? (string)m["slug"]!))];
    }

    /// <summary>
    /// Her mind on the plan, with <paramref name="model"/>: OpenAI's Responses API, asked as the plan allows (see
    /// <see cref="Shaped"/>), streamed, each request with the access token as it is then. While she waits on the plan
    /// (see <see cref="Stopped"/>) nothing is sent: each request fails at once, so a reminder comes in the app's words.
    /// </summary>
    public IChatClient Client(string model)
    {
        var key = new ApiKeyCredential("signed out");
        var options = new OpenAIClientOptions { Endpoint = new Uri(Api), NetworkTimeout = TimeSpan.FromSeconds(60), RetryPolicy = new NotOnLimits() };
        if (_handler is not null)
            options.Transport = new HttpClientPipelineTransport(new HttpClient(_handler, disposeHandler: false));
        return new OnPlan(new OpenAIClient(key, options).GetResponsesClient().AsIChatClient(model), this, key);
    }

    /// <summary>
    /// The request as the plan takes it (OpenAI's preview limitations): the opening system message as instructions, any
    /// later one as a developer message; no output limit, temperature, top-p, earlier response or background; only her
    /// own function tools; not stored. The callers stay as they are for every provider.
    /// </summary>
    internal static (List<ChatMessage> Sent, ChatOptions Options) Shaped(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var all = messages.ToList();
        var lead = all.TakeWhile(m => m.Role == ChatRole.System).Count();
        var shaped = options?.Clone() ?? new ChatOptions();
        var instructions = string.Join("\n\n", all.Take(lead).Select(m => m.Text).Prepend(options?.Instructions).Where(t => !string.IsNullOrEmpty(t)));
        shaped.Instructions = instructions.Length > 0 ? instructions : null;
        shaped.MaxOutputTokens = null;
        shaped.Temperature = null;
        shaped.TopP = null;
        shaped.ConversationId = null;
        shaped.AllowBackgroundResponses = null;
        shaped.Tools = options?.Tools?.Where(t => t is AIFunctionDeclaration).ToList() is { Count: > 0 } own ? own : null;
        shaped.RawRepresentationFactory = _ => new CreateResponseOptions { StoredOutputEnabled = false };
        return ([.. all.Skip(lead).Select(m => m.Role == ChatRole.System ? new ChatMessage(Developer, m.Contents) : m)], shaped);
    }

    /// <summary>Throws while she waits on the plan; once the limit was reached, lets a request through every <see cref="TryAgainAfter"/>.</summary>
    private void Check()
    {
        if (Stopped is { } why && (why != PlanStop.Limit || _clock.GetUtcNow() < _tryAgainAt))
            throw new PlanStopped(why);
    }

    private PlanStopped Stop(PlanStop why)
    {
        var news = Stopped != why;
        Stopped = why;
        if (why == PlanStop.Limit)
            _tryAgainAt = _clock.GetUtcNow() + TryAgainAfter;
        var told = news && Stopping is not null;
        if (news)
        {
            Stopping?.Invoke(why);
            Changed?.Invoke();
        }
        return new PlanStopped(why, told);
    }

    /// <summary>It answered: whatever stopped it is over.</summary>
    private void Works()
    {
        if (Stopped is null)
            return;
        Stopped = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// What a failed request means for the plan, by OpenAI's error code (Errors and recovery): null for a passing failure,
    /// the sign-in kept. Signing out is left to a refresh ChatGPT turns down for good (see <see cref="Access"/>).
    /// </summary>
    private PlanStopped? Halt(string? code) => code switch
    {
        "subscription_sharing_usage_limit_exceeded" => Stop(PlanStop.Limit),
        "subscription_sharing_user_not_eligible" => Stop(PlanStop.NotEligible),
        _ => null,
    };

    private static string? CodeIn(JsonNode? body) => body?["error"] switch
    {
        JsonValue code => (string?)code,
        { } error => (string?)error["code"],
        null => null,
    };

    private static string? CodeIn(ClientResultException e)
    {
        try
        {
            return e.GetRawResponse()?.Content is { } content ? CodeIn(JsonNode.Parse(content.ToString())) : null;
        }
        catch (Exception x) when (x is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The library's retry, once and after a pause, for anything passing; never for a limit reached, which retrying only asks again.</summary>
    private sealed class NotOnLimits() : ClientRetryPolicy(maxRetries: 1)
    {
        protected override bool ShouldRetry(PipelineMessage message, Exception? exception) =>
            message.Response?.Status != 429 && base.ShouldRetry(message, exception);

        protected override ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception) =>
            message.Response?.Status == 429 ? ValueTask.FromResult(false) : base.ShouldRetryAsync(message, exception);
    }

    private sealed class OnPlan(IChatClient inner, ChatGptPlan plan, ApiKeyCredential key) : DelegatingChatClient(inner)
    {
        // the plan answers only streamed
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                         [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            plan.Check();
            var (sent, shaped) = Shaped(messages, options);
            for (var attempt = 0; ; attempt++)
            {
                key.Update(await plan.Access(fresh: attempt > 0, cancellationToken));
                await using var updates = base.GetStreamingResponseAsync(sent, shaped, cancellationToken).GetAsyncEnumerator(cancellationToken);
                bool started = false, done = false, again = false;
                while (true)
                {
                    try
                    {
                        if (!await updates.MoveNextAsync())
                            break;
                    }
                    catch (ClientResultException e) when (e.Status == 401 && attempt == 0 && !started)
                    {
                        again = true;                               // turned down before it was due to lapse: refreshed, and asked once more
                        break;
                    }
                    catch (ClientResultException e)
                    {
                        if (plan.Halt(CodeIn(e)) is { } stopped)
                            throw stopped;
                        if (e.Status is 401 or 403)                 // even with a fresh token: said in the plan's words, the sign-in kept
                            throw new PlanRefused(e.Status, e);
                        throw;
                    }
                    started = true;
                    var update = updates.Current;
                    // a failure after the stream began (a limit reached part-way): as a failed request
                    var failed = update.RawRepresentation switch
                    {
                        StreamingResponseFailedUpdate f => f.Response?.Error?.Code.ToString() ?? "",
                        StreamingResponseErrorUpdate e => e.Code ?? "",
                        _ => null,
                    };
                    if (failed is not null)
                        throw (Exception?)plan.Halt(failed) ?? new HttpRequestException($"ChatGPT stopped the reply ({failed}).", null, HttpStatusCode.ServiceUnavailable);
                    done |= update.RawRepresentation is StreamingResponseCompletedUpdate;
                    yield return update;
                }
                if (again)
                    continue;
                if (!done)                                          // only a completed response counts
                    throw new HttpRequestException("ChatGPT's reply stopped before it was finished.", null, HttpStatusCode.ServiceUnavailable);
                plan.Works();
                yield break;
            }
        }
    }

    private static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded + new string('=', (4 - padded.Length % 4) % 4));
    }
}
