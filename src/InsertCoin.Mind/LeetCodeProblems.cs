using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>
/// A LeetCode problem's public data, for her hints (the leetcode_problem tool): its statement, difficulty, topics and
/// LeetCode's own hints, found by a link, a number, a name, or a browser tab's title ("1. Two Sum - LeetCode",
/// "1. 两数之和 - 力扣（LeetCode）"). From leetcode.com, whose unofficial GraphQL answers without a sign-in; leetcode.cn's
/// turns programs away, so a problem from there is found by its number, which the two sites share.
/// </summary>
public sealed partial class LeetCodeProblems(HttpClient http)
{
    public static readonly Uri Endpoint = new("https://leetcode.com/graphql");
    private const int LongestStatement = 6_000;

    /// <summary>What she is told about the problem <paramref name="asked"/> means; a failure throws, saying why.</summary>
    public async Task<string> Describe(string asked, CancellationToken cancel = default)
    {
        var slug = await Find(asked, cancel) ?? throw new InvalidOperationException($"no LeetCode problem matches \"{asked.Trim()}\"");
        var data = await Query("query q($s:String!){question(titleSlug:$s){questionFrontendId title titleSlug difficulty isPaidOnly content hints topicTags{name}}}",
                               new { s = slug }, cancel);
        if (!data.TryGetProperty("question", out var question) || question.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"no LeetCode problem matches \"{asked.Trim()}\"");
        var text = new StringBuilder($"LeetCode {Text(question, "questionFrontendId")}. {Text(question, "title")} ({Text(question, "difficulty")})");
        var topics = question.TryGetProperty("topicTags", out var tags) && tags.ValueKind == JsonValueKind.Array
            ? tags.EnumerateArray().Select(t => Text(t, "name")).Where(n => n.Length > 0).ToList() : [];
        if (topics.Count > 0)
            text.Append($". Topics: {string.Join(", ", topics)}");
        text.Append($".\nAddress: https://leetcode.com/problems/{Text(question, "titleSlug")}/\n\n");
        var content = Text(question, "content");
        if (question.TryGetProperty("isPaidOnly", out var paid) && paid.ValueKind == JsonValueKind.True && content.Length == 0)
            text.Append("A premium problem: its statement needs the player's sign-in. Hint from its title and topics, or ask them to paste the statement.");
        else
        {
            var statement = HtmlText.From(content).Text;
            text.Append(statement.Length <= LongestStatement ? statement : statement[..LongestStatement] + "\n… (the rest is cut)");
        }
        var hints = question.TryGetProperty("hints", out var given) && given.ValueKind == JsonValueKind.Array
            ? given.EnumerateArray().Select(h => HtmlText.From(h.GetString() ?? "").Text).Where(h => h.Length > 0).ToList() : [];
        if (hints.Count > 0)
            text.Append("\n\nLeetCode's own hints, gentlest first:\n" + string.Join("\n", hints.Select((h, i) => $"{i + 1}. {h}")));
        return text.ToString();
    }

    /// <summary>
    /// The problem's slug (its name in its address): from a link as it is; from a number, or a tab's title with one, by
    /// that number; from a name, the problem with that title, or else the first that matches it.
    /// </summary>
    public async Task<string?> Find(string asked, CancellationToken cancel = default)
    {
        asked = asked.Trim();
        if (Link().Match(asked) is { Success: true } link)
            return link.Groups[1].Value.ToLowerInvariant();
        var titled = Titled().Match(asked);
        var number = titled.Success ? titled.Groups[1].Value : Number().IsMatch(asked) ? asked : null;
        var name = titled.Success ? titled.Groups[2].Value.Trim() : asked;
        var found = await Search(number ?? name, cancel);
        return number is not null
            ? found.Where(q => q.Number == number).Select(q => q.Slug).FirstOrDefault()
            : found.Where(q => string.Equals(q.Title, name, StringComparison.OrdinalIgnoreCase)).Concat(found).Select(q => q.Slug).FirstOrDefault();
    }

    private async Task<List<(string Number, string Title, string Slug)>> Search(string keywords, CancellationToken cancel)
    {
        var data = await Query("query q($k:String){questionList(categorySlug:\"\",limit:20,skip:0,filters:{searchKeywords:$k}){data{questionFrontendId title titleSlug}}}",
                               new { k = keywords }, cancel);
        return data.TryGetProperty("questionList", out var list) && list.ValueKind == JsonValueKind.Object
               && list.TryGetProperty("data", out var questions) && questions.ValueKind == JsonValueKind.Array
            ? [.. questions.EnumerateArray().Select(q => (Text(q, "questionFrontendId"), Text(q, "title"), Text(q, "titleSlug")))]
            : [];
    }

    private async Task<JsonElement> Query(string query, object variables, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(new { query, variables }) };
        request.Headers.Referrer = new Uri("https://leetcode.com/problemset/");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) InsertCoin/1.0");
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancel);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"LeetCode couldn't be reached: {e.Message}");
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"LeetCode answered {(int)response.StatusCode} {response.ReasonPhrase}");
            try
            {
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);
                return json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                    ? data.Clone() : throw new InvalidOperationException("LeetCode's answer had nothing in it");
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("LeetCode's answer wasn't readable");
            }
        }
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";

    [GeneratedRegex(@"leetcode\.(?:com|cn)/problems/([a-z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Link();
    // "1. Two Sum - LeetCode", "1. 两数之和 - 力扣（LeetCode）", "1. Two Sum"
    [GeneratedRegex(@"^(\d+)\.\s*(.+?)(?:\s+-\s+.*)?$")]
    private static partial Regex Titled();
    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Number();
}
