using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InsertCoin.Core;

namespace InsertCoin.Adapters;

/// <summary>
/// LeetCode's public (unofficial, undocumented) GraphQL endpoint. Any HTTP or schema failure throws
/// <see cref="TaskSourceUnavailableException"/> so the app can show LINK DOWN and back off.
/// </summary>
public sealed class LeetCodeSource(HttpClient http) : ITaskSource
{
    public static readonly Uri Endpoint = new("https://leetcode.com/graphql");
    private const int RecentLimit = 20;

    private readonly ConcurrentDictionary<string, Difficulty> _difficulties = new();

    public async Task<IReadOnlyList<SolvedTask>> FetchRecentAsync(string username, CancellationToken ct)
    {
        var data = await QueryAsync(
            "query q($u:String!,$n:Int!){recentAcSubmissionList(username:$u,limit:$n){title titleSlug timestamp}}",
            new { u = username, n = RecentLimit }, ct);

        if (!data.TryGetProperty("recentAcSubmissionList", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new TaskSourceUnavailableException("recentAcSubmissionList missing from response");

        var tasks = new List<SolvedTask>();
        foreach (var item in list.EnumerateArray())
        {
            var submission = item.Deserialize<Submission>()
                ?? throw new TaskSourceUnavailableException("empty submission");
            if (!long.TryParse(submission.Timestamp, out var seconds))
                throw new TaskSourceUnavailableException($"bad timestamp '{submission.Timestamp}'");
            var difficulty = await DifficultyOrNullAsync(submission.TitleSlug, ct);
            tasks.Add(new SolvedTask(submission.TitleSlug, submission.Title, difficulty,
                DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime()));
        }
        return tasks;
    }

    public async Task<bool> PlayerExistsAsync(string username, CancellationToken ct)
    {
        var data = await QueryAsync("query q($u:String!){matchedUser(username:$u){username}}", new { u = username }, ct);
        return data.TryGetProperty("matchedUser", out var user) && user.ValueKind == JsonValueKind.Object;
    }

    private async Task<Difficulty?> DifficultyOrNullAsync(string slug, CancellationToken ct)
    {
        if (_difficulties.TryGetValue(slug, out var known))
            return known;
        try
        {
            var data = await QueryAsync("query q($s:String!){question(titleSlug:$s){difficulty}}", new { s = slug }, ct);
            var text = data.GetProperty("question").GetProperty("difficulty").GetString();
            if (!Enum.TryParse<Difficulty>(text, ignoreCase: true, out var difficulty))
                return null;
            _difficulties[slug] = difficulty;
            return difficulty;
        }
        catch (Exception e) when (e is TaskSourceUnavailableException or KeyNotFoundException or InvalidOperationException)
        {
            return null; // counted later, once the lookup succeeds
        }
    }

    private async Task<JsonElement> QueryAsync(string query, object variables, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(new { query, variables }),
            };
            request.Headers.Referrer = new Uri("https://leetcode.com");
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new TaskSourceUnavailableException("response has no data");
            return data.Clone();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TaskSourceUnavailableException(e.Message, e);
        }
    }

    private sealed record Submission(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("titleSlug")] string TitleSlug,
        [property: JsonPropertyName("timestamp")] string Timestamp);
}

public sealed class TaskSourceUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
