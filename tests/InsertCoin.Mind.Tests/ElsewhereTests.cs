using System.Net;

namespace InsertCoin.Mind.Tests;

/// <summary>Reminders sent out of the cabinet: ntfy, Discord, Telegram and any webhook, each only when on and set up.</summary>
public class ElsewhereTests
{
    private static readonly Notice Call = new("Reminder", "21:00 给妈妈打电话");
    private static readonly ElsewhereSecrets Secrets = new()
    {
        NtfyTopic = " bob-reminders ", NtfyToken = "tk_abc", DiscordWebhook = "https://discord.com/api/webhooks/1/x",
        TelegramToken = "123:ABC", TelegramChat = "42", WebhookUrl = "https://example.org/hook",
    };

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpStatusCode> answer) : HttpMessageHandler
    {
        public List<(string Url, string? Auth, string Body)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(answer(request));
        }
    }

    [Fact]
    public void Each_place_gets_what_it_expects_and_only_when_it_is_on()
    {
        Assert.Empty(Couriers.Parcels(new Elsewhere(), Secrets, Call));
        Assert.Empty(Couriers.Parcels(new Elsewhere { Ntfy = true, Telegram = true }, new ElsewhereSecrets { TelegramToken = "123:ABC" }, Call));

        var parcels = Couriers.Parcels(new Elsewhere { Ntfy = true, Discord = true, Telegram = true, Webhook = true, NtfyServer = "https://ntfy.example/" },
                                       Secrets, Call).ToDictionary(p => p.Name);

        Assert.Equal(("https://ntfy.example/", "tk_abc"), (parcels["ntfy"].Url, parcels["ntfy"].Bearer));
        Assert.Contains("\"topic\":\"bob-reminders\"", parcels["ntfy"].BodyJson);
        Assert.Contains("\"tags\":[\"alarm_clock\"]", parcels["ntfy"].BodyJson);
        Assert.Contains("\"allowed_mentions\":{\"parse\":[]}", parcels["Discord"].BodyJson);       // never pings anyone
        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", parcels["Telegram"].Url);
        Assert.Contains("\"chat_id\":\"42\"", parcels["Telegram"].BodyJson);
        Assert.Contains("\"app\":\"INSERT COIN\"", parcels["webhook"].BodyJson);
    }

    [Fact]
    public async Task Sending_reports_each_place_and_one_failing_does_not_stop_the_others()
    {
        var http = new FakeHttp(r => r.RequestUri!.Host == "discord.com" ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        var where = new Elsewhere { Ntfy = true, Discord = true, Webhook = true };

        var results = await Couriers.Send(new HttpClient(http), where, Secrets with { WebhookUrl = "not a web address" }, Call);

        Assert.Equal([("ntfy", null), ("Discord", "404 Not Found"), ("webhook", "that address is not a web address (https://…)")],
                     results.Select(r => (r.Name, r.Error)));
        var ntfy = http.Sent.Single(s => s.Url.StartsWith("https://ntfy.sh"));
        Assert.Equal("Bearer tk_abc", ntfy.Auth);
        Assert.Contains("给妈妈打电话", System.Text.RegularExpressions.Regex.Unescape(ntfy.Body));     // any language, in the JSON body
    }

    [Fact]
    public void Secrets_survive_a_round_trip_and_bad_json_means_none()
    {
        Assert.Equal(Secrets, ElsewhereSecrets.FromJson(Secrets.ToJson()));
        Assert.DoesNotContain("null", new ElsewhereSecrets { NtfyTopic = "t" }.ToJson());
        Assert.Equal(new ElsewhereSecrets(), ElsewhereSecrets.FromJson("{oops"));
        Assert.Equal(new ElsewhereSecrets(), ElsewhereSecrets.FromJson(null));
    }
}
