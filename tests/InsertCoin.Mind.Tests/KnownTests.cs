namespace InsertCoin.Mind.Tests;

/// <summary>What she always has in mind: listed from her facts for every reply, never a note the model rewrites.</summary>
public class KnownTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));

    private static Fact F(long id, string text, int importance, bool pinned = false, int daysAgo = 0) =>
        new(id, MindStore.Shared, text, importance, Friday.AddDays(-daysAgo), null, pinned);

    [Fact]
    public void Her_facts_are_listed_after_the_players_own_words_pinned_first_then_by_importance_and_the_newest_while_they_fit()
    {
        Fact[] facts = [F(1, "The player likes tea.", 4), F(2, "The player works at Acme.", 7, daysAgo: 3), F(3, "The player's name is Bob.", 5, pinned: true),
                        F(4, "The player is training for a marathon.", 7, daysAgo: 1), F(5, "The player wants to move abroad.", 9, daysAgo: 9)];

        Assert.Equal("In the player's own words: Call me Bob.\n- The player's name is Bob.\n- The player wants to move abroad.\n" +
                     "- The player is training for a marathon.\n- The player works at Acme.",
                     Prompt.Known("Call me Bob.", facts, [], Friday));
        Assert.Equal("", Prompt.Known(" ", [F(1, "The player likes tea.", 4)], [], Friday));       // nothing important yet

        var many = Enumerable.Range(1, 200).Select(i => F(i, $"The player remembers thing number {i}.", 6)).ToList();
        Assert.InRange(Prompt.Known("", many, [], Friday).Length, Prompt.KnownLimit - 60, Prompt.KnownLimit);
    }

    [Fact]
    public void A_fact_that_tells_a_time_from_when_it_was_said_says_when_that_was()
    {
        Fact[] facts = [F(1, "The player began learning guitar about five years ago.", 7, daysAgo: 30), F(2, "The player drinks green tea.", 7)];

        Assert.Equal("- The player drinks green tea.\n- The player began learning guitar about five years ago. (as of 2 Sep 2026)",
                     Prompt.Known("", facts, [], Friday));
    }

    [Fact]
    public void A_change_shows_as_one_for_two_weeks_and_an_important_fact_that_ended_says_so()
    {
        var toronto = F(2, "The player lives in Toronto.", 8, daysAgo: 3);
        Fact[] retired =
        [
            F(1, "The player lives in Montreal.", 8, daysAgo: 90) with { Retired = Friday.AddDays(-3), Why = $"{Conversation.Changed} The player lives in Toronto." },
            F(3, "The player is dating Sam.", 7, daysAgo: 60) with { Retired = Friday.AddDays(-2), Why = Conversation.Ended },
            F(4, "The player drinks green tea.", 4, daysAgo: 60) with { Retired = Friday.AddDays(-2), Why = Conversation.Ended },
        ];

        Assert.Equal("- The player lives in Toronto. (Since 29 Sep; before: The player lives in Montreal.)\n" +
                     "- No longer so since 30 Sep: The player is dating Sam.",
                     Prompt.Known("", [toronto], retired, Friday));
        Assert.Equal("- The player lives in Toronto.", Prompt.Known("", [toronto], retired, Friday.AddDays(15)));     // old news by then
    }

    [Fact]
    public void The_notes_the_model_rewrote_go_into_her_log_out_of_view_and_the_notes_become_the_players_own()
    {
        using var temp = new TempStore();
        temp.Store.Add("shino", Speaker.User, "hi", Friday);
        temp.Store.SetNote(MindStore.Shared, MindStore.AboutUser, "Bob, who loves Hades.");
        temp.Store.SetNote("shino", MindStore.Us, "Friends.");
        using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={temp.Location};Pooling=False"))
        {
            old.Open();
            using var back = old.CreateCommand();
            back.CommandText = "PRAGMA user_version = 13";              // as it was while the model rewrote them
            back.ExecuteNonQuery();
        }

        using (new MindStore(temp.Location)) { }

        Assert.Equal("", temp.Store.Note(MindStore.Shared, MindStore.AboutUser));
        Assert.Equal("", temp.Store.Note("shino", MindStore.Us));
        var kept = temp.Store.Observations("shino");
        Assert.Equal(["Her note about the player, as it stood: Bob, who loves Hades.", "Her note about the two of them, as it stood: Friends."],
                     kept.Select(o => o.Text));
        Assert.All(kept, o => Assert.True(o.Condensed));                    // out of view, still recalled
        Assert.Empty(Log.Newest(kept, 10_000));

        temp.Store.SetNote(MindStore.Shared, MindStore.AboutUser, "Call me Bob.");
        using (new MindStore(temp.Location)) { }                            // once only: what the player writes stays
        Assert.Equal("Call me Bob.", temp.Store.Note(MindStore.Shared, MindStore.AboutUser));
    }
}
