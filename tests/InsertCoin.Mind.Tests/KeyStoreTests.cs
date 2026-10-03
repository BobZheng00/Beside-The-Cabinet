namespace InsertCoin.Mind.Tests;

public class KeyStoreTests
{
    [Fact]
    public void A_key_is_kept_encrypted_and_can_be_forgotten()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var path = Path.Combine(Path.GetTempPath(), "insert-coin-mind-tests", Guid.NewGuid().ToString("N"), "key.bin");
        var keys = new KeyStore(path);
        Assert.Null(keys.Load());

        keys.Save("  sk-test-1234  ");

        Assert.True(keys.Has);
        Assert.Equal("sk-test-1234", keys.Load());
        Assert.DoesNotContain("sk-test", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        keys.Save(null);
        Assert.False(keys.Has);
        Assert.Null(keys.Load());
    }
}
