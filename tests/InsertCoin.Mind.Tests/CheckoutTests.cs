namespace InsertCoin.Mind.Tests;

/// <summary>Finding the main checkout from a worktree, on folders laid out as git lays them out.</summary>
public sealed class CheckoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "insert-coin-mind-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>A main checkout at main/, and a worktree of it at main/.claude/worktrees/one whose .git file points back.</summary>
    private (string Main, string Worktree) Layout()
    {
        var main = Path.Combine(_root, "main");
        Directory.CreateDirectory(Path.Combine(main, ".git", "worktrees", "one"));
        var worktree = Path.Combine(main, ".claude", "worktrees", "one");
        Directory.CreateDirectory(Path.Combine(worktree, "src", "InsertCoin.Game"));
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {Path.Combine(main, ".git", "worktrees", "one")}\n");
        return (main, worktree);
    }

    [Fact]
    public void A_worktree_folder_is_found_at_the_same_place_in_the_main_checkout()
    {
        var (main, worktree) = Layout();
        Assert.Equal(Path.Combine(main, "src", "InsertCoin.Game"), Checkout.InMain(Path.Combine(worktree, "src", "InsertCoin.Game")));
    }

    [Fact]
    public void A_relative_gitdir_is_read_from_the_worktree()
    {
        var (main, worktree) = Layout();
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../../../.git/worktrees/one");
        Assert.Equal(Path.Combine(main, "src"), Checkout.InMain(Path.Combine(worktree, "src")));
    }

    [Fact]
    public void The_main_checkout_borrows_from_nowhere()
    {
        var (main, _) = Layout();
        Assert.Null(Checkout.InMain(main));
    }

    [Fact]
    public void A_submodule_or_an_unreadable_git_file_borrows_from_nowhere()
    {
        var (_, worktree) = Layout();
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../../../.git/modules/one");
        Assert.Null(Checkout.InMain(worktree));
        File.WriteAllText(Path.Combine(worktree, ".git"), "not a git file");
        Assert.Null(Checkout.InMain(worktree));
    }

    [Fact]
    public void A_worktree_without_the_voice_program_borrows_the_main_checkouts()
    {
        var (main, worktree) = Layout();
        Directory.CreateDirectory(Path.Combine(main, "builds", "qwen-tts"));
        Assert.Equal(Path.Combine(main, "builds", "qwen-tts"), Checkout.Borrowing(Path.Combine(worktree, "src", "InsertCoin.Game", "..", "..", "builds", "qwen-tts")));
    }

    [Fact]
    public void A_worktree_with_its_own_voice_program_keeps_it()
    {
        var (main, worktree) = Layout();
        Directory.CreateDirectory(Path.Combine(main, "builds", "qwen-tts"));
        Directory.CreateDirectory(Path.Combine(worktree, "builds", "qwen-tts"));
        Assert.Equal(Path.Combine(worktree, "builds", "qwen-tts"), Checkout.Borrowing(Path.Combine(worktree, "builds", "qwen-tts")));
    }

    [Fact]
    public void Missing_from_both_it_stays_where_it_was_looked_for()
    {
        var (_, worktree) = Layout();
        Assert.Equal(Path.Combine(worktree, "builds", "qwen-tts"), Checkout.Borrowing(Path.Combine(worktree, "builds", "qwen-tts")));
    }

    [Fact]
    public void A_folder_outside_git_borrows_from_nowhere()
    {
        Directory.CreateDirectory(_root);
        Assert.Null(Checkout.InMain(_root));
    }
}
