using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>
/// Her mind on the player's ChatGPT plan: signing in (PKCE, a listener on 127.0.0.1), the tokens kept and refreshed,
/// requests shaped as the plan takes them, and what a limit, a lapsed sign-in or an account that can't share its plan
/// does. OpenAI is a stand-in throughout: never a real account.
/// </summary>
public class ChatGptPlanTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(-4));
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "insert-coin-mind-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeOpenAi _openAi = new();
    private readonly FakeClock _clock = new(Noon);
    private string? _kept;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string IdsFile => Path.Combine(_folder, "chatgpt.json");

    private ChatGptPlan Plan() => new(_kept, kept => _kept = kept, IdsFile, _openAi, _clock);

    /// <summary>OpenAI as the plan sees it: its sign-in, its models and its Responses API, each request logged.</summary>
    private sealed class FakeOpenAi : HttpMessageHandler
    {
        public List<(string Path, string Body, string? Bearer)> Asked { get; } = [];
        public string? Challenge, Nonce;
        public int Issued;
        public bool RefusesRefresh, BusyRefreshing, Revokes = true;
        public Queue<HttpResponseMessage> Answers { get; } = new();

        public IEnumerable<string> Paths => Asked.Select(a => a.Path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Asked.Add((path, body, request.Headers.Authorization?.Parameter));
            switch (path)
            {
                case "/api/accounts/oauth/token":
                    var form = HttpUtility.ParseQueryString(body);
                    if (form["grant_type"] == "authorization_code" && Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!))) != Challenge)
                        return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
                    if (form["grant_type"] == "refresh_token" && RefusesRefresh)
                        return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"refresh token reused"}""");
                    if (form["grant_type"] == "refresh_token" && BusyRefreshing)
                        return Json(HttpStatusCode.TooManyRequests, """{"error":"rate_limit_exceeded"}""");
                    Issued++;
                    var claims = Base64Url(Encoding.UTF8.GetBytes($$"""{"sub":"user-1","email":"player@example.com","nonce":"{{Nonce}}"}"""));
                    return Json(HttpStatusCode.OK, $$"""
                        {"access_token":"at-{{Issued}}","refresh_token":"rt-{{Issued}}","id_token":"e30.{{claims}}.sig","token_type":"Bearer","expires_in":3600,
                         "scope":"openid profile email offline_access resource.invoke chatgpt.tokens.use.direct"}
                        """);
                case "/api/accounts/oauth/revoke":
                    return new HttpResponseMessage(Revokes ? HttpStatusCode.OK : HttpStatusCode.BadGateway);
                case "/v1/models":
                    return Json(HttpStatusCode.OK, """
                        {"models":[{"slug":"gpt-5.5","display_name":"GPT-5.5","visibility":"list"},{"slug":"gpt-internal","display_name":"Internal","visibility":"hide"},
                                   {"slug":"gpt-5.5-mini","display_name":"GPT-5.5 mini","visibility":"list"}]}
                        """);
                default:
                    return Answers.Count > 0 ? Answers.Dequeue() : Streamed("Hi there.");
            }
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A reply on the plan as OpenAI streams it, ending with response.completed.</summary>
    private static HttpResponseMessage Streamed(string text) => Events(
        ("response.created", """{"type":"response.created","sequence_number":0,"response":{"id":"resp_1","object":"response","created_at":1790000000,"status":"in_progress","model":"gpt-5.5","output":[]}}"""),
        ("response.output_item.added", """{"type":"response.output_item.added","sequence_number":1,"output_index":0,"item":{"id":"msg_1","type":"message","status":"in_progress","role":"assistant","content":[]}}"""),
        ("response.output_text.delta", $$"""{"type":"response.output_text.delta","sequence_number":2,"item_id":"msg_1","output_index":0,"content_index":0,"delta":"{{text}}"}"""),
        ("response.output_item.done", $$$"""{"type":"response.output_item.done","sequence_number":3,"output_index":0,"item":{"id":"msg_1","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"{{{text}}}","annotations":[]}]}}"""),
        ("response.completed", $$$$"""
            {"type":"response.completed","sequence_number":4,"response":{"id":"resp_1","object":"response","created_at":1790000000,"status":"completed","model":"gpt-5.5",
             "output":[{"id":"msg_1","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"{{{{text}}}}","annotations":[]}]}],
             "usage":{"input_tokens":120,"input_tokens_details":{"cached_tokens":100},"output_tokens":4,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":124}}}
            """));

    private static HttpResponseMessage Events(params (string Event, string Data)[] events)
    {
        var stream = string.Concat(events.Select(e => $"event: {e.Event}\ndata: {e.Data.ReplaceLineEndings(" ")}\n\n"));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") };
    }

    /// <summary>The player signs in: their "browser" goes to OpenAI's sign-in, which sends it back to the app's listener as OpenAI would.</summary>
    private async Task<Uri> SignIn(ChatGptPlan plan, string? deny = null, string client = "oaiapp_test")
    {
        Uri? opened = null;
        await plan.SignIn(async page =>
        {
            opened = page;
            var asked = HttpUtility.ParseQueryString(page.Query);
            _openAi.Challenge = asked["code_challenge"];
            _openAi.Nonce = asked["nonce"];
            using var browser = new HttpClient();
            var back = deny is null ? $"code=c-1&state={Uri.EscapeDataString(asked["state"]!)}&client_id={client}&scope=openid"
                                    : $"error={deny}&state={Uri.EscapeDataString(asked["state"]!)}";
            await browser.GetAsync($"{asked["redirect_uri"]}?{back}");
        }, CancellationToken.None);
        return opened!;
    }

    [Fact]
    public async Task Signing_in_asks_for_plan_usage_with_pkce_and_keeps_the_tokens_and_email_for_the_keystore_only()
    {
        var plan = Plan();
        Assert.False(plan.Connected);
        Assert.Equal(PlanStop.SignedOut, plan.Stopped);

        var opened = await SignIn(plan);

        var asked = HttpUtility.ParseQueryString(opened.Query);
        Assert.Equal("https://auth.openai.com/api/accounts/authorize", opened.GetLeftPart(UriPartial.Path));
        Assert.Equal("dynamic_agent_client", asked["client_id"]);
        Assert.Equal("Beside the Cabinet", asked["agent_name_hint"]);
        Assert.StartsWith("urn:uuid:", asked["ext_agent_host_id"]);
        Assert.Equal("openid profile email offline_access resource.invoke chatgpt.tokens.use.direct", asked["scope"]);
        Assert.Equal("https://api.openai.com/v1", asked["resource"]);
        Assert.Equal("S256", asked["code_challenge_method"]);
        Assert.StartsWith("http://127.0.0.1:", asked["redirect_uri"]);
        Assert.EndsWith("/auth/callback", asked["redirect_uri"]);
        var exchange = HttpUtility.ParseQueryString(_openAi.Asked.Single(a => a.Path.EndsWith("/token")).Body);
        Assert.Equal("oaiapp_test", exchange["client_id"]);
        Assert.Equal("https://api.openai.com/v1", exchange["resource"]);

        Assert.True(plan.Connected);
        Assert.Null(plan.Stopped);
        Assert.Equal("player@example.com", plan.Email);
        Assert.Contains("rt-1", _kept);
        Assert.Contains("player@example.com", _kept);
        var ids = File.ReadAllText(IdsFile);
        Assert.Contains("oaiapp_test", ids);
        Assert.DoesNotContain("at-1", ids);
        Assert.DoesNotContain("player@example.com", ids);

        Assert.Equal("player@example.com", Plan().Email);             // kept for the next start
    }

    [Fact]
    public async Task Signing_in_again_uses_the_issued_client_and_the_same_host_and_registers_nothing_new()
    {
        var first = HttpUtility.ParseQueryString((await SignIn(Plan())).Query);
        var again = HttpUtility.ParseQueryString((await SignIn(Plan())).Query);

        Assert.Equal("oaiapp_test", again["client_id"]);
        Assert.Null(again["agent_name_hint"]);
        Assert.Equal(first["ext_agent_host_id"], again["ext_agent_host_id"]);
    }

    [Fact]
    public async Task Sign_in_turned_down_in_the_browser_says_so_and_keeps_nothing()
    {
        var plan = Plan();
        var failed = await Assert.ThrowsAsync<PlanSignInFailed>(() => SignIn(plan, deny: "access_denied"));
        Assert.Equal("Sign-in was turned down in the browser.", failed.Message);
        Assert.False(plan.Connected);
        Assert.Null(_kept);
    }

    [Fact]
    public async Task The_models_offered_are_the_accounts_listed_ones_in_order()
    {
        var plan = Plan();
        await SignIn(plan);

        var models = await plan.Models(CancellationToken.None);

        Assert.Equal([("gpt-5.5", "GPT-5.5"), ("gpt-5.5-mini", "GPT-5.5 mini")], models);
        Assert.Equal("at-1", _openAi.Asked.Last().Bearer);
    }

    [Fact]
    public async Task A_request_on_the_plan_is_streamed_unstored_with_instructions_and_only_her_own_tools()
    {
        var plan = Plan();
        await SignIn(plan);
        var client = plan.Client("gpt-5.5");
        var tool = AIFunctionFactory.Create(() => "done", "print_memo");

        var reply = await client.GetResponseAsync(
            [new(ChatRole.System, "You are Hana."), new(ChatRole.User, "Hello"), new(ChatRole.System, "[cabinet] A note."), new(ChatRole.User, "And?")],
            new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.7f, Tools = [tool, new HostedWebSearchTool()] });

        Assert.Equal("Hi there.", reply.Text);
        Assert.Equal(120, reply.Usage?.InputTokenCount);
        Assert.Equal(100, reply.Usage?.CachedInputTokenCount);
        var (path, body, bearer) = _openAi.Asked.Last();
        Assert.Equal("/v1/responses", path);
        Assert.Equal("at-1", bearer);
        var sent = JsonNode.Parse(body)!;
        Assert.True((bool)sent["stream"]!);
        Assert.False((bool)sent["store"]!);
        Assert.Equal("You are Hana.", (string?)sent["instructions"]);
        Assert.Equal("gpt-5.5", (string?)sent["model"]);
        foreach (var refused in new[] { "max_output_tokens", "temperature", "top_p", "previous_response_id", "background", "user", "truncation", "metadata" })
            Assert.Null(sent[refused]);
        var roles = sent["input"]!.AsArray().Select(i => (string?)i!["role"]).ToList();
        Assert.Equal(["user", "developer", "user"], roles);
        Assert.Equal(["print_memo"], sent["tools"]!.AsArray().Select(t => (string?)t!["name"]));
    }

    [Fact]
    public async Task A_token_about_to_lapse_is_refreshed_before_the_request_and_the_new_one_kept()
    {
        var plan = Plan();
        await SignIn(plan);
        _clock.Now = Noon.AddMinutes(57);

        await plan.Client("gpt-5.5").GetResponseAsync("Hello");

        var refresh = HttpUtility.ParseQueryString(_openAi.Asked.Single(a => a.Body.Contains("grant_type=refresh_token")).Body);
        Assert.Equal("rt-1", refresh["refresh_token"]);
        Assert.Equal("oaiapp_test", refresh["client_id"]);
        Assert.Equal("at-2", _openAi.Asked.Last().Bearer);
        Assert.Contains("rt-2", _kept);
        Assert.Equal("player@example.com", plan.Email);
    }

    [Fact]
    public async Task A_request_turned_down_for_its_token_is_asked_once_more_with_a_fresh_one()
    {
        var plan = Plan();
        await SignIn(plan);
        _openAi.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, """{"error":{"code":"token_expired","message":"expired"}}"""));

        var reply = await plan.Client("gpt-5.5").GetResponseAsync("Hello");

        Assert.Equal("Hi there.", reply.Text);
        Assert.Equal("at-2", _openAi.Asked.Last().Bearer);
        Assert.Null(plan.Stopped);
    }

    [Fact]
    public async Task The_limit_reached_is_told_once_then_she_waits_without_asking_and_tries_again_later_or_when_the_player_writes()
    {
        var plan = Plan();
        await SignIn(plan);
        var told = new List<PlanStop>();
        plan.Stopping += told.Add;
        var client = plan.Client("gpt-5.5");
        _openAi.Answers.Enqueue(Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"subscription_sharing_usage_limit_exceeded","message":"limit"}}"""));

        var stopped = await Assert.ThrowsAsync<PlanStopped>(() => client.GetResponseAsync("Hello"));
        var asked = _openAi.Asked.Count;
        var waiting = await Assert.ThrowsAsync<PlanStopped>(() => client.GetResponseAsync("Anyone?"));

        Assert.Equal(PlanStop.Limit, stopped.Why);
        Assert.True(stopped.Told);                                      // the card says it: her bubble needn't
        Assert.False(waiting.Told);                                     // a request while she waits: her bubble says it, plainly
        Assert.Equal("Usage limit reached: review your plan or this app's limit in ChatGPT settings (Manage usage, in Settings → Mind).", Trouble.Explain(stopped));
        Assert.Equal(asked, _openAi.Asked.Count);                       // waiting: nothing sent, not even retried
        Assert.Equal([PlanStop.Limit], told);
        Assert.True(plan.Connected);

        _clock.Now = Noon.AddMinutes(16);                               // a quarter of an hour on: tried again, and still reached
        _openAi.Answers.Enqueue(Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"subscription_sharing_usage_limit_exceeded","message":"limit"}}"""));
        await Assert.ThrowsAsync<PlanStopped>(() => client.GetResponseAsync("Hello?"));
        Assert.Equal(asked + 1, _openAi.Asked.Count);
        Assert.Equal([PlanStop.Limit], told);                           // not told again

        plan.TryAgain();                                                // the player writes to her
        Assert.Equal("Hi there.", (await client.GetResponseAsync("Hello")).Text);
        Assert.Null(plan.Stopped);
    }

    [Fact]
    public async Task The_limit_reached_part_way_through_a_reply_counts_as_reached()
    {
        var plan = Plan();
        await SignIn(plan);
        _openAi.Answers.Enqueue(Events(
            ("response.created", """{"type":"response.created","sequence_number":0,"response":{"id":"resp_2","object":"response","created_at":1790000000,"status":"in_progress","model":"gpt-5.5","output":[]}}"""),
            ("response.failed", """
                {"type":"response.failed","sequence_number":1,"response":{"id":"resp_2","object":"response","created_at":1790000000,"status":"failed","model":"gpt-5.5","output":[],
                 "error":{"code":"subscription_sharing_usage_limit_exceeded","message":"limit"}}}
                """)));

        var stopped = await Assert.ThrowsAsync<PlanStopped>(() => plan.Client("gpt-5.5").GetResponseAsync("Hello"));

        Assert.Equal(PlanStop.Limit, stopped.Why);
    }

    [Fact]
    public async Task Usage_that_couldnt_be_checked_is_passing_and_keeps_the_sign_in()
    {
        var plan = Plan();
        await SignIn(plan);
        var unavailable = """{"error":{"code":"subscription_sharing_usage_unavailable","message":"try later"}}""";
        _openAi.Answers.Enqueue(Json(HttpStatusCode.ServiceUnavailable, unavailable));
        _openAi.Answers.Enqueue(Json(HttpStatusCode.ServiceUnavailable, unavailable));

        var failed = await Assert.ThrowsAnyAsync<Exception>(() => plan.Client("gpt-5.5").GetResponseAsync("Hello"));

        Assert.IsNotType<PlanStopped>(failed);
        Assert.Equal("The provider is having trouble. Try again later.", Trouble.Explain(failed));
        Assert.Equal(2, _openAi.Paths.Count(p => p == "/v1/responses"));   // asked once more after a pause, no more
        Assert.Null(plan.Stopped);
        Assert.True(plan.Connected);
    }

    [Fact]
    public async Task An_account_that_cant_share_its_plan_stops_it_until_signed_in_again()
    {
        var plan = Plan();
        await SignIn(plan);
        _openAi.Answers.Enqueue(Json(HttpStatusCode.Forbidden, """{"error":{"code":"subscription_sharing_user_not_eligible","message":"no"}}"""));

        var stopped = await Assert.ThrowsAsync<PlanStopped>(() => plan.Client("gpt-5.5").GetResponseAsync("Hello"));
        _clock.Now = Noon.AddHours(2);

        Assert.Equal(PlanStop.NotEligible, stopped.Why);
        Assert.Equal(PlanStop.NotEligible, (await Assert.ThrowsAsync<PlanStopped>(() => plan.Client("gpt-5.5").GetResponseAsync("Hello"))).Why);
        await SignIn(plan);
        Assert.Null(plan.Stopped);
    }

    [Fact]
    public async Task A_refresh_turned_down_signs_the_player_out_and_forgets_the_tokens()
    {
        var plan = Plan();
        await SignIn(plan);
        var told = new List<PlanStop>();
        plan.Stopping += told.Add;
        _openAi.RefusesRefresh = true;
        _clock.Now = Noon.AddHours(2);

        var stopped = await Assert.ThrowsAsync<PlanStopped>(() => plan.Client("gpt-5.5").GetResponseAsync("Hello"));

        Assert.Equal(PlanStop.SignedOut, stopped.Why);
        Assert.Equal([PlanStop.SignedOut], told);
        Assert.False(plan.Connected);
        Assert.Null(_kept);
        Assert.DoesNotContain("/v1/responses", _openAi.Paths);
    }

    [Fact]
    public async Task A_refresh_turned_down_for_a_passing_reason_keeps_the_sign_in()
    {
        var plan = Plan();
        await SignIn(plan);
        _openAi.BusyRefreshing = true;
        _clock.Now = Noon.AddHours(2);

        var failed = await Assert.ThrowsAnyAsync<HttpRequestException>(() => plan.Client("gpt-5.5").GetResponseAsync("Hello"));

        Assert.Equal(HttpStatusCode.TooManyRequests, failed.StatusCode);
        Assert.True(plan.Connected);
        Assert.Null(plan.Stopped);
        Assert.Contains("rt-1", _kept);
        _openAi.BusyRefreshing = false;
        Assert.Equal("Hi there.", (await plan.Client("gpt-5.5").GetResponseAsync("Hello")).Text);
    }

    [Fact]
    public async Task A_fresh_token_turned_down_again_is_said_in_the_plans_words_and_keeps_the_sign_in()
    {
        var plan = Plan();
        await SignIn(plan);
        var refused = """{"error":{"code":"subscription_sharing_invalid_user","message":"no"}}""";
        _openAi.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, refused));
        _openAi.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, refused));

        var failed = await Assert.ThrowsAsync<PlanRefused>(() => plan.Client("gpt-5.5").GetResponseAsync("Hello"));

        Assert.StartsWith("ChatGPT turned this request down.", Trouble.Explain(failed));
        Assert.True(plan.Connected);                                    // a refresh token that still works is kept
        Assert.Null(plan.Stopped);
        Assert.Contains("rt-2", _kept);
    }

    [Fact]
    public async Task A_403_without_a_code_is_said_in_the_plans_words()
    {
        var plan = Plan();
        await SignIn(plan);
        _openAi.Answers.Enqueue(Json(HttpStatusCode.Forbidden, """{"detail":"region"}"""));

        var failed = await Assert.ThrowsAsync<PlanRefused>(() => plan.Client("gpt-5.5").GetResponseAsync("Hello"));

        Assert.DoesNotContain("API key", Trouble.Explain(failed));
        Assert.Null(plan.Stopped);
    }

    [Fact]
    public async Task Only_a_completed_response_is_a_reply()
    {
        var plan = Plan();
        await SignIn(plan);
        _openAi.Answers.Enqueue(Events(
            ("response.created", """{"type":"response.created","sequence_number":0,"response":{"id":"resp_3","object":"response","created_at":1790000000,"status":"in_progress","model":"gpt-5.5","output":[]}}"""),
            ("response.incomplete", """
                {"type":"response.incomplete","sequence_number":1,"response":{"id":"resp_3","object":"response","created_at":1790000000,"status":"incomplete","model":"gpt-5.5","output":[],
                 "incomplete_details":{"reason":"content_filter"}}}
                """)));

        await Assert.ThrowsAsync<HttpRequestException>(() => plan.Client("gpt-5.5").GetResponseAsync("Hello"));
    }

    [Fact]
    public async Task An_answer_for_another_registration_of_the_app_is_turned_down()
    {
        await SignIn(Plan());
        var plan = Plan();

        var failed = await Assert.ThrowsAsync<PlanSignInFailed>(() => SignIn(plan, client: "oaiapp_someone_else"));

        Assert.Equal("ChatGPT didn't finish registering the app. Try again.", failed.Message);
        Assert.Contains("oaiapp_test", File.ReadAllText(IdsFile));
        Assert.DoesNotContain("oaiapp_someone_else", File.ReadAllText(IdsFile));
    }

    [Fact]
    public async Task Disconnecting_ends_the_sign_in_at_chatgpt_and_forgets_it_here()
    {
        var plan = Plan();
        await SignIn(plan);
        var told = new List<PlanStop>();
        plan.Stopping += told.Add;

        Assert.True(await plan.Disconnect());

        var revoke = HttpUtility.ParseQueryString(_openAi.Asked.Last().Body);
        Assert.Equal("rt-1", revoke["token"]);
        Assert.Equal("refresh_token", revoke["token_type_hint"]);
        Assert.Equal("oaiapp_test", revoke["client_id"]);
        Assert.False(plan.Connected);
        Assert.Null(_kept);
        Assert.Contains("oaiapp_test", File.ReadAllText(IdsFile));      // signing in again registers nothing new
        Assert.Empty(told);                                             // their own choice: nothing to tell them

        await SignIn(plan);
        _openAi.Revokes = false;
        Assert.False(await plan.Disconnect());                          // ChatGPT couldn't be told: the player is asked to disconnect there
        Assert.False(plan.Connected);
    }
}
