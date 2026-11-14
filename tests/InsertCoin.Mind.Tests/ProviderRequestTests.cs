using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>What actually goes to an OpenAI-compatible server, read from the request a stand-in receives.</summary>
public class ProviderRequestTests
{
    private const string Reply = """{"id":"r","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"Hi"},"finish_reason":"stop"}]}""";

    [Theory]
    [InlineData("gpt-6-luna", true, "none")]
    [InlineData("openai/gpt-6-luna", true, "none")]
    [InlineData("gpt-6-luna", false, null)]
    [InlineData("openai/gpt-6-luna", false, null)]
    [InlineData("deepseek-chat", true, null)]
    [InlineData("qwen-plus", true, null)]
    [InlineData("a-local-model", true, null)]
    public async Task Only_a_request_offering_tools_to_a_model_that_needs_it_turns_reasoning_off(string model, bool tools, string? effort)
    {
        var request = await Sent(model, tools);

        Assert.Equal(tools, request.TryGetProperty("tools", out _));
        Assert.Equal(effort, request.TryGetProperty("reasoning_effort", out var sent) ? sent.GetString() : null);
    }

    [Theory]
    [InlineData("deepseek-flash", true, true)]
    [InlineData("deepseek-flash", false, true)]
    [InlineData("deepseek-chat", true, false)]
    [InlineData("qwen-plus", true, false)]
    [InlineData("gpt-6-luna", true, false)]
    public async Task Only_DeepSeeks_small_model_is_told_not_to_think(string model, bool tools, bool told)
    {
        var request = await Sent(model, tools);

        Assert.Equal(tools, request.TryGetProperty("tools", out _));
        Assert.Equal(told ? "disabled" : null, request.TryGetProperty("thinking", out var thinking) ? thinking.GetProperty("type").GetString() : null);
    }

    private const string Answered = """{"id":"resp_1","object":"response","created_at":1,"status":"completed","model":"m","output":[{"type":"message","id":"msg_1","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Hi","annotations":[]}]}],"usage":{"input_tokens":10,"output_tokens":2,"total_tokens":12}}""";

    [Fact]
    public async Task OpenAI_with_a_key_is_asked_through_its_Responses_API_unstored_with_its_web_search_and_reasoning_on()
    {
        var (asked, reply) = await AskOpenAi("gpt-6-luna", (200, Answered));

        Assert.Equal("Hi", reply);
        var (path, request) = asked.Single();
        Assert.Equal("/v1/responses", path);
        Assert.False(request.GetProperty("store").GetBoolean());
        Assert.Equal(["web_search_call.action.sources", "reasoning.encrypted_content"], request.GetProperty("include").EnumerateArray().Select(i => i.GetString()));
        Assert.Equal(["print_memo", "web_search"], request.GetProperty("tools").EnumerateArray()
                                                           .Select(t => t.TryGetProperty("name", out var name) ? name.GetString() : t.GetProperty("type").GetString()));
        Assert.False(request.TryGetProperty("reasoning", out var reasoning) && reasoning.TryGetProperty("effort", out _));
    }

    [Fact]
    public async Task A_model_that_turns_encrypted_reasoning_down_is_asked_again_without_it_and_never_again_with_it()
    {
        const string refused = """{"error":{"message":"Encrypted content is not supported with this model.","type":"invalid_request_error","param":"include","code":null}}""";

        var (asked, reply) = await AskOpenAi("gpt-4.1", (400, refused), (200, Answered), (200, Answered));

        Assert.Equal("Hi", reply);
        Assert.Equal([true, false, false], asked.Select(a => a.Request.GetProperty("include").EnumerateArray().Any(i => i.GetString() == "reasoning.encrypted_content")));
    }

    /// <summary>
    /// Asks OpenAI with a key, at a stand-in, for two streamed replies (one when the first request is turned down),
    /// answering each request in turn with a whole response, as the Responses API sends one.
    /// </summary>
    private static async Task<(List<(string Path, JsonElement Request)> Asked, string Reply)> AskOpenAi(string model, params (int Status, string Body)[] answers)
    {
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        using var server = new HttpListener { Prefixes = { $"http://localhost:{port}/" } };
        server.Start();
        var asked = new List<(string, JsonElement)>();
        var serving = Task.Run(async () =>
        {
            foreach (var (status, body) in answers)
            {
                var context = await server.GetContextAsync();
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                asked.Add((context.Request.Url!.AbsolutePath, JsonDocument.Parse(await reader.ReadToEndAsync()).RootElement.Clone()));
                var answer = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(answer);
                context.Response.Close();
            }
        });

        Provider.OpenAiAt = $"http://localhost:{port}/v1";
        using var client = new Provider(Protocol.OpenAi, Provider.OpenAiApi, model).Connect("key");
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => "done", "print_memo"), new HostedWebSearchTool()] };
        var reply = await client.GetResponseAsync("Hello", options);
        if (answers.Length > 2)
            await client.GetResponseAsync("And?", options);
        await serving;
        return (asked, reply.Text);
    }

    /// <summary>The body of the one request a provider at a stand-in sends for one reply.</summary>
    private static async Task<JsonElement> Sent(string model, bool tools)
    {
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        using var server = new HttpListener { Prefixes = { $"http://localhost:{port}/" } };
        server.Start();
        var received = Answer(server);

        using var client = new Provider(Protocol.OpenAi, $"http://localhost:{port}/v1", model).Connect("key");
        await client.GetResponseAsync("Hello", tools ? new ChatOptions { Tools = [AIFunctionFactory.Create(() => "done", "print_memo")] } : new ChatOptions());

        return await received;
    }

    private static async Task<JsonElement> Answer(HttpListener server)
    {
        var context = await server.GetContextAsync();
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var answer = Encoding.UTF8.GetBytes(Reply);
        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(answer);
        context.Response.Close();
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
