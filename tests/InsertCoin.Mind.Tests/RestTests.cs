using InsertCoin.Core;

namespace InsertCoin.Mind.Tests;

public class RestTests
{
    private static readonly TimeSpan Four = TimeSpan.FromHours(4);
    private static readonly TimeSpan Eleven = TimeSpan.FromHours(23);

    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(-4));

    private static DayRecord Night(int day, int hour, int minute, long playedMs = 0) =>
        new() { Day = new DateOnly(2026, 10, day), LastActive = At(hour < 4 ? day + 1 : day, hour, minute), PlayedMs = playedMs };

    [Theory]
    [InlineData(22, 0, null)]
    [InlineData(22, 40, Nudge.WindDown)]
    [InlineData(23, 10, Nudge.Bedtime)]
    [InlineData(23, 40, Nudge.StillUp)]
    [InlineData(0, 20, Nudge.StillUp)]
    [InlineData(0, 45, null)]                               // then quiet
    public void Around_bedtime_a_word_before_at_and_once_after(int hour, int minute, Nudge? due) =>
        Assert.Equal(due, Rest.AtBedtime(Eleven, At(7, hour, minute), new HashSet<Nudge>(), Four));

    [Fact]
    public void A_bedtime_after_midnight_counts_round_midnight()
    {
        var half = new TimeSpan(0, 30, 0);

        Assert.Equal(Nudge.WindDown, Rest.AtBedtime(half, At(8, 0, 10), new HashSet<Nudge>(), Four));
        Assert.Equal(Nudge.Bedtime, Rest.AtBedtime(half, At(8, 0, 35), new HashSet<Nudge>(), Four));
        Assert.Equal(Nudge.StillUp, Rest.AtBedtime(half, At(8, 1, 5), new HashSet<Nudge>(), Four));
    }

    [Fact]
    public void Each_word_is_said_once_a_night()
    {
        var given = new HashSet<Nudge> { Nudge.WindDown };

        Assert.Null(Rest.AtBedtime(Eleven, At(7, 22, 50), given, Four));
        Assert.Equal(Nudge.Bedtime, Rest.AtBedtime(Eleven, At(7, 23, 5), given, Four));
    }

    [Fact]
    public void A_break_after_two_hours_of_play_and_two_hours_after_the_last()
    {
        const long minute = 60_000;

        Assert.False(Rest.BreakDue(119 * minute, null));
        Assert.True(Rest.BreakDue(120 * minute, null));
        Assert.False(Rest.BreakDue(200 * minute, 120 * minute));
        Assert.True(Rest.BreakDue(240 * minute, 120 * minute));
    }

    [Fact]
    public void Three_late_nights_of_four_are_noticed()
    {
        DayRecord[] days = [Night(3, 0, 30), Night(4, 1, 15), Night(5, 23, 10), Night(6, 2, 0)];

        var notice = Rest.Notice(days, Eleven, 0, Four);

        Assert.NotNull(notice);
        Assert.Contains("past their bedtime (23:00) on 3 of the last 4 nights, once until about 02:00", notice);
        Assert.Contains("without lecturing", notice);
    }

    [Fact]
    public void Without_a_bedtime_late_is_past_one_in_the_morning()
    {
        Assert.Null(Rest.Notice([Night(3, 0, 30), Night(4, 1, 15), Night(5, 1, 0), Night(6, 0, 50)], null, 0, Four));
        Assert.Contains("past one in the morning", Rest.Notice([Night(3, 2, 30), Night(4, 1, 45), Night(5, 3, 0)], null, 0, Four));
    }

    [Fact]
    public void Days_without_play_with_time_banked_are_noticed_rest_counts_too()
    {
        DayRecord[] days = [Night(4, 22, 0), Night(5, 22, 0), Night(6, 22, 0)];

        Assert.Contains("hasn't played for three days, with 45 min banked", Rest.Notice(days, Eleven, 45 * 60_000, Four));
        Assert.Null(Rest.Notice(days, Eleven, 10 * 60_000, Four));
        Assert.Null(Rest.Notice([.. days[..2], Night(6, 22, 0, playedMs: 30 * 60_000)], Eleven, 45 * 60_000, Four));
    }

    [Fact]
    public void Their_usual_night_is_the_middle_of_a_week_of_evenings()
    {
        DayRecord[] days = [Night(1, 0, 30), Night(2, 1, 0), Night(3, 0, 45), Night(4, 1, 15), Night(5, 0, 30), Night(6, 23, 50), Night(7, 2, 0)];

        Assert.Equal(new TimeSpan(0, 45, 0), Rest.Usual(days, Four));
        Assert.Null(Rest.Usual(days[..4], Four));
    }

    [Theory]
    [InlineData("23:30", 23, 30)]
    [InlineData("0:30", 0, 30)]
    [InlineData("03:30", 3, 30)]
    [InlineData("20:00", 20, 0)]
    public void A_bedtime_is_taken_from_the_evening_to_half_past_three(string text, int hour, int minute) =>
        Assert.Equal(new TimeSpan(hour, minute, 0), Rest.ParseBedtime(text));

    [Theory]
    [InlineData("19:30")]
    [InlineData("04:00")]
    [InlineData("noon")]
    public void Other_times_are_no_bedtime(string text) => Assert.Null(Rest.ParseBedtime(text));
}
