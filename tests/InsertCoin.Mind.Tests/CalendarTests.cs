namespace InsertCoin.Mind.Tests;

/// <summary>The player's calendar read from a private link and kept in step on the agenda.</summary>
public class CalendarTests
{
    private static readonly TimeZoneInfo China = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 8, 0, 0, TimeSpan.FromHours(8));

    // as Google and Outlook publish them: a folded line, escapes, an alarm inside an event, zones by IANA and by Windows name
    private const string Feed = """
        BEGIN:VCALENDAR
        PRODID:-//Google Inc//Google Calendar 70.9054//EN
        VERSION:2.0
        BEGIN:VTIMEZONE
        TZID:Asia/Shanghai
        BEGIN:STANDARD
        TZOFFSETFROM:+0800
        TZOFFSETTO:+0800
        DTSTART:19700101T000000
        END:STANDARD
        END:VTIMEZONE
        BEGIN:VEVENT
        DTSTART;TZID=Asia/Shanghai:20261008T100000
        DTEND;TZID=Asia/Shanghai:20261008T110000
        UID:interview@google.com
        SUMMARY:Job interview\, Ac
         me
        BEGIN:VALARM
        ACTION:DISPLAY
        TRIGGER:-PT10M
        DESCRIPTION:not a property of the event
        END:VALARM
        END:VEVENT
        BEGIN:VEVENT
        DTSTART:20261005T013000Z
        DTEND:20261005T020000Z
        RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4
        EXDATE:20261012T013000Z
        UID:standup@google.com
        SUMMARY:Stand-up
        END:VEVENT
        BEGIN:VEVENT
        RECURRENCE-ID:20261007T013000Z
        DTSTART:20261007T060000Z
        DTEND:20261007T063000Z
        UID:standup@google.com
        SUMMARY:Stand-up (moved)
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;VALUE=DATE:20261009
        DTEND;VALUE=DATE:20261010
        UID:exam@google.com
        SUMMARY:Calculus exam
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;TZID=Asia/Shanghai:20261010T190000
        DURATION:PT2H
        UID:dinner@google.com
        STATUS:CANCELLED
        SUMMARY:Dinner
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;TZID=China Standard Time:20261013T090000
        DTEND;TZID=China Standard Time:20261013T093000
        RRULE:FREQ=MONTHLY;BYDAY=2TU
        UID:outlook-review
        SUMMARY:Monthly review
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;TZID=America/New_York:20261031T090000
        DTEND;TZID=America/New_York:20261031T100000
        RRULE:FREQ=DAILY;COUNT=2
        UID:ny-call
        SUMMARY:Call with New York
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void A_published_calendar_is_read_with_its_zones_repeats_and_changes()
    {
        var events = Ics.Events(Feed, Monday.AddHours(-8), Monday.AddDays(46), China);

        Assert.Equal(
        [
            "10-05 09:30 Stand-up", "10-07 14:00 Stand-up (moved)", "10-08 10:00 Job interview, Acme", "10-09 00:00 Calculus exam (all day)",
            "10-13 09:00 Monthly review", "10-14 09:30 Stand-up",
            "10-31 21:00 Call with New York", "11-01 22:00 Call with New York",      // 09:00 in New York, before and after its clocks go back
            "11-10 09:00 Monthly review",                                             // the second Tuesday again
        ], events.Select(e => $"{e.Start:MM-dd HH:mm} {e.Title}{(e.AllDay ? " (all day)" : "")}"));
        Assert.Equal(TimeSpan.FromHours(1), events.Single(e => e.Title.StartsWith("Job")).End - events.Single(e => e.Title.StartsWith("Job")).Start);
        Assert.Equal(3, events.Count(e => e.Title.StartsWith("Stand-up")));        // the 12th was left out
        Assert.Equal(events.Count, events.Select(e => e.Uid).Distinct().Count());
    }

    [Fact]
    public void Synced_events_follow_the_calendar_and_keep_what_the_player_set()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var events = Ics.Events(Feed, Monday.AddHours(-8), Monday.Add(CalendarSync.Ahead), China);

        Assert.Equal((events.Count, 0, 0), CalendarSync.Apply(store, "Google", events, Monday.AddDays(-1), Monday));
        var interview = store.Agenda().Single(p => p.Text == "Job interview, Acme");
        Assert.Equal(("Google", (TimeSpan?)null), (interview.Source, interview.Before));
        store.UpdatePlan(interview.Id, interview.Text, interview.At, false, TimeSpan.FromHours(1), Repeat.Once);   // the player wants a reminder for this one

        // the interview moves to 11:00 and the exam is called off
        var later = events.Where(e => e.Title != "Calculus exam")
                          .Select(e => e.Title.StartsWith("Job") ? e with { Start = e.Start.AddHours(1), End = e.End.AddHours(1) } : e).ToList();
        Assert.Equal((0, 1, 1), CalendarSync.Apply(store, "Google", later, Monday.AddDays(-1), Monday));
        interview = store.Agenda().Single(p => p.Text == "Job interview, Acme");
        Assert.Equal((11, TimeSpan.FromHours(1)), (interview.At.Hour, interview.Before!.Value));
        Assert.DoesNotContain(store.Agenda(), p => p.Text == "Calculus exam");
        Assert.Equal((0, 0, 0), CalendarSync.Apply(store, "Google", later, Monday.AddDays(-1), Monday));           // nothing new: nothing done

        store.DeletePlansFrom("Google");
        Assert.Empty(store.Agenda());
    }

    [Fact]
    public void A_busy_week_is_summed_up_in_her_turn_not_listed_in_full()
    {
        var plans = Enumerable.Range(0, 20).Select(i => new Plan(i, $"Meeting {i}", Monday.AddHours(2 + i * 6), false, null, null, Monday, null, Source: "Work")).ToList();

        var block = Agenda.Block(plans, Monday);

        Assert.Equal(Agenda.MostAhead, block.Split("Meeting").Length - 1);
        Assert.Contains($"- and {20 - Agenda.MostAhead} more in the week", block);
    }
}
