using System.Collections.Immutable;
using InsertCoin.Core;

namespace InsertCoin.Mind.Tests;

public class SituationTests
{
    private static readonly Rules Rules = new();
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Describes_time_debt_today_streak_week_and_what_is_running()
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

        var text = Situation.Describe(state, Rules, Friday, "BobSmith");

        Assert.DoesNotContain("Now:", text);                                // the time is the turn's own (When)
        Assert.Contains("Player: BobSmith on LeetCode.", text);
        Assert.Contains("Play time banked: 50 min. Each night at 04:00, half of what isn't played fades.", text);
        Assert.Contains("Owed: nothing.", text);
        Assert.Contains("Focus pays 15 min of play for every 1 h focused.", text);
        Assert.Contains("Today (Friday): 45 min earned of the day's cap of 2 h; 1 h 15 min more can be earned.", text);
        Assert.Contains("So far today: 1 problem solved, 1 h 10 min focused, 25 min played.", text);
        Assert.DoesNotContain("Bedtime", text);
        Assert.Contains("Daily solve streak: 2 days (best 7).", text);
        Assert.Contains("This week: 4 problems solved, 2 h 30 min focused, 1 h 40 min earned, 1 h 30 min played.", text);
        Assert.Contains("Right now: playing Hades, 25 min into the session.", text);
    }

    [Fact]
    public void Says_when_the_cap_is_reached_and_when_it_is_past_bedtime()
    {
        var rules = Rules with { Bedtime = TimeSpan.FromHours(23) };
        var state = LedgerState.New(Friday.AddDays(-4), rules) with
        {
            Today = DayRecord.Begin(new DateOnly(2026, 10, 2), rules) with { EarnedMs = 120 * 60_000 },
        };

        var evening = Situation.Describe(state, rules, Friday, null);
        var late = Situation.Describe(state, rules, Friday.AddHours(4), null);

        Assert.Contains("Today (Friday): the day's cap of 2 h is reached; nothing more is paid until 04:00.", evening);
        Assert.Contains("Bedtime: 23:00; play after it costs x1.5 until 04:00.", evening);
        Assert.Contains("Bedtime: 23:00, and it is past it: play costs x1.5 until 04:00.", late);
    }

    [Fact]
    public void Witnesses_what_matters_and_skips_routine()
    {
        Assert.Equal("The player solved \"Two Sum\" (Easy): +1 token.",
                     Situation.Witness(new CoinsEarned(new SolvedTask("1", "Two Sum", Difficulty.Easy, Friday), 1, false, 1, Paid(15)), Rules));
        Assert.Equal("The player solved \"LRU Cache\" (Medium): +2 tokens, but today's cap let only 10 min of it count.",
                     Situation.Witness(new CoinsEarned(new SolvedTask("2", "LRU Cache", Difficulty.Medium, Friday), 2, false, 1, Paid(30, credited: 10)), Rules));
        Assert.Equal("A focus part ended: 30 min focused, worth 7 min of play, but today's cap was already reached, so it paid nothing.",
                     Situation.Witness(new FocusPaid(30 * 60_000, Paid(7.5, credited: 0)), Rules));
        Assert.Equal("Play went past the player's bedtime (00:30): it costs x1.5 until 04:00.",
                     Situation.Witness(new LateNight(new TimeSpan(0, 30, 0), 1.5), Rules));
        Assert.Equal("A new day: overnight 40 min of unplayed time faded, 40 min left.",
                     Situation.Witness(new Faded(40 * 60_000, 40 * 60_000), Rules));
        Assert.Equal("The player stopped playing after 1 h 5 min; 2 IOU tickets printed.",
                     Situation.Witness(new SessionEnded(new Session { PlayedMs = 65 * 60_000, IousPrinted = 2 }), Rules));
        Assert.Null(Situation.Witness(new TokenInserted(2), Rules));
        Assert.Null(Situation.Witness(new Paused(), Rules));
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
}
