using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using OpenAI;

namespace InsertCoin.Mind.Tests;

/// <summary>
/// What the player is told when a provider answers 429: an account out of credit is told so, a plain rate limit is
/// told to slow down. The bodies are written from each provider's public docs and go through the real client.
/// </summary>
public class TroubleTests
{
    private const string SlowDown = "The provider says to slow down. Try again in a minute.";
    private const string NoCredit = "This account has no API credit left. Add some on the provider's billing page.";
    private const string CreditBalanceExhausted = """{"error":{"message":"Your organization has no prepaid credits remaining.","type":"insufficient_quota","param":null,"code":"credit_balance_exhausted"}}""";
    private const string InsufficientQuota = """{"error":{"message":"You exceeded your current quota, please check your plan and billing details.","type":"insufficient_quota","param":null,"code":"insufficient_quota"}}""";
    private const string RateLimit = """{"error":{"message":"Rate limit reached for requests","type":"requests","param":null,"code":"rate_limit_exceeded"}}""";

    private static readonly Provider OpenAi = new(Protocol.OpenAi, "https://api.openai.com/v1", "a-model");
    private static readonly Provider Other = new(Protocol.OpenAi, "https://models.example.com/v1", "a-model");
    private static readonly Provider DashScope = new(Protocol.OpenAi, "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus");

    private sealed class TooMany(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static async Task<string> Told(string body, Provider? provider, bool streamed = false, bool onPlan = false)
    {
        var client = new OpenAIClient(new ApiKeyCredential("not-a-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://models.example.com/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(new TooMany(body))),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        }).GetChatClient("a-model").AsIChatClient();

        var failed = await Assert.ThrowsAsync<ClientResultException>(async () =>
        {
            if (!streamed)
                await client.GetResponseAsync("Hello");
            else
                await foreach (var _ in client.GetStreamingResponseAsync("Hello")) { }
        });
        return Trouble.Explain(failed, provider, onPlan);
    }

    [Theory]
    [InlineData(CreditBalanceExhausted, false)]
    [InlineData(CreditBalanceExhausted, true)]
    [InlineData(InsufficientQuota, false)]
    [InlineData(InsufficientQuota, true)]
    public async Task An_OpenAI_account_out_of_credit_is_told_so_and_where_ChatGPT_Plus_stands(string body, bool streamed)
    {
        var told = await Told(body, OpenAi, streamed);

        Assert.StartsWith("This account has no API credit left. Add some on OpenAI's billing page.", told);
        Assert.Contains("ChatGPT Plus doesn't include API use", told);
        Assert.Contains("pick ChatGPT plan", told);
    }

    [Fact]
    public async Task An_OpenAI_backup_out_of_credit_doesnt_tell_someone_on_the_plan_to_pick_the_plan()
    {
        var told = await Told(CreditBalanceExhausted, OpenAi, onPlan: true);

        Assert.Equal("This account has no API credit left. Add some on OpenAI's billing page. ChatGPT Plus doesn't include API use.", told);
    }

    [Fact]
    public async Task Another_provider_out_of_credit_is_told_so_with_no_word_of_ChatGPT() =>
        Assert.Equal(NoCredit, await Told(InsufficientQuota, Other));

    [Theory]
    [InlineData("insufficient_quota")]
    [InlineData("exceeded_current_quota_error")]
    public async Task A_body_with_only_a_type_saying_the_quota_is_used_up_counts(string type) =>
        Assert.Equal(NoCredit, await Told($$$"""{"error":{"message":"No quota.","type":"{{{type}}}"}}""", Other));

    [Theory]
    [InlineData(RateLimit)]
    [InlineData("""{"error":{"message":"Your request rate increased too quickly.","type":"rate_limit_error","code":"slow_down"}}""")]
    [InlineData("""{"error":{"message":"Your organization reached its enforced spend limit.","type":"insufficient_quota","code":"organization_spend_limit_exceeded"}}""")]
    [InlineData("""{"error":{"code":429,"message":"You are being rate limited"}}""")]
    [InlineData("<html>Too many requests</html>")]
    [InlineData("")]
    public async Task A_rate_limit_keeps_slow_down(string body) => Assert.Equal(SlowDown, await Told(body, OpenAi));

    [Theory]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1")]
    [InlineData("https://dashscope-intl.aliyuncs.com/compatible-mode/v1")]
    [InlineData("https://coding.dashscope.aliyuncs.com/v1")]
    [InlineData("https://coding-intl.dashscope.aliyuncs.com/v1")]
    public async Task DashScopes_per_minute_limit_is_a_rate_limit_though_it_says_insufficient_quota(string server) =>
        Assert.Equal(SlowDown, await Told(InsufficientQuota, new Provider(Protocol.OpenAi, server, "qwen-plus")));

    [Fact]
    public async Task DashScopes_free_quota_used_up_says_to_pick_another_model() =>
        Assert.Equal("This model's free quota is used up. Pick another model in Settings → AI model.",
                     await Told("""{"error":{"message":"Free allocated quota exceeded.","type":"insufficient_quota","code":"insufficient_quota"}}""", DashScope));

    [Fact]
    public async Task The_ChatGPT_plan_is_never_told_to_buy_API_credit() =>
        Assert.Equal(SlowDown, await Told(InsufficientQuota, new Provider(Protocol.ChatGpt, ChatGptPlan.Api, "a-model")));

    [Fact]
    public async Task Without_knowing_the_provider_it_still_says_no_credit() =>
        Assert.Equal(NoCredit, await Told(InsufficientQuota, null));
}
