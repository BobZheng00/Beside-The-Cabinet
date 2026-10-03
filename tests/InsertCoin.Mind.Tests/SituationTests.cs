using System.Collections.Immutable;
using InsertCoin.Core;

namespace InsertCoin.Mind.Tests;

public class SituationTests
{
    private static readonly Rules Rules = new();
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Describes_time_debt_streak_week_and_what_is_running()
    {
        var state = LedgerState.New(Friday.AddDays(-4), Rules) with
        {
            Credits = 3,
            FuelMs = 5 * 60_000,
            SolveDays = ImmutableSortedSet.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)),
            BestStreak = 7,
            Week = new WeekStats { Solves = 4, TokensEarned = 6, PlayedMs = 90 * 60_000 },
            Session = new Session { GameNames = ["Hades"], PlayedMs = 25 * 60_000 },
        };

        var text = Situation.Describe(state, Rules, Friday, "BobSmith");

        Assert.Contains("Now: Friday 2 October, 19:30.", text);
        Assert.Contains("Player: BobSmith on LeetCode.", text);
        Assert.Contains("Play time banked: 50 min. Unused time expires when the week ends, in 3 days.", text);
        Assert.Contains("Owed: nothing.", text);
        Assert.Contains("Daily solve streak: 2 days (best 7).", text);
        Assert.Contains("This week: 4 problems solved, 6 tokens earned, 1 h 30 min played.", text);
        Assert.Contains("Right now: playing Hades, 25 min into the session.", text);
    }

    [Fact]
    public void Witnesses_what_matters_and_skips_routine()
    {
        Assert.Equal("The player solved \"Two Sum\" (Easy): +1 token.",
                     Situation.Witness(new CoinsEarned(new SolvedTask("1", "Two Sum", Difficulty.Easy, Friday), 1, false, 1), Rules));
        Assert.Equal("The player stopped playing after 1 h 5 min; 2 IOU tickets printed.",
                     Situation.Witness(new SessionEnded(new Session { PlayedMs = 65 * 60_000, IousPrinted = 2 }), Rules));
        Assert.Null(Situation.Witness(new TokenInserted(2), Rules));
        Assert.Null(Situation.Witness(new Paused(), Rules));
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
