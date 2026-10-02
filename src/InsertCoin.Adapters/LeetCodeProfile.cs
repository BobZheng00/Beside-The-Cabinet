namespace InsertCoin.Adapters;

/// <summary>Turns what a player types or pastes (a username or a leetcode.com profile link) into a username.</summary>
public static class LeetCodeProfile
{
    // First path segments that are site pages, not usernames.
    private static readonly HashSet<string> SitePages = new(StringComparer.OrdinalIgnoreCase)
    {
        "problems", "problemset", "contest", "discuss", "explore", "studyplan", "profile", "accounts", "submissions",
        "store", "subscribe", "tag", "company", "list", "progress", "points", "assessment", "interview", "u",
    };

    /// <summary>On failure, <paramref name="error"/> is a short message fit for the cabinet screen.</summary>
    public static bool TryParse(string? input, out string? username, out string? error)
    {
        username = null;
        error = null;
        var text = (input ?? "").Trim();
        if (text.Length == 0)
            return Fail("TYPE A NAME", out error);

        var looksLikeLink = text.Contains('/') || text.Contains("leetcode.", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeLink)
        {
            text = text.TrimStart('@');
            if (text.Any(char.IsWhiteSpace))
                return Fail("NO SPACES IN NAMES", out error);
            username = text;
            return true;
        }

        if (!Uri.TryCreate(text.Contains("://") ? text : "https://" + text, UriKind.Absolute, out var uri))
            return Fail("NOT A LEETCODE LINK", out error);
        var host = uri.Host.ToLowerInvariant();
        if (host is "leetcode.cn" or "www.leetcode.cn" || host.EndsWith(".leetcode.cn"))
            return Fail("LEETCODE.CN NOT SUPPORTED", out error);
        if (host is not ("leetcode.com" or "www.leetcode.com"))
            return Fail("NOT A LEETCODE LINK", out error);

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var candidate = segments switch
        {
            ["u", var name, ..] => name,
            [var name] when !SitePages.Contains(name) => name,
            _ => null,
        };
        if (candidate is null)
            return Fail("NOT A PROFILE LINK", out error);
        username = Uri.UnescapeDataString(candidate);
        return true;
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
