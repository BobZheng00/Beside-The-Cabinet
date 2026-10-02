namespace InsertCoin.Adapters.Tests;

public class LeetCodeProfileTests
{
    [Theory]
    [InlineData("BobSmith", "BobSmith")]
    [InlineData("  BobSmith  ", "BobSmith")]
    [InlineData("@BobSmith", "BobSmith")]
    [InlineData("https://leetcode.com/u/BobSmith/", "BobSmith")]
    [InlineData("https://leetcode.com/u/BobSmith", "BobSmith")]
    [InlineData("leetcode.com/u/BobSmith/", "BobSmith")]
    [InlineData("https://www.leetcode.com/BobSmith/", "BobSmith")]
    [InlineData("http://leetcode.com/BobSmith?tab=recent#top", "BobSmith")]
    [InlineData("https://leetcode.com/profile/account/BobSmith", null)]
    [InlineData("user_name-123", "user_name-123")]
    public void Extracts_the_username(string input, string? expected)
    {
        var ok = LeetCodeProfile.TryParse(input, out var username, out _);

        Assert.Equal(expected is not null, ok);
        Assert.Equal(expected, username);
    }

    [Theory]
    [InlineData("", "TYPE A NAME")]
    [InlineData("   ", "TYPE A NAME")]
    [InlineData("https://leetcode.cn/u/someone/", "LEETCODE.CN NOT SUPPORTED")]
    [InlineData("https://github.com/BobSmith", "NOT A LEETCODE LINK")]
    [InlineData("https://leetcode.com/problems/two-sum/", "NOT A PROFILE LINK")]
    [InlineData("https://leetcode.com/", "NOT A PROFILE LINK")]
    [InlineData("bob smith", "NO SPACES IN NAMES")]
    public void Explains_what_is_wrong(string input, string expectedError)
    {
        Assert.False(LeetCodeProfile.TryParse(input, out var username, out var error));
        Assert.Null(username);
        Assert.Equal(expectedError, error);
    }
}
