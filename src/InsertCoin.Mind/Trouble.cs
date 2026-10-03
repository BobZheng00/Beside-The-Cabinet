using System.ClientModel;
using System.Net;
using Anthropic.Exceptions;

namespace InsertCoin.Mind;

/// <summary>Why a request to the user's provider failed, in words she can say and the settings window can show.</summary>
public static class Trouble
{
    public static string Explain(Exception e) => Status(e) switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The API key was refused. Check it in Chat settings.",
        HttpStatusCode.NotFound => "The server doesn't know that model or address. Check them in Chat settings.",
        HttpStatusCode.PaymentRequired => "The provider says the account needs credit.",
        HttpStatusCode.TooManyRequests => "The provider says to slow down. Try again in a minute.",
        >= HttpStatusCode.InternalServerError => "The provider is having trouble. Try again later.",
        { } code => $"The provider said no ({(int)code}).",
        null when e is TaskCanceledException or TimeoutException => "The provider took too long to answer.",
        null => "I can't reach the server. Check the address and your connection.",
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
