using System.Collections.Immutable;
using InsertCoin.Core;

namespace InsertCoin.Mind.Tests;

/// <summary>The cabinet's record of each day in her diary: written by code, everything the record holds, once.</summary>
public class DiaryTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private static DayRecord Saturday => new()
    {
        Day = new DateOnly(2026, 10, 3),
        CapMs = 150 * 60_000L, Bedtime = new TimeSpan(23, 30, 0), EarnedMs = 150 * 60_000L, OverCapMs = 30 * 60_000L,
        Solves = 2, Solved = ["Two Sum (Easy)", "LRU Cache (Medium)"],
        Activities = ImmutableDictionary<string, double>.Empty.Add("the gym", 1).Add("a walk", 0.5),
        FocusedMs = 100 * 60_000L, PlayedMs = 70 * 60_000L, Games = ["Hades"], LateExtraMs = 5 * 60_000L,
        FirstActive = new DateTimeOffset(2026, 10, 3, 9, 10, 0, TimeSpan.FromHours(8)),
        LastActive = new DateTimeOffset(2026, 10, 3, 23, 55, 0, TimeSpan.FromHours(8)),
        LastPlayed = new DateTimeOffset(2026, 10, 3, 23, 40, 0, TimeSpan.FromHours(8)),
    };

    [Fact]
    public void A_day_is_written_from_what_the_player_did_and_what_it_earned_is_kept_apart()
    {
        Assert.Equal("The cabinet's record: solved 2 problems: Two Sum (Easy), LRU Cache (Medium); did a walk (counted half), the gym; " +
                     "focused 1 h 40 min; played 1 h 10 min (Hades), the last game ending at 23:40; played past bedtime; " +
                     "bedtime 23:30; at the PC from 09:10 to 23:55; a good day, towards the streak.",
                     Diary.Entry(Saturday, good: true));
        Assert.Equal("Earned 2 h 30 min of play, and 30 min more past the day's cap of 2 h 30 min; playing past bedtime cost 5 min extra.",
                     Diary.Accounts(Saturday));
        Assert.Equal("", Diary.Accounts(new DayRecord { Day = new DateOnly(2026, 10, 4) }));
        Assert.Equal("The cabinet's record: not at the PC all day.", Diary.Entry(new DayRecord { Day = new DateOnly(2026, 10, 4) }, good: false));
    }

    [Fact]
    public void Each_finished_day_goes_into_her_diary_once_dated_at_its_end_and_upkeep_keeps_no_facts_from_it()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday.AddDays(2)));
        var state = new LedgerState { Days = [Saturday], GoodDays = [Saturday.Day] };

        conversation.Record(state);
        conversation.Record(state);                                            // once only

        var note = Assert.Single(temp.Store.Observations("shino"));
        Assert.Equal((new DateTime(2026, 10, 3, 23, 59, 0), Diary.Cabinet), (note.At.DateTime, note.Source));
        Assert.StartsWith("The cabinet's record: solved 2 problems", note.Text);
        Assert.EndsWith("a good day, towards the streak.", note.Text);
        Assert.False(conversation.NeedsDigest(TimeSpan.Zero));                 // nothing for upkeep to keep facts from
        Assert.Single(Log.Newest(Log.Left(temp.Store.Observations("shino"), temp.Store.Folded("shino")), 10_000));     // in view at once

        var sunday = Saturday with { Day = new DateOnly(2026, 10, 4), Solved = [], Solves = 0 };
        conversation.Record(state with { Days = [Saturday, sunday] });         // the next day turns
        Assert.Equal(2, temp.Store.Observations("shino").Count);
    }
}
