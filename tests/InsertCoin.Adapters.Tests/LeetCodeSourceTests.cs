using System.Net;
using System.Text;
using InsertCoin.Core;

namespace InsertCoin.Adapters.Tests;

public class LeetCodeSourceTests
{
    private const string Recent = """
        {"data":{"recentAcSubmissionList":[
          {"title":"LRU Cache","titleSlug":"lru-cache","timestamp":"1790000100"},
          {"title":"Two Sum","titleSlug":"two-sum","timestamp":"1790000000"}]}}
        """;

    // Recorded from leetcode.com for a username that does not exist.
    private const string NoSuchUser = """
        {"errors":[{"message":"That user does not exist.","locations":[{"line":1,"column":21}],"path":["matchedUser"],
        "extensions":{"handled":true,"skip_sentry":true}}],"data":{"matchedUser":null,"recentAcSubmissionList":[]}}
        """;

    private static string Question(string difficulty) => """{"data":{"question":{"difficulty":""" + $"\"{difficulty}\"" + "}}}";

    [Fact]
    public async Task Parses_recent_submissions_with_difficulty()
    {
        var http = new FakeLeetCode()
            .On("recentAcSubmissionList", Recent)
            .On("\"s\":\"lru-cache\"", Question("Medium"))
            .On("\"s\":\"two-sum\"", Question("Easy"));

        var tasks = await new LeetCodeSource(http.Client()).FetchRecentAsync("bob", default);

        Assert.Equal(2, tasks.Count);
        Assert.Equal(new SolvedTask("lru-cache", "LRU Cache", Difficulty.Medium, DateTimeOffset.FromUnixTimeSeconds(1790000100)), tasks[0]);
        Assert.Equal(Difficulty.Easy, tasks[1].Difficulty);
    }

    [Fact]
    public async Task Difficulty_is_looked_up_once_per_problem()
    {
        var http = new FakeLeetCode()
            .On("recentAcSubmissionList", Recent)
            .On("\"s\":\"lru-cache\"", Question("Medium"))
            .On("\"s\":\"two-sum\"", Question("Easy"));
        var source = new LeetCodeSource(http.Client());

        await source.FetchRecentAsync("bob", default);
        await source.FetchRecentAsync("bob", default);

        Assert.Equal(2, http.Requests.Count(r => r.Contains("question(")));
    }

    [Fact]
    public async Task Failed_difficulty_lookup_leaves_the_task_pending()
    {
        var http = new FakeLeetCode()
            .On("recentAcSubmissionList", Recent)
            .On("\"s\":\"lru-cache\"", """{"data":{"question":null}}""")
            .On("\"s\":\"two-sum\"", "boom", HttpStatusCode.BadGateway);

        var tasks = await new LeetCodeSource(http.Client()).FetchRecentAsync("bob", default);

        Assert.All(tasks, t => Assert.Null(t.Difficulty));
    }

    [Theory]
    [InlineData("oops", HttpStatusCode.InternalServerError)]
    [InlineData("<html>not json</html>", HttpStatusCode.OK)]
    [InlineData("""{"data":{"somethingElse":[]}}""", HttpStatusCode.OK)]
    [InlineData("""{"errors":[{"message":"rate limited"}]}""", HttpStatusCode.OK)]
    public async Task Outages_and_schema_changes_report_unavailable(string body, HttpStatusCode status)
    {
        var http = new FakeLeetCode().On("recentAcSubmissionList", body, status);

        await Assert.ThrowsAsync<TaskSourceUnavailableException>(
            () => new LeetCodeSource(http.Client()).FetchRecentAsync("bob", default));
    }

    [Fact]
    public async Task Player_exists_check()
    {
        var http = new FakeLeetCode()
            .On("\"u\":\"bob\"", """{"data":{"matchedUser":{"username":"bob"}}}""")
            .On("\"u\":\"ghost\"", NoSuchUser);
        var source = new LeetCodeSource(http.Client());

        Assert.True(await source.PlayerExistsAsync("bob", default));
        Assert.False(await source.PlayerExistsAsync("ghost", default));
    }

    private sealed class FakeLeetCode : HttpMessageHandler
    {
        private readonly List<(string Needle, string Body, HttpStatusCode Status)> _routes = [];
        public List<string> Requests { get; } = [];

        public FakeLeetCode On(string needle, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes.Add((needle, body, status));
            return this;
        }

        public HttpClient Client() => new(this);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(LeetCodeSource.Endpoint, request.RequestUri);
            var body = await request.Content!.ReadAsStringAsync(ct);
            Requests.Add(body);
            var route = _routes.First(r => body.Contains(r.Needle));
            return new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };
        }
    }
}
