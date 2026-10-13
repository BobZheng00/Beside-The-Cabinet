using System.Text.Json;
using System.Text.Json.Serialization;

namespace InsertCoin.Mind;

/// <summary>
/// Something on its way out of the cabinet: a title ("Reminder", "Missed reminder") and what it says; or, with
/// <paramref name="Hers"/>, a text from her in her own words, with her name as its title.
/// </summary>
public sealed record Notice(string Title, string Text, bool Hers = false);

/// <summary>
/// Where her texts go while the player is away from the PC, and reminders too while they are at it if they choose
/// (see <see cref="Reaching"/>); each off until the player turns it on: an <see href="https://ntfy.sh">ntfy</see> topic
/// (phone push, free, no account), a Discord webhook, a Telegram bot, or any webhook (a JSON POST). The addresses and
/// tokens are <see cref="ElsewhereSecrets"/>, kept apart, encrypted.
/// </summary>
public sealed record Elsewhere
{
    public bool Ntfy { get; init; }
    public string NtfyServer { get; init; } = "https://ntfy.sh";
    public bool Discord { get; init; }
    public bool Telegram { get; init; }
    public bool Webhook { get; init; }

    [JsonIgnore]
    public bool Any => Ntfy || Discord || Telegram || Webhook;
}

/// <summary>What <see cref="Elsewhere"/> needs that nobody else should see: kept encrypted (see <see cref="KeyStore"/>), never in settings.json.</summary>
public sealed record ElsewhereSecrets
{
    public string? NtfyTopic { get; init; }
    public string? NtfyToken { get; init; }
    public string? DiscordWebhook { get; init; }
    public string? TelegramToken { get; init; }
    public string? TelegramChat { get; init; }
    public string? WebhookUrl { get; init; }

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ElsewhereSecrets FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ElsewhereSecrets();
        try
        {
            return JsonSerializer.Deserialize<ElsewhereSecrets>(json, Json) ?? new ElsewhereSecrets();
        }
        catch (JsonException)
        {
            return new ElsewhereSecrets();
        }
    }
}

/// <summary>One delivery: where it goes, what is posted (as JSON), and a bearer token if the place needs one.</summary>
public sealed record Parcel(string Name, string Url, object Body, string? Bearer = null)
{
    public string BodyJson => JsonSerializer.Serialize(Body);
}

/// <summary>Sends a <see cref="Notice"/> to the places the player chose; each place's failure is reported, none stops the others.</summary>
public static class Couriers
{
    /// <summary>What goes to each place that is on and has what it needs. A text of hers comes as a message from her, a notice with an alarm clock.</summary>
    public static IEnumerable<Parcel> Parcels(Elsewhere where, ElsewhereSecrets secrets, Notice notice)
    {
        var mark = notice.Hers ? "" : "⏰ ";
        if (where.Ntfy && secrets.NtfyTopic is { Length: > 0 } topic)
            // JSON publishing, so the title and text can be in any language (headers would have to be ASCII)
            yield return new Parcel("ntfy", where.NtfyServer.Trim().TrimEnd('/') + "/",
                                    new
                                    {
                                        topic = topic.Trim(), title = notice.Title, message = notice.Text,
                                        tags = new[] { notice.Hers ? "speech_balloon" : "alarm_clock" }, priority = 4,
                                    },
                                    secrets.NtfyToken is { Length: > 0 } token ? token.Trim() : null);
        if (where.Discord && secrets.DiscordWebhook is { Length: > 0 } discord)
            yield return new Parcel("Discord", discord.Trim(),
                                    new { content = $"{mark}**{notice.Title}**: {notice.Text}", allowed_mentions = new { parse = Array.Empty<string>() } });
        if (where.Telegram && secrets.TelegramToken is { Length: > 0 } bot && secrets.TelegramChat is { Length: > 0 } chat)
            // her own bot: a text of hers needs no name in front
            yield return new Parcel("Telegram", $"{Telegram.Server}/bot{bot.Trim()}/sendMessage",
                                    new { chat_id = chat.Trim(), text = notice.Hers ? notice.Text : $"{mark}{notice.Title}: {notice.Text}" });
        if (where.Webhook && secrets.WebhookUrl is { Length: > 0 } hook)
            yield return new Parcel("webhook", hook.Trim(), new { app = "Beside the Cabinet", title = notice.Title, message = notice.Text });
    }

    /// <summary>Sends to every place that is on; returns each place's name and its error, or null when it got there.</summary>
    public static async Task<IReadOnlyList<(string Name, string? Error)>> Send(HttpClient http, Elsewhere where, ElsewhereSecrets secrets, Notice notice,
                                                                                 CancellationToken cancel = default)
    {
        var sent = Parcels(where, secrets, notice).Select(async parcel =>
        {
            if (!Uri.TryCreate(parcel.Url, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http"))
                return (parcel.Name, (string?)"that address is not a web address (https://…)");
            try
            {
                // buffered, with a length: small webhook receivers often cannot read a chunked body
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(parcel.BodyJson, System.Text.Encoding.UTF8, "application/json") };
                if (parcel.Bearer is { } bearer)
                    request.Headers.Authorization = new("Bearer", bearer);
                using var response = await http.SendAsync(request, cancel);
                return (parcel.Name, response.IsSuccessStatusCode ? null : $"{(int)response.StatusCode} {response.ReasonPhrase}");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException)
            {
                return (parcel.Name, (string?)e.GetBaseException().Message);
            }
        });
        return await Task.WhenAll(sent);
    }
}
