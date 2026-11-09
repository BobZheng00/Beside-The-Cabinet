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

        Assert.Equal("Right now: playing Hades, 25 min into the session.\nPlay time the player has earned for their games and not yet used: 50 min.", text);
        var quiet = Situation.Describe(LedgerState.New(Friday, Rules), Rules, Friday);
        Assert.Equal("Play time the player has earned for their games and not yet used: none.", quiet);                     // a new player: nothing of LeetCode, rates or zeros

        var ledger = Situation.Ledger(state, Rules, Friday, "BobSmith", null);
        Assert.Contains("The player's LeetCode account: BobSmith.", ledger);
        Assert.Contains("Play time the player has earned for their games and not yet used: 50 min. Each night at 04:00, half of what isn't played fades.", ledger);
        Assert.Contains("Today (Friday): 45 min earned of the day's cap of 2 h, the most play time a day can earn; 1 h 15 min more can be earned.", ledger);
        Assert.Contains("So far today: 1 problem solved, 1 h 10 min focused, 25 min played.", ledger);
        Assert.Contains("Streak of good days (a solve, an activity or 25 min of focus): 2 days (best 7).", ledger);
        Assert.Contains("This week: 4 problems solved, 2 h 30 min focused, 1 h 40 min earned, 1 h 30 min played.", ledger);
        Assert.Contains("Solving a problem on LeetCode (a site of programming practice problems) pays by how hard it is: an easy one 1 token, a medium one 2, a hard one 4", ledger);
        Assert.Contains("focused work, timed by the cabinet, pays 15 min of play for every 1 h focused", ledger);
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
    [InlineData(true, true, true, "Solving a problem on LeetCode (a site of programming practice problems) pays by how hard it is: an easy one 1 token, a medium one 2, a hard one 4; the player's own activities (the gym, a run, a chore) pay by their size (small 1, medium 2, big 4); focused work, timed by the cabinet, pays 15 min of play for every 1 h focused.", "a solve, an activity or 25 min of focus")]
    [InlineData(true, true, false, "The player's own activities (the gym, a run, a chore) pay by their size (small 1, medium 2, big 4); focused work, timed by the cabinet, pays 15 min of play for every 1 h focused.", "an activity or 25 min of focus")]
    [InlineData(true, false, false, "Focused work, timed by the cabinet, pays 15 min of play for every 1 h focused.", "25 min of focus")]
    [InlineData(false, true, false, "The player's own activities (the gym, a run, a chore) pay by their size (small 1, medium 2, big 4).", "an activity")]
    [InlineData(false, false, true, "Solving a problem on LeetCode (a site of programming practice problems) pays by how hard it is: an easy one 1 token, a medium one 2, a hard one 4.", "a solve")]
    [InlineData(true, false, true, "Solving a problem on LeetCode (a site of programming practice problems) pays by how hard it is: an easy one 1 token, a medium one 2, a hard one 4; focused work, timed by the cabinet, pays 15 min of play for every 1 h focused.", "a solve or 25 min of focus")]
    public void Her_cabinet_tool_says_how_play_is_earned_by_only_what_earns(bool focus, bool activities, bool leetCode, string pays, string goodDay)
    {
        var rules = Rules with { Earns = new Earning(focus, activities, leetCode) };
        var state = LedgerState.New(Friday.AddDays(-4), rules) with { Week = new WeekStats { Solves = 4 } };

        var ledger = Situation.Ledger(state, rules, Friday, "BobSmith", null);

        Assert.Contains("How play is earned: the cabinet keeps a balance of the player's work, life and games. Doing good things earns play time for their games, " +
                        $"counted in tokens (the cabinet's own coins); a token is 15 min of play. {pays} A good day has {goodDay}; from the third", ledger);
        Assert.Contains($"Streak of good days ({goodDay}): ", ledger);
        Assert.Equal(leetCode, ledger.Contains("LeetCode"));                // the account, the rates: nothing names it when it is off
        Assert.Equal(leetCode, ledger.Contains("solve"));                   // nor a solve, as the week's problems or the streak's
    }

    private static readonly Rules NoPlayTime = Rules with { Earns = new Earning(false, false, false), Bedtime = TimeSpan.FromHours(23) };

    [Fact]
    public void A_picked_program_behind_another_window_is_told_as_open_with_its_clock_paused()
    {
        var state = LedgerState.New(Friday, Rules) with { Session = new Session { GameNames = ["VLC media player"], PlayedMs = 40 * 60_000 }, Held = true };

        Assert.StartsWith("Right now: VLC media player is open behind another window (clock paused), 40 min into the session.",
                          Situation.Describe(state, Rules, Friday));
        Assert.StartsWith("Right now: VLC media player is open behind another window, 40 min into the session.",
                          Situation.Describe(state, NoPlayTime, Friday));
    }

    /// <summary>A player with a balance from before (banked, owed, today's earning), who then chose no play time.</summary>
    private static LedgerState Kept(Rules rules) => LedgerState.New(Friday.AddDays(-4), rules) with
    {
        Credits = 3,
        DebtMs = 20 * 60_000,
        BestStreak = 7,
        Week = new WeekStats { EarnedMs = 100 * 60_000, FocusedMs = 150 * 60_000, PlayedMs = 90 * 60_000 },
        Today = DayRecord.Begin(new DateOnly(2026, 10, 2), rules) with { EarnedMs = 45 * 60_000, FocusedMs = 70 * 60_000, PlayedMs = 25 * 60_000 },
        Session = new Session { GameNames = ["Hades"], PlayedMs = 25 * 60_000 },
    };

    /// <summary>What says nothing of a balance: no play time banked or owed, no earning, cap, fade, streak or price.</summary>
    private static void NoBalanceIn(string text)
    {
        foreach (var word in new[] { "banked", "owed", "earn", "cap", "fade", "streak", "token", "IOU", "costs", "credit", "LeetCode" })
            Assert.DoesNotMatch($@"(?i)\b{word}", text);
    }

    [Fact]
    public void With_no_play_time_her_turn_has_only_the_game_and_nothing_of_a_balance()
    {
        var playing = Situation.Describe(Kept(NoPlayTime), NoPlayTime, Friday.AddHours(4));      // past bedtime too
        var quiet = Situation.Describe(LedgerState.New(Friday, NoPlayTime), NoPlayTime, Friday);
        var away = Situation.Describe(Kept(NoPlayTime) with { Paused = true }, NoPlayTime, Friday);

        Assert.Equal("Right now: playing Hades, 25 min into the session.", playing);
        Assert.Equal("Right now: not playing.", quiet);
        Assert.Equal("Right now: in a game of Hades, but the player stepped away.", away);
    }

    [Fact]
    public void With_no_play_time_her_cabinet_tool_gives_what_was_done_and_played_and_nothing_of_a_balance()
    {
        var thursday = DayRecord.Begin(new DateOnly(2026, 10, 1), Rules) with { FocusedMs = 30 * 60_000, EarnedMs = 15 * 60_000, FadedMs = 10 * 60_000 };
        var state = Kept(NoPlayTime) with { Days = [thursday] };

        var ledger = Situation.Ledger(state, NoPlayTime, Friday, "BobSmith", "yesterday");

        NoBalanceIn(ledger);
        Assert.Contains("So far today: 1 h 10 min focused, 25 min played.", ledger);
        Assert.Contains("Bedtime: 23:00.", ledger);
        Assert.Contains("This week: 2 h 30 min focused, 1 h 30 min played.", ledger);
        Assert.Contains("Right now: playing Hades, 25 min into the session.", ledger);
        Assert.Contains("The player chose no play time: the cabinet keeps no balance, and their games cost nothing.", ledger);
        Assert.EndsWith("Thursday 1 October 2026: The cabinet's record: focused 30 min.", ledger);
    }

    [Theory]
    [InlineData(true, true, true, " (solving problems, focusing, their own activities)")]
    [InlineData(true, true, false, " (focusing, their own activities)")]
    [InlineData(false, true, false, " (their own activities)")]
    [InlineData(true, false, true, " (solving problems, focusing)")]
    public void Her_base_text_names_only_what_earns(bool focus, bool activities, bool leetCode, string ways)
    {
        var persona = new Persona("Shino", "Shino is cheerful.");

        var system = Prompt.Build(persona, "", "", null, [], "", "hi", earns: new Earning(focus, activities, leetCode))[0].Text;

        Assert.Contains($"the player earns time for their games by doing good things{ways} and spends it playing.", system);
    }

    [Fact]
    public void With_no_play_time_her_base_text_says_the_cabinet_is_there_and_nothing_of_earning()
    {
        var persona = new Persona("Shino", "Shino is cheerful.");

        var system = Prompt.Build(persona, "", "", null, [], "", "hi", earns: new Earning(false, false, false))[0].Text;

        Assert.Contains("INSERT COIN, their little arcade cabinet, is with them and tells you what happens.", system);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(system, "INSERT COIN"));      // introduced once (#204's review)
        Assert.DoesNotContain("balance", system);
        Assert.DoesNotMatch(@"\bearn", system);
        Assert.DoesNotContain("spends it playing", system);
    }

    [Fact]
    public void Her_base_text_says_what_is_there_and_never_where_she_is()
    {
        var persona = new Persona("Shino", "Shino is cheerful.");

        foreach (var system in new[] { Prompt.Build(persona, "", "", null, [], "", "hi")[0].Text,
                                       Prompt.Build(persona, "", "", null, [], "", "hi", tools: true, asking: ["open_link"])[0].Text })
        {
            Assert.StartsWith("You are Shino. You keep the player company while they work and play, as yourself, not a service they call on. " +
                              "INSERT COIN, their little arcade cabinet, is with them: it keeps their balance of work and play and tells you what happens.", system);
            // a card's own world and place stand wherever it sets them (spec 2026-10-27-any-character.md)
            foreach (var word in new[] { "stool", "desk", "room", "a person in the room" })
                Assert.DoesNotMatch($@"(?i)\b{word}\b", system);
            Assert.Contains("The player types or speaks to you, and your words show in a speech bubble, a page at a time.", system);
            Assert.Contains("add [out] to your reply; when they are going to sleep, [sleep].", system);
        }
        Assert.Contains("These ask the player on a card before they act: open_link;", Prompt.Build(persona, "", "", null, [], "", "hi", tools: true, asking: ["open_link"])[0].Text);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Her_base_text_names_LeetCode_only_while_it_earns(bool focus, bool activities, bool leetCode)
    {
        var persona = new Persona("Shino", "Shino is cheerful.");

        var system = Prompt.Build(persona, "", "", null, [], "", "hi", earns: new Earning(focus, activities, leetCode))[0].Text;

        Assert.Contains($"something they are learning ({(leetCode ? "a LeetCode problem, " : "")}an exercise, a course, anything)", system);
        Assert.Equal(leetCode, system.Contains("LeetCode"));            // #231
    }

    [Fact]
    public void The_cabinets_own_things_are_said_in_plain_words()
    {
        var owing = LedgerState.New(Friday.AddDays(-4), Rules) with { DebtMs = 15 * 60_000 };
        var owingMore = owing with { DebtMs = 35 * 60_000 };

        Assert.Contains("Owed: 15 min of play time, played on credit (1 IOU, a slip the cabinet printed).", Situation.Describe(owing, Rules, Friday));
        Assert.Contains("Owed: 35 min of play time, played on credit (2 IOUs, slips the cabinet printed).", Situation.Ledger(owingMore, Rules, Friday, null, null));
        Assert.Equal("The player's play time for their games ran out; the game goes on, on credit (it costs 1.5 times as much), and the cabinet prints IOUs, " +
                     "slips for the play time owed.", Situation.Witness(new CreditsOut(), Rules));
        Assert.Equal("That paid off all the play time they owed.", Situation.Witness(new DebtPaid(15 * 60_000, 0, 0, 0), Rules));
        Assert.Equal("5 min of play time left for their games.", Situation.Witness(new LowTime(Warning.FiveMinutes, 5 * 60_000), Rules));
        Assert.Equal("Today became a good day (it has a solve, an activity or 25 min of focus), day 3 of a streak of good days.",
                     Situation.Witness(new StreakStar(3, Paid(15)), Rules));
        Assert.Contains("When their play time runs out the game goes on, on credit (it costs 1.5 times as much), and the cabinet prints IOUs, slips for the play time owed.",
                        Situation.Ledger(owing, Rules, Friday, null, null));
        foreach (var line in new[] { Situation.Describe(owing, Rules, Friday), Situation.Ledger(owing, Rules, Friday, null, null) })
            Assert.DoesNotMatch(@"banked|\bx\d|IOU ticket", line);           // the cabinet's shorthand, which one card read wrong
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

        Assert.Contains("Today's cap of 2 h, the most play time a day can earn, is reached: nothing more is earned until 04:00.", evening);
        Assert.DoesNotContain("bedtime", evening);                          // only once it matters
        Assert.Contains("It is past their bedtime (23:00): play costs 1.5 times as much play time until 04:00.", late);
    }

    [Fact]
    public void Witnesses_what_happened_not_what_it_paid_and_skips_the_bookkeeping()
    {
        Assert.Equal("The player solved a programming practice problem on LeetCode: \"Two Sum\", an easy one.",
                     Situation.Witness(new CoinsEarned(new SolvedTask("1", "Two Sum", Difficulty.Easy, Friday), 1, false, 1, Paid(15)), Rules));
        Assert.Equal("The player solved a programming practice problem on LeetCode: \"LRU Cache\", a medium one; day 3 of a streak of good days.",
                     Situation.Witness(new CoinsEarned(new SolvedTask("2", "LRU Cache", Difficulty.Medium, Friday), 2, true, 3, Paid(30, credited: 10)), Rules));
        Assert.Equal("A focus part ended: 30 min focused.", Situation.Witness(new FocusPaid(30 * 60_000, Paid(7.5, credited: 0)), Rules));
        Assert.Equal("Play went past the player's bedtime (00:30).", Situation.Witness(new LateNight(new TimeSpan(0, 30, 0), 1.5), Rules));
        Assert.Equal("The player's own activity \"the gym\" counted towards their play time by half for now.", Situation.Witness(new ActivityPaid("the gym", Size.Medium, 0.5, 0.5, Paid(15)), Rules));
        Assert.Equal("The player stopped playing after 1 h 5 min.",
                     Situation.Witness(new SessionEnded(new Session { PlayedMs = 65 * 60_000, IousPrinted = 2 }), Rules));
        Assert.Null(Situation.Witness(new TokenInserted(2), Rules));
        Assert.Null(Situation.Witness(new Paused(), Rules));
        Assert.Null(Situation.Witness(new Faded(40 * 60_000, 40 * 60_000), Rules));
        Assert.Null(Situation.Witness(new CapReached(120 * 60_000), Rules));
    }

    [Fact]
    public void With_no_play_time_what_a_last_payment_did_to_the_balance_isnt_witnessed()
    {
        Assert.Null(Situation.Witness(new DebtPaid(15 * 60_000, 1, 0, 0), NoPlayTime));
        Assert.Null(Situation.Witness(new StreakStar(3, Paid(15)), NoPlayTime));
        Assert.Equal("The player solved a programming practice problem: \"Two Sum\", an easy one.",       // LeetCode's last answer, and nothing names it
                     Situation.Witness(new CoinsEarned(new SolvedTask("1", "Two Sum", Difficulty.Easy, Friday), 2, true, 3, Paid(30)), NoPlayTime));
        Assert.Equal("The player stopped playing after 20 min.", Situation.Witness(new SessionEnded(new Session { PlayedMs = 20 * 60_000 }), NoPlayTime));
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

    [Theory]
    [InlineData("https://api.openai.com/v1", " ", "Fill in the model first.")]
    [InlineData("", "gpt", "Fill in the server first.")]
    [InlineData("not a url", "gpt", "Fill in the server first.")]
    [InlineData(" ", "", "Fill in the server and the model first.")]
    public void Test_names_only_the_field_that_is_missing(string server, string model, string said) =>
        Assert.Equal(said, Provider.Missing(server, model));

    [Theory]
    [InlineData("OpenAI", "gpt-6-luna")]
    [InlineData("OpenRouter", "openai/gpt-6-luna")]
    [InlineData("Anthropic", "claude-haiku-5-5")]
    public void A_new_player_finds_the_model_filled_in(string preset, string model) =>
        Assert.Equal(model, Provider.Presets.Single(p => p.Name == preset).ModelHint);

    [Theory]
    [InlineData("Ollama (this PC)", "the name of a model already downloaded into Ollama")]
    [InlineData("LM Studio (this PC)", "the name of a model already downloaded into LM Studio")]
    [InlineData("Other (OpenAI-compatible)", "the model's name at this provider")]
    [InlineData("OpenAI", "gpt-6-luna")]
    public void An_empty_model_box_says_what_goes_there(string preset, string said) =>
        Assert.Equal(said, Provider.Presets.Single(p => p.Name == preset).ModelPlaceholder);

    [Fact]
    public void A_hover_tip_is_broken_into_short_lines_between_words()
    {
        Assert.Equal("Opens a web page in\nthe player's browser.\nAsked first.", Words.Wrap("Opens a web page in the player's browser.\nAsked first.", 21));
        Assert.Equal("Short.", Words.Wrap("Short."));
        Assert.Equal("averyveryverylongword\nfits", Words.Wrap("averyveryverylongword fits", 10));     // a word longer than a line keeps its own
    }
}
