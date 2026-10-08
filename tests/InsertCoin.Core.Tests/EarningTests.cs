using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class EarningTests
{
    [Theory]
    [InlineData(Difficulty.Easy, 1)]
    [InlineData(Difficulty.Medium, 2)]
    [InlineData(Difficulty.Hard, 4)]
    public void Difficulty_sets_token_count(Difficulty difficulty, int tokens)
    {
        var m = new Machine();
        var events = m.Solve("two-sum", difficulty);

        Assert.Equal(tokens, m.State.Credits);
        var earned = Assert.Single(events.OfType<CoinsEarned>());
        Assert.Equal(tokens, earned.Tokens);
        Assert.False(earned.StarToken);
    }

    [Fact]
    public void A_problem_counts_once_ever()
    {
        var m = new Machine();
        m.Solve("two-sum");
        m.Now += TimeSpan.FromDays(2);
        var events = m.Solve("two-sum");

        Assert.Empty(events.OfType<CoinsEarned>());
        Assert.Equal(Min(15) / 4, m.State.TimeLeftMs(m.Rules));     // two nights' fade, and nothing new
    }

    [Fact]
    public void Solves_accepted_before_install_do_not_count()
    {
        var m = new Machine();
        var events = m.Solve("old", Difficulty.Hard, acceptedAt: T0.AddMinutes(-1));

        Assert.Empty(events);
        Assert.Equal(0, m.State.Credits);
    }

    [Fact]
    public void Unknown_difficulty_waits_and_is_counted_once_known()
    {
        var m = new Machine();
        Assert.Empty(m.Solve("lru-cache", difficulty: null));
        Assert.DoesNotContain("lru-cache", m.State.SeenTaskIds);

        m.Solve("lru-cache", Difficulty.Medium, acceptedAt: T0);
        Assert.Equal(2, m.State.Credits);
    }

    [Fact]
    public void Setting_the_player_marks_visible_tasks_seen_without_reward()
    {
        var m = new Machine();
        var visible = new[] { new SolvedTask("a", "A", Difficulty.Hard, T0), new SolvedTask("b", "B", null, T0) };

        Assert.Empty(m.Send(new PlayerSet("bob", visible)));
        Assert.Empty(m.Solve("a", Difficulty.Hard));
        Assert.Equal("bob", m.State.Username);
        Assert.Equal(0, m.State.Credits);
    }

    [Fact]
    public void Several_tasks_in_one_poll_are_all_counted()
    {
        var m = new Machine();
        m.Send(new TasksObserved([
            new SolvedTask("a", "A", Difficulty.Easy, T0),
            new SolvedTask("b", "B", Difficulty.Hard, T0),
            new SolvedTask("a", "A", Difficulty.Easy, T0),
        ]));

        Assert.Equal(5, m.State.Credits);
        Assert.Equal(2, m.State.Week.Solves);
    }

    [Fact]
    public void Streak_of_three_days_adds_one_star_token_on_the_first_solve_of_the_day()
    {
        var m = new Machine();
        m.Solve("d1");
        m.Now += TimeSpan.FromDays(1);
        m.Solve("d2");
        m.Now += TimeSpan.FromDays(1);

        var first = m.Solve("d3a").OfType<CoinsEarned>().Single();
        var second = m.Solve("d3b").OfType<CoinsEarned>().Single();

        Assert.True(first.StarToken);
        Assert.Equal(3, first.Streak);
        Assert.Equal(1 + 1, first.Tokens);
        Assert.False(second.StarToken);
        Assert.Equal(1, second.Tokens);
        Assert.Equal(3, m.State.BestStreak);
    }

    [Fact]
    public void A_solve_before_4am_belongs_to_the_previous_day()
    {
        var m = new Machine();
        m.Solve("wed");                                  // Wed 10:00 → Wed
        m.Now = new DateTimeOffset(2026, 10, 2, 1, 30, 0, T0.Offset); // Fri 01:30 → Thu
        m.Solve("thu-late");
        m.Now = new DateTimeOffset(2026, 10, 2, 11, 0, 0, T0.Offset); // Fri 11:00 → Fri

        var events = m.Solve("fri");

        Assert.True(events.OfType<CoinsEarned>().Single().StarToken);
        Assert.Equal(3, GameCalendar.CurrentStreak(m.State, m.Now, m.Rules));
    }

    [Fact]
    public void Streak_stays_alive_until_today_ends()
    {
        var m = new Machine();
        m.Solve("a");
        m.Now += TimeSpan.FromDays(1);
        Assert.Equal(1, GameCalendar.CurrentStreak(m.State, m.Now, m.Rules));
        m.Now += TimeSpan.FromDays(1);
        Assert.Equal(0, GameCalendar.CurrentStreak(m.State, m.Now, m.Rules));
    }

    [Fact]
    public void Earning_pays_debt_first_and_a_partial_ticket_leaves_the_rest_as_fuel()
    {
        var m = new Machine();
        m.Play(TimeSpan.FromMinutes(5));                 // no credits: 5 min overtime = 7.5 min debt
        Assert.Equal(Min(7.5), m.State.DebtMs);

        var events = m.Solve("pay");

        var paid = Assert.Single(events.OfType<DebtPaid>());
        Assert.Equal(Min(7.5), paid.PaidMs);
        Assert.Equal(0, m.State.DebtMs);
        Assert.Equal(0, m.State.Credits);
        Assert.Equal(Min(7.5), m.State.FuelMs);
    }

    [Fact]
    public void Earning_that_does_not_cover_the_debt_keeps_the_machine_empty()
    {
        var m = new Machine();
        m.Play(TimeSpan.FromMinutes(20));                // 30 min debt = 2 tickets
        m.Solve("pay");

        Assert.Equal(Min(15), m.State.DebtMs);
        Assert.Equal(0, m.State.Credits);
        Assert.Equal(0, m.State.FuelMs);
    }
}
