using System.Collections.Immutable;
using InsertCoin.Core;

namespace InsertCoin.Mind.Tests;

public class SituationTests
{
    private static readonly Rules Rules = new();
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Her_turn_has_only_what_matters_now_and_her_cabinet_tool_the_whole_ledger()
    {
        var state = LedgerState.New(Friday.AddDays(-4), Rules) with
        {
            Credits = 3,
            FuelMs = 5 * 60_000,
            SolveDays = ImmutableSortedSet.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)),
            BestStreak = 7,
            Week = new WeekStats { Solves = 4, EarnedMs = 100 * 60_000, FocusedMs = 150 * 60_000, PlayedMs = 90 * 60_000 },
            Today = DayRecord.Begin(new DateOnly(2026, 10, 2), Rules) with { EarnedMs = 45 * 60_000, Solves = 1, FocusedMs = 70 * 60_000, PlayedMs = 25 * 60_000 },
            Session = new Session { GameNames = ["Hades"], PlayedMs = 25 * 60_000 },
        };

        var text = Situation.Describe(state, Rules, Friday);

        Assert.Equal("Right now: playing Hades, 25 min into the session.\nPlay time banked: 50 min.", text);
        var quiet = Situation.Describe(LedgerState.New(Friday, Rules), Rules, Friday);
        Assert.Equal("Play time banked: none.", quiet);                     // a new player: nothing of LeetCode, rates or zeros

        var ledger = Situation.Ledger(state, Rules, Friday, "BobSmith", null);
        Assert.Contains("The player's LeetCode account: BobSmith.", ledger);
        Assert.Contains("Play time banked: 50 min. Each night at 04:00, half of what isn't played fades.", ledger);
        Assert.Contains("Today (Friday): 45 min earned of the day's cap of 2 h; 1 h 15 min more can be earned.", ledger);
        Assert.Contains("So far today: 1 problem solved, 1 h 10 min focused, 25 min played.", ledger);
        Assert.Contains("Streak of good days (a solve, an activity or 25 min of focus): 2 days (best 7).", ledger);
        Assert.Contains("This week: 4 problems solved, 2 h 30 min focused, 1 h 40 min earned, 1 h 30 min played.", ledger);
        Assert.Contains("LeetCode problems pay Easy 1 token, Medium 2, Hard 4", ledger);
        Assert.Contains("focus pays 15 min for every 1 h focused", ledger);
        Assert.Contains("from the third good day in a row", ledger);
        Assert.Contains("(Monday to Thursday 1 h 30 min, Friday 2 h, weekends 2 h 30 min)", ledger);
    }

    [Fact]
    public void Her_cabinet_tool_gives_a_days_record_with_what_it_earned()
    {
        var thursday = DayRecord.Begin(new DateOnly(2026, 10, 1), Rules) with { Solves = 1, Solved = ["Two Sum (Easy)"], EarnedMs = 15 * 60_000, FadedMs = 10 * 60_000 };
        var state = LedgerState.New(Friday.AddDays(-4), Rules) with { Days = [thursday], SolveDays = ImmutableSortedSet.Create(thursday.Day) };

        var yesterday = Situation.Ledger(state, Rules, Friday, null, "yesterday");

        Assert.Equal("Thursday 1 October 2026: The cabinet's record: solved 1 problem: Two Sum (Easy); a good day, towards the streak. " +
                     "Earned 15 min of play; 10 min of unplayed time faded overnight as it began.", yesterday.Split('\n')[^1]);
        Assert.EndsWith("Wednesday 30 September 2026: no record (the cabinet keeps the last 120 days).", Situation.Ledger(state, Rules, Friday, null, "2026-09-30"));
        Assert.EndsWith("No day called \"last week\": name it as today, yesterday or YYYY-MM-DD.", Situation.Ledger(state, Rules, Friday, null, "last week"));
    }

    [Theory]
    [InlineData(true, true, true, "LeetCode problems pay Easy 1 token, Medium 2, Hard 4; the player's own activities pay by their size (small 1, medium 2, big 4); focus pays 15 min for every 1 h focused.", "a solve, an activity or 25 min of focus")]
    [InlineData(true, true, false, "The player's own activities pay by their size (small 1, medium 2, big 4); focus pays 15 min for every 1 h focused.", "an activity or 25 min of focus")]
    [InlineData(true, false, false, "Focus pays 15 min for every 1 h focused.", "25 min of focus")]
    [InlineData(false, true, false, "The player's own activities pay by their size (small 1, medium 2, big 4).", "an activity")]
    [InlineData(false, false, true, "LeetCode problems pay Easy 1 token, Medium 2, Hard 4.", "a solve")]
    [InlineData(true, false, true, "LeetCode problems pay Easy 1 token, Medium 2, Hard 4; focus pays 15 min for every 1 h focused.", "a solve or 25 min of focus")]
    public void Her_cabinet_tool_says_how_play_is_earned_by_only_what_earns(bool focus, bool activities, bool leetCode, string pays, string goodDay)
    {
        var rules = Rules with { Earns = new Earning(focus, activities, leetCode) };
        var state = LedgerState.New(Friday.AddDays(-4), rules) with { Week = new WeekStats { Solves = 4 } };

        var ledger = Situation.Ledger(state, rules, Friday, "BobSmith", null);

        Assert.Contains($"How play is earned: a token is 15 min of play. {pays} A good day has {goodDay}; from the third", ledger);
        Assert.Contains($"Streak of good days ({goodDay}): ", ledger);
        Assert.Equal(leetCode, ledger.Contains("LeetCode"));                // the account, the rates: nothing names it when it is off
        Assert.Equal(leetCode, ledger.Contains("solve"));                   // nor a solve, as the week's problems or the streak's
    }

    [Fact]
    public void With_nothing_earning_her_cabinet_tool_says_so_and_names_no_streak()
    {
        var rules = Rules with { Earns = new Earning(false, false, false) };

        var ledger = Situation.Ledger(LedgerState.New(Friday, rules), rules, Friday, "BobSmith", null);

        Assert.Contains("How play is earned: a token is 15 min of play. Nothing earns play time now. A day can earn at most its cap", ledger);
        Assert.DoesNotContain("good day", ledger);
        Assert.DoesNotContain("LeetCode", ledger);
    }

    [Theory]
    [InlineData(true, true, true, " (solving problems, focusing, their own activities)")]
    [InlineData(true, true, false, " (focusing, their own activities)")]
    [InlineData(false, true, false, " (their own activities)")]
    [InlineData(true, false, true, " (solving problems, focusing)")]
    [InlineData(false, false, false, "")]
    public void Her_base_text_names_only_what_earns(bool focus, bool activities, bool leetCode, string ways)
    {
        var persona = new Persona("Shino", "Shino is cheerful.");

        var system = Prompt.Build(persona, "", "", null, [], "", "hi", earns: new Earning(focus, activities, leetCode))[0].Text;

        Assert.Contains($"the player earns time for their games by doing good things{ways} and spends it playing.", system);
    }

    [Fact]
    public void Says_when_the_cap_is_reached_and_when_it_is_past_bedtime()
    {
        var rules = Rules with { Bedtime = TimeSpan.FromHours(23) };
        var state = LedgerState.New(Friday.AddDays(-4), rules) with
        {
            Today = DayRecord.Begin(new DateOnly(2026, 10, 2), rules) with { EarnedMs = 120 * 60_000 },
        };

        var evening = Situation.Describe(state, rules, Friday);
        var late = Situation.Describe(state, rules, Friday.AddHours(4));

        Assert.Contains("Today's cap of 2 h is reached: nothing more is paid until 04:00.", evening);
        Assert.DoesNotContain("bedtime", evening);                          // only once it matters
        Assert.Contains("It is past their bedtime (23:00): play costs x1.5 until 04:00.", late);
    }

    [Fact]
    public void Witnesses_what_happened_not_what_it_paid_and_skips_the_bookkeeping()
    {
        Assert.Equal("The player solved \"Two Sum\" (Easy).",
                     Situation.Witness(new CoinsEarned(new SolvedTask("1", "Two Sum", Difficulty.Easy, Friday), 1, false, 1, Paid(15)), Rules));
        Assert.Equal("The player solved \"LRU Cache\" (Medium): day 3 of a streak of good days.",
                     Situation.Witness(new CoinsEarned(new SolvedTask("2", "LRU Cache", Difficulty.Medium, Friday), 2, true, 3, Paid(30, credited: 10)), Rules));
        Assert.Equal("A focus part ended: 30 min focused.", Situation.Witness(new FocusPaid(30 * 60_000, Paid(7.5, credited: 0)), Rules));
        Assert.Equal("Play went past the player's bedtime (00:30).", Situation.Witness(new LateNight(new TimeSpan(0, 30, 0), 1.5), Rules));
        Assert.Equal("\"the gym\" counted by half for now.", Situation.Witness(new ActivityPaid("the gym", Size.Medium, 0.5, 0.5, Paid(15)), Rules));
        Assert.Equal("The player stopped playing after 1 h 5 min.",
                     Situation.Witness(new SessionEnded(new Session { PlayedMs = 65 * 60_000, IousPrinted = 2 }), Rules));
        Assert.Null(Situation.Witness(new TokenInserted(2), Rules));
        Assert.Null(Situation.Witness(new Paused(), Rules));
        Assert.Null(Situation.Witness(new Faded(40 * 60_000, 40 * 60_000), Rules));
        Assert.Null(Situation.Witness(new CapReached(120 * 60_000), Rules));
    }

    private static Payout Paid(double minutes, double? credited = null)
    {
        var earned = (long)(minutes * 60_000);
        var paid = (long)((credited ?? minutes) * 60_000);
        return new Payout(earned, 0, paid, earned - paid, (int)(paid / (15 * 60_000)));
    }

    [Fact]
    public void A_provider_needs_an_address_and_a_model()
    {
        Assert.Null(Provider.From("OpenAi", "", "gpt"));
        Assert.Null(Provider.From("OpenAi", "not a url", "gpt"));
        Assert.Null(Provider.From("Anthropic", "https://api.anthropic.com", " "));
        Assert.Equal(new Provider(Protocol.Anthropic, "https://api.anthropic.com", "claude-haiku-4-5"),
                     Provider.From("anthropic", " https://api.anthropic.com ", "claude-haiku-4-5"));
    }

    [Fact]
    public void A_hover_tip_is_broken_into_short_lines_between_words()
    {
        Assert.Equal("Opens a web page in\nthe player's browser.\nAsked first.", Words.Wrap("Opens a web page in the player's browser.\nAsked first.", 21));
        Assert.Equal("Short.", Words.Wrap("Short."));
        Assert.Equal("averyveryverylongword\nfits", Words.Wrap("averyveryverylongword fits", 10));     // a word longer than a line keeps its own
    }
}
