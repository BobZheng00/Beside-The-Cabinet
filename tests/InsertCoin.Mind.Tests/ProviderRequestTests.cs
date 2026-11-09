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
