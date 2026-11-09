using System.ClientModel;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;

namespace InsertCoin.Mind;

/// <summary>Why a request to the user's provider failed, in words she can say and the settings window can show.</summary>
public static class Trouble
{
    /// <summary>What she says when the provider sent back nothing at all, asked twice (R1): not left as silence.</summary>
    public const string Empty = "The provider sent back an empty answer. Try sending that again.";

    /// <param name="provider">Who was asked, when known: what counts as out of credit, and where to add it, depends on them.</param>
    public static string Explain(Exception e, Provider? provider = null) => e is PlanStopped or PlanRefused ? e.Message : Status(e) switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The API key was refused. Check it in Settings → Mind.",
        HttpStatusCode.NotFound => "The server doesn't know that model or address. Check them in Settings → Mind.",
        HttpStatusCode.PaymentRequired => "The provider says the account needs credit.",
        HttpStatusCode.TooManyRequests when OutOfCredit(e, provider) => provider?.Through == "api.openai.com"
            ? "This account has no API credit left. Add some on OpenAI's billing page. ChatGPT Plus doesn't include API use: to use your plan here, pick ChatGPT plan in Settings → Mind."
            : "This account has no API credit left. Add some on the provider's billing page.",
        HttpStatusCode.TooManyRequests => "The provider says to slow down. Try again in a minute.",
        >= HttpStatusCode.InternalServerError => "The provider is having trouble. Try again later.",
        { } code => $"The provider said no ({(int)code}).",
        null when e is TaskCanceledException or TimeoutException => "The provider took too long to answer.",
        null => "I can't reach the server. Check the address and your connection.",
    };

    /// <summary>The provider turned the request itself down (400, 413, 415, 422): something in it the model can't take, not a fault on the way.</summary>
    public static bool Refused(Exception e) =>
        Status(e) is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType or HttpStatusCode.UnprocessableEntity;

    /// <summary>Whether a 429 means the account has run out, which waiting won't mend, rather than a limit on how fast it may ask.</summary>
    private static bool OutOfCredit(Exception e, Provider? provider)
    {
        try
        {
            if (provider?.Protocol == Protocol.ChatGpt || Body(e) is not { } body || JsonNode.Parse(body)?["error"] is not JsonObject error)
                return false;
            // DashScope sends insufficient_quota for its per-minute limits too: only this line of its means used up
            if (provider?.Through.StartsWith("dashscope", StringComparison.OrdinalIgnoreCase) == true)
                return error["message"]?.ToString().Contains("free allocated quota exceeded", StringComparison.OrdinalIgnoreCase) == true;
            // the code first: OpenAI's type can say insufficient_quota for a spend limit the player set as well
            var code = error["code"]?.ToString();
            return (string.IsNullOrEmpty(code) ? error["type"]?.ToString() : code) is "insufficient_quota" or "credit_balance_exhausted" or "exceeded_current_quota_error";
        }
        catch (Exception x) when (x is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? Body(Exception e) => e switch
    {
        ClientResultException openAi => openAi.GetRawResponse()?.Content.ToString(),
        { InnerException: { } inner } => Body(inner),
        _ => null,
    };

    private static HttpStatusCode? Status(Exception e) => e switch
    {
        ClientResultException { Status: > 0 } openAi => (HttpStatusCode)openAi.Status,
        AnthropicApiException anthropic => anthropic.StatusCode,
        HttpRequestException { StatusCode: { } code } => code,
        { InnerException: { } inner } => Status(inner),
        _ => null,
    };
}
