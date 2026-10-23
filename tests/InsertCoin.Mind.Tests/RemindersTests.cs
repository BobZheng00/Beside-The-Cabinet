using Microsoft.Data.Sqlite;

namespace InsertCoin.Mind.Tests;

/// <summary>Reminders a while before, repeating ones, the player's default lead, and what goes off when.</summary>
public class RemindersTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private static Plan P(string text, DateTimeOffset at, TimeSpan? before = null, Repeat repeat = Repeat.Once, bool allDay = false, DateTimeOffset? added = null) =>
        new(1, text, at, allDay, before, null, added ?? Friday.AddDays(-1), null, repeat);

    [Fact]
    public void Options_are_read_as_how_long_before_and_how_it_repeats()
    {
        Assert.Equal((TimeSpan.FromMinutes(30), Repeat.Once), Agenda.ParseOptions("30 min before"));
        Assert.Equal((TimeSpan.FromHours(1), Repeat.Weekly), Agenda.ParseOptions("an hour before, weekly"));
        Assert.Equal((TimeSpan.FromDays(1), Repeat.Once), Agenda.ParseOptions("the day before"));
        Assert.Equal((null, Repeat.Weekdays), Agenda.ParseOptions("every weekday"));
        Assert.Equal((null, Repeat.Daily), Agenda.ParseOptions("Every day"));
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions("whenever"));
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions("99999999999 min before"));     // nonsense, without throwing
        Assert.Equal((TimeSpan.FromMinutes(10), Repeat.Once), Agenda.ParseOptions("10 min before"));
        Assert.Equal((TimeSpan.Zero, Repeat.Once), Agenda.ParseOptions("at the time"));
        Assert.Equal((null, Repeat.Weekdays), Agenda.ParseOptions("Monday to Friday"));
        // a lead in other words is read whole, never by the part of it that looks like a lead of its own
        Assert.Equal((TimeSpan.FromMinutes(30), Repeat.Once), Agenda.ParseOptions("half an hour before"));
        Assert.Equal((TimeSpan.FromMinutes(30), Repeat.Daily), Agenda.ParseOptions("a half-hour before, daily"));
        Assert.Equal((TimeSpan.FromMinutes(15), Repeat.Once), Agenda.ParseOptions("a quarter of an hour before"));
        Assert.Equal((TimeSpan.FromMinutes(45), Repeat.Once), Agenda.ParseOptions("three quarters of an hour before"));
        Assert.Equal((TimeSpan.FromMinutes(45), Repeat.Once), Agenda.ParseOptions("three-quarter hour before"));
        Assert.Equal((TimeSpan.FromMinutes(90), Repeat.Once), Agenda.ParseOptions("an hour and a half before"));
        Assert.Equal((TimeSpan.FromMinutes(90), Repeat.Once), Agenda.ParseOptions("one and a half hour before"));
        Assert.Equal((TimeSpan.FromMinutes(90), Repeat.Once), Agenda.ParseOptions("1 hour 30 min before"));
        Assert.Equal((TimeSpan.FromMinutes(90), Repeat.Once), Agenda.ParseOptions("1 hour 30 minutes before"));
        Assert.Equal((TimeSpan.FromMinutes(90), Repeat.Once), Agenda.ParseOptions("1 hour and 30 minutes before"));
        Assert.Equal((TimeSpan.FromHours(26), Repeat.Once), Agenda.ParseOptions("1 day and 2 hours before"));
        Assert.Equal((TimeSpan.FromMinutes(30), Repeat.Once), Agenda.ParseOptions("a reminder of 30 min before"));
        // what isn't read whole isn't read at all
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions("half a day before"));
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions("a quarter of a day before"));
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions("1.5 hours before"));
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions("1,5 hours before"));
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions("1 week and 2 days before"));
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions("half a day and 2 hours before"));
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions("1 hour, 30 min before"));
        Assert.Equal((TimeSpan.FromMinutes(10), Repeat.Daily), Agenda.ParseOptions("every day, 10 min before"));
        // a lead of many parts stops at four, and never past the longest
        Assert.Equal((Agenda.LongestLead, Repeat.Once), Agenda.ParseOptions("9999 days 9999 days 9999 days 9999 days before"));
        Assert.Equal((null, Repeat.Once), Agenda.ParseOptions(string.Concat(Enumerable.Repeat("9999 days ", 1100)) + "before"));
        // as before: a lead right after a comma or against other words, and "beforehand"
        Assert.Equal((TimeSpan.FromHours(1), Repeat.Weekly), Agenda.ParseOptions("weekly,1 hour before"));
        Assert.Equal((TimeSpan.FromMinutes(30), Repeat.Daily), Agenda.ParseOptions("daily,30 min before"));
        Assert.Equal((TimeSpan.FromMinutes(10), Repeat.Once), Agenda.ParseOptions("10 minutes beforehand"));
        Assert.Equal((TimeSpan.FromHours(1), Repeat.Once), Agenda.ParseOptions("1 hour before每周"));
        Assert.Equal((TimeSpan.FromHours(1), Repeat.Once), Agenda.ParseOptions("每周1 hour before"));
        Assert.Equal((TimeSpan.FromMinutes(10), Repeat.Weekly), Agenda.ParseOptions("weekly and 10 min early"));

        var spec = Agenda.ParseDirection("2026-10-08 10:00 | Job interview | 1 hour before", remind: false, Friday)!;
        Assert.Equal((TimeSpan.FromHours(1), Repeat.Once), (spec.Before, spec.Repeat));
        spec = Agenda.ParseDirection("2026-10-03 08:00 | Take your vitamins | daily", remind: true, Friday)!;
        Assert.Equal((TimeSpan.Zero, Repeat.Daily), (spec.Before, spec.Repeat));
    }

    [Fact]
    public void A_reminder_kept_with_a_lead_too_long_goes_off_the_longest_lead_ahead_without_breaking_the_agenda()
    {
        var odd = P("Dentist", Friday.AddDays(3), before: TimeSpan.FromDays(800_000));

        Assert.Equal(Friday.AddDays(3) - Agenda.LongestLead, odd.RemindAt());
        Assert.Single(Agenda.Due([odd], Friday));
        Assert.Contains("Dentist", Agenda.Block([odd], Friday));
    }

    [Fact]
    public void Repeating_items_come_round_again_skipping_weekends_on_weekdays()
    {
        var standup = P("Stand-up", new DateTimeOffset(2026, 10, 2, 9, 30, 0, Friday.Offset), TimeSpan.FromMinutes(5), Repeat.Weekdays);
        Assert.Equal(DayOfWeek.Monday, Agenda.Next(standup.At, Repeat.Weekdays).DayOfWeek);
        // missed for days (the cabinet was closed): the next one still ahead, not each one in between
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 9, 30, 0, Friday.Offset), Agenda.NextAfter(standup, new DateTimeOffset(2026, 10, 7, 12, 0, 0, Friday.Offset)));
        Assert.Equal(standup.At, Agenda.NextAfter(standup with { Repeat = Repeat.Once }, Friday));
    }

    [Fact]
    public void Times_after_the_clocks_change_keep_their_time_of_day()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var october = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(-4));         // summer time

        Assert.Equal(TimeSpan.FromHours(-5), Agenda.ParseWhen("2026-11-05 15:00", october, eastern)!.Value.At.Offset);   // after 1 November: winter time
        Assert.Equal(TimeSpan.FromHours(-4), Agenda.ParseWhen("2026-10-25 15:00", october, eastern)!.Value.At.Offset);
        var vitamins = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.FromHours(-4));
        var next = Agenda.Next(vitamins, Repeat.Daily, eastern);
        Assert.Equal((8, TimeSpan.FromHours(-5)), (next.Hour, next.Offset));                      // still at 8 on the clock
    }

    [Fact]
    public void A_reminder_goes_off_ahead_of_its_item_and_the_default_lead_only_while_the_item_is_ahead()
    {
        var interview = P("Job interview", Friday.AddHours(2), TimeSpan.FromHours(1));
        Assert.Empty(Agenda.Due([interview], Friday.AddMinutes(59)));
        Assert.Single(Agenda.Due([interview], Friday.AddHours(1)));
        Assert.Single(Agenda.Due([interview], Friday.AddDays(1)));                          // asked for, missed: still worth a word

        var dinner = P("Dinner with Mia", Friday.AddHours(2));                                // no reminder of its own
        Assert.Empty(Agenda.Due([dinner], Friday.AddHours(1.6)));
        Assert.Single(Agenda.Due([dinner], Friday.AddHours(1.6), lead: TimeSpan.FromMinutes(30)));
        Assert.Empty(Agenda.Due([dinner], Friday.AddHours(3), lead: TimeSpan.FromMinutes(30)));   // over: a default one is not worth it
        Assert.Empty(Agenda.Due([P("Exam", Friday.Date.AddDays(1), allDay: true)], Friday.AddDays(1), lead: TimeSpan.FromMinutes(30)));   // whole days have no lead

        // added when its reminder time had already passed: only while the item is still ahead
        Assert.Single(Agenda.Due([P("Call", Friday.AddMinutes(10), TimeSpan.FromMinutes(30), added: Friday)], Friday));
        Assert.Empty(Agenda.Due([P("Call", Friday.AddMinutes(-10), TimeSpan.Zero, added: Friday)], Friday));

        var exam = P("Calculus exam", Friday.Date.AddDays(1), TimeSpan.FromDays(1), allDay: true);
        Assert.Equal(Friday.Date.AddHours(9), exam.RemindAt()!.Value.DateTime);              // the morning before
        Assert.Contains("Calculus exam (a reminder on Fri 2 Oct at 09:00)", Agenda.Block([exam], Friday.Date.AddHours(8)));
    }

    [Fact]
    public async Task Asking_again_with_a_reminder_or_a_repeat_adds_it_to_the_item_already_there()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        await conversation.Answer(new FakeModel("[plan: 2026-10-08 10:00 | Job interview at Acme] Good luck!"), "interview thursday at 10", "", _ => { }, (_, _) => { });
        await conversation.Answer(new FakeModel("[plan: 2026-10-08 10:00 | Job interview at Acme | 1 hour before] Will do."), "remind me an hour before", "", _ => { }, (_, _) => { });
        await conversation.Answer(new FakeModel("[remind: 2026-10-03 08:00 | Take your vitamins | daily] Every morning!"), "vitamins every day at 8", "", _ => { }, (_, _) => { });

        Assert.Equal([("Take your vitamins", TimeSpan.Zero, Repeat.Daily), ("Job interview at Acme", TimeSpan.FromHours(1), Repeat.Once)],
                     temp.Store.Agenda().Select(p => (p.Text, p.Before!.Value, p.Repeat)));
        var next = new FakeModel("Mm.");
        await conversation.Answer(next, "what's up", "", _ => { }, (_, _) => { });
        var (vitamins, interview) = (temp.Store.Agenda()[0].Id, temp.Store.Agenda()[1].Id);
        Assert.Contains($"- #{vitamins} Sat 3 Oct 08:00 (tomorrow): Take your vitamins (every day; a reminder goes off then)", next.Requests.Single()[^1].Text);
        Assert.Contains($"- #{interview} Thu 8 Oct 10:00 (in 6 days): Job interview at Acme (a reminder 1 hour before)", next.Requests.Single()[^1].Text);
    }

    [Fact]
    public async Task She_passes_a_reminder_on_in_her_own_words_and_it_is_logged_as_the_cabinet_s()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var interview = P("Job interview", Friday.AddHours(1), TimeSpan.FromHours(1));
        var mum = P("Call Mum", Friday.AddHours(-3), TimeSpan.Zero);
        var note = Agenda.Announcement([interview, mum], Friday);
        Assert.Equal("Reminders the player set are going off: \"Job interview\" (in an hour, Fri 2 Oct 20:30); " +
                     "\"Call Mum\" (came due 3 hours ago, while the cabinet was closed). Tell them in one to three short sentences, in your own words.", note);

        var model = new FakeModel("[wave] 面试还有一个小时哦！");
        var said = await conversation.Announce(model, note, "", _ => { }, (_, _) => { });

        Assert.Equal("面试还有一个小时哦！", said);
        Assert.Contains($"[cabinet] {note}", model.Requests.Single()[^1].Text);
        Assert.Equal([(Speaker.Cabinet, note), (Speaker.Her, "面试还有一个小时哦！")],
                     temp.Store.After("shino", 0).Select(l => (l.Speaker, l.Text)));
    }

    [Fact]
    public async Task A_reminder_she_could_not_say_is_not_said_late_in_her_next_reply()
    {
        // 2026-10-11: her words for a reminder failed on the provider's 503, the cabinet's request stayed in the talk, and
        // minutes after its ticket she said "Time to stretch" with the weather
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var stretch = Agenda.Announcement([P("Stretch", Friday, TimeSpan.Zero)], Friday);
        var water = Agenda.Announcement([P("Drink water", Friday, TimeSpan.Zero)], Friday);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            conversation.Announce(new FakeModel { Fails = new InvalidOperationException("HTTP 503") }, stretch, "", _ => { }, (_, _) => { }));
        var empty = new FakeModel();                                         // nothing back, twice
        Assert.Equal("", await conversation.Announce(empty, water, "", _ => { }, (_, _) => { }));
        Assert.Equal(2, empty.Requests.Count);
        Assert.Equal("", await conversation.Announce(new FakeModel("[quiet]"), water, "", _ => { }, (_, _) => { }));    // quiet is an answer

        Assert.Equal([(Speaker.Cabinet, water)], temp.Store.After("shino", 0).Select(l => (l.Speaker, l.Text)));
    }

    [Fact]
    public void A_repeating_item_without_a_reminder_moves_on_once_over()
    {
        using var temp = new TempStore();
        var gym = temp.Store.AddPlan("Gym", Friday.AddHours(-2), allDay: false, before: null, Friday.AddDays(-1), null, Repeat.Weekly);
        temp.Store.ClosePast(Friday);
        Assert.Equal(Friday.AddHours(-2).AddDays(7), temp.Store.Agenda().Single(p => p.Id == gym).At);
    }

    [Fact]
    public void Reminders_kept_before_lead_times_existed_still_go_off_at_their_time()
    {
        var folder = Path.Combine(Path.GetTempPath(), "insert-coin-mind-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "memory.db");
        using (var old = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            old.Open();
            using var create = old.CreateCommand();
            create.CommandText = """
                CREATE TABLE agenda (id INTEGER PRIMARY KEY, text TEXT NOT NULL, at TEXT NOT NULL, all_day INTEGER NOT NULL, remind INTEGER NOT NULL,
                                     reminded TEXT, added TEXT NOT NULL, by TEXT, closed TEXT);
                INSERT INTO agenda (text, at, all_day, remind, added) VALUES ('Call Mum', '2026-10-02T21:00:00.0000000+08:00', 0, 1, '2026-10-02T19:00:00.0000000+08:00');
                INSERT INTO agenda (text, at, all_day, remind, added) VALUES ('Exam', '2026-10-09T00:00:00.0000000+08:00', 1, 0, '2026-10-02T19:00:00.0000000+08:00');
                PRAGMA user_version = 5;
                """;
            create.ExecuteNonQuery();
        }
        try
        {
            using var store = new MindStore(path);
            Assert.Equal([("Call Mum", (TimeSpan?)TimeSpan.Zero), ("Exam", null)], store.Agenda().Select(p => (p.Text, p.Before)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
