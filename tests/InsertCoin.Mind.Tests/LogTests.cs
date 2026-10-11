using Microsoft.Data.Sqlite;

namespace InsertCoin.Mind.Tests;

/// <summary>Her memory of what has left the window: a dated log of observations (see <see cref="Log"/>).</summary>
public class LogTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    [Fact]
    public void Her_notes_are_laid_out_by_day_with_how_long_ago_and_a_mark_on_what_matters_most()
    {
        Observation[] log =
        [
            new(1, Friday.AddDays(-9), Priority.High, "The player has an interview at Acme on Friday 9 October.", 1, 4),
            new(2, Friday.AddDays(-9).AddHours(1), Priority.Low, "They chatted about the rain.", 5, 6),
            new(3, Friday.AddDays(-1), Priority.Medium, "The player solved Two Sum.", 7, 9),
        ];

        Assert.Equal(string.Join("\n", "Wed 23 Sep 2026 (9 days ago)", "- ! The player has an interview at Acme on Friday 9 October.",
                                       "- They chatted about the rain.", "Thu 1 Oct 2026 (yesterday)", "- The player solved Two Sum."),
                     Log.Laid(log, Friday));
        Assert.DoesNotContain("ago", Log.Laid(log, null));
        // whole days only, so what she reads of her notes changes at midnight, not every hour, and the cached prompt holds
        Assert.Equal(Log.Laid(log, Friday.AddHours(-19)), Log.Laid(log, Friday.AddHours(4)));
    }

    [Fact]
    public async Task She_sees_her_newest_notes_that_fit_and_older_ones_are_recalled_when_they_come_up()
    {
        using var temp = new TempStore();
        var line = temp.Store.Add("shino", Speaker.User, "earlier", Friday.AddDays(-60));
        temp.Store.Observe("shino", [new Observation(0, Friday.AddDays(-60), Priority.High, "The player's dentist appointment is on Monday 10 August at 15:00.", line, line)],
                           line, Friday.AddDays(-60));
        for (var i = 0; i < 40; i++)
            temp.Store.Observe("shino", [new Observation(0, Friday.AddDays(-50 + i), Priority.Medium, $"Day {i}: the player practised graphs {new string('x', 300)}.", line, line)],
                               line, Friday.AddDays(-50 + i));
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday)) { Window = 8000 };     // 3,000 tokens of notes in view
        var model = new FakeModel("Yes, the dentist!");

        await conversation.Answer(model, "when was my dentist appointment again?", "", _ => { }, (_, _) => { });

        var system = model.Requests.Single()[0].Text;
        Assert.Contains("Day 39:", system);
        Assert.DoesNotContain("Day 0:", system);
        Assert.DoesNotContain("dentist", system);
        Assert.Contains("(2 months ago): The player's dentist appointment is on Monday 10 August at 15:00.", model.Requests.Single()[^1].Text);
    }

    [Fact]
    public async Task An_answer_that_is_not_the_notes_asked_for_keeps_the_lines_and_the_third_lets_them_go_unnoted()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock) { Window = 4000 };
        for (var i = 0; i < 60; i++)
            temp.Store.Add("shino", i % 2 == 0 ? Speaker.User : Speaker.Her, $"line {i} " + new string('x', 400), Friday.AddMinutes(i));
        clock.Now = Friday.AddMinutes(60);
        var model = new FakeModel("Sorry, I can't help with that.");

        Assert.False(await conversation.Fold(model));
        Assert.False(await conversation.Fold(model));
        Assert.Equal(0, temp.Store.Folded("shino"));
        Assert.True(await conversation.Fold(model));            // they leave without notes, still searchable word for word

        Assert.Empty(temp.Store.Observations("shino"));
        Assert.False(conversation.NeedsFolding);
    }

    [Fact]
    public async Task A_long_fold_is_observed_a_batch_at_a_time_each_with_her_latest_notes()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock) { Window = 32_000 };
        for (var i = 0; i < 400; i++)
            temp.Store.Add("shino", i % 2 == 0 ? Speaker.User : Speaker.Her, $"line {i} " + new string('x', 400), Friday.AddMinutes(i));
        clock.Now = Friday.AddMinutes(400);
        var model = new FakeModel("""{"observations": [{"date": "2026-10-02", "priority": "medium", "text": "They kept talking."}]}""");

        Assert.True(await conversation.Fold(model));

        Assert.True(model.Requests.Count >= 3);
        Assert.Equal(model.Requests.Count, temp.Store.Observations("shino").Count);
        Assert.DoesNotContain("Her latest notes", model.Requests[0][1].Text);
        Assert.Contains("Her latest notes, for what came just before (don't repeat them):\nFri 2 Oct 2026\n- They kept talking.", model.Requests[1][1].Text);
    }

    [Fact]
    public void Each_note_is_dated_by_the_day_it_names_or_else_by_the_batch_s_last_day()
    {
        Line[] lines =
        [
            new(5, Speaker.User, "I start at Acme on Monday", Friday),
            new(6, Speaker.Her, "Congratulations!", Friday.AddMinutes(1)),
            new(7, Speaker.User, "morning", Friday.AddDays(1)),
            new(8, Speaker.Her, "Morning!", Friday.AddDays(1).AddMinutes(1)),
        ];
        var observed = Log.Parse("""
            Here you go: {"observations": [
              {"date": "2026-10-02", "priority": "high", "text": "The player starts at Acme on Monday 5 October."},
              {"date": "1999-01-01", "priority": "whatever", "text": "They said good morning."},
              {"date": "2026-10-03", "text": "ok"}]}
            """, lines)!;

        Assert.Equal([(Friday, Priority.High, 5L, 6L), (Friday.AddDays(1), Priority.Medium, 7L, 8L)], observed.Select(o => (o.At, o.Priority, o.First, o.Last)));
        Assert.Null(Log.Parse("not json", lines));
        Assert.Empty(Log.Parse("""{"observations": []}""", lines)!);
    }

    // ---- Condensing ------------------------------------------------------------------------------------------------

    /// <summary>A note a day from Monday 24 August to Thursday 1 October 2026, each about a hundred tokens: far over 1,500.</summary>
    private static void Daily(MindStore store, string first = "The player practised graphs.")
    {
        for (var i = 0; i < 39; i++)
            store.Observe("shino", [new Observation(0, Friday.AddDays(-39 + i).AddHours(-7), Priority.Medium, (i == 0 ? first : $"Day {i}: graphs.") + " " + new string('x', 380), 1, 1)],
                          1, Friday.AddDays(-39 + i));
    }

    [Fact]
    public async Task When_her_notes_outgrow_their_room_the_oldest_finished_week_is_condensed_and_its_notes_stay_recallable()
    {
        using var temp = new TempStore();
        Daily(temp.Store, first: "The player adopted a kitten named Miso.");
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday)) { Window = 4000 };     // 1,500 tokens of notes in view
        var model = new FakeModel("""{"observations": [{"priority": "high", "text": "That week the player settled into a daily graph practice."}]}""");

        Assert.True(await conversation.Condense(model));

        Assert.Contains("Below are her notes from the week starting Monday 24 August 2026", model.Requests.Single()[0].Text);
        Assert.StartsWith("Mon 24 Aug 2026\n- The player adopted a kitten named Miso.", model.Requests.Single()[1].Text);
        var log = temp.Store.Observations("shino");
        var week = log.Single(o => o.Span == Span.Week);
        Assert.Equal((new DateTimeOffset(2026, 8, 24, 0, 0, 0, Friday.Offset), Priority.High, false), (week.At, week.Priority, week.Condensed));
        Assert.Equal(7, log.Count(o => o.Condensed));
        Assert.StartsWith("Week of 24 Aug 2026\n- ! That week the player settled into a daily graph practice.\nMon 31 Aug 2026",
                          Log.Laid([.. log.Where(o => !o.Condensed)], null));

        // the original is out of view, and still comes back when it comes up
        var answer = new FakeModel("Miso!");
        await conversation.Answer(answer, "what was my kitten called again?", "", _ => { }, (_, _) => { });
        Assert.DoesNotContain("Miso", answer.Requests.Single()[0].Text);
        Assert.Contains("The player adopted a kitten named Miso.", answer.Requests.Single()[^1].Text);
    }

    [Fact]
    public async Task A_lone_note_moves_up_a_level_without_asking_and_a_week_still_going_on_is_left_as_it_is()
    {
        using var temp = new TempStore();
        temp.Store.Observe("shino", [new Observation(0, Friday.AddDays(-40), Priority.Low, "The player mentioned rain.", 1, 1)], 1, Friday.AddDays(-40));
        for (var i = 0; i < 30; i++)                                  // this week's notes alone are over the room
            temp.Store.Observe("shino", [new Observation(0, Friday.AddHours(-i), Priority.Medium, $"Hour {i}. " + new string('x', 380), 1, 1)], 1, Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday)) { Window = 4000 };
        var model = new FakeModel("unused");

        Assert.True(await conversation.Condense(model));               // Sun 23 Aug's week had one note: moved up as it was
        Assert.Empty(model.Requests);
        Assert.Equal(("The player mentioned rain.", Span.Week), temp.Store.Observations("shino").Where(o => !o.Condensed).Select(o => (o.Text, o.Span)).First());

        Assert.True(await conversation.Condense(model));               // and its month, finished too
        Assert.False(await conversation.Condense(model));              // then only the week still going on is left: nothing to do
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void Weeks_go_into_their_month_once_no_finished_week_of_days_is_left_and_nothing_is_condensed_while_the_notes_fit()
    {
        var august = new DateTimeOffset(2026, 8, 1, 0, 0, 0, Friday.Offset);
        List<Observation> log =
        [
            new(1, august.AddDays(2), Priority.High, "Week one. " + new string('x', 2000), 1, 1, Span.Week),
            new(2, august.AddDays(9), Priority.Medium, "Week two. " + new string('x', 2000), 1, 1, Span.Week),
            new(3, august.AddDays(5), Priority.Low, "Condensed already.", 1, 1, Span.Day, Condensed: true),
            new(4, Friday, Priority.Medium, "Today. " + new string('x', 2000), 1, 1),
        ];

        var stretch = Log.ToCondense(log, 4000, Friday)!;

        Assert.Equal((Span.Month, august, "August 2026"), (stretch.Span, stretch.At, stretch.Period));
        Assert.Equal([1L, 2L], stretch.Notes.Select(n => n.Id));
        Assert.Null(Log.ToCondense(log, 100_000, Friday));             // everything fits
    }

    [Fact]
    public void A_summary_from_before_becomes_her_first_note_once()
    {
        var folder = Path.Combine(Path.GetTempPath(), "insert-coin-mind-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "memory.db");
        using (var old = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            old.Open();
            using var command = old.CreateCommand();
            command.CommandText = """
                CREATE TABLE summaries (character TEXT PRIMARY KEY, text TEXT NOT NULL, through INTEGER NOT NULL, at TEXT NOT NULL);
                INSERT INTO summaries VALUES ('shino', 'They talked about the interview.', 42, '2026-10-02T19:30:00.0000000+08:00');
                """;
            command.ExecuteNonQuery();
        }

        using (var store = new MindStore(path))
        {
            var note = store.Observations("shino").Single();
            Assert.Equal(("They talked about the interview.", Priority.High, 42L, Friday), (note.Text, note.Priority, note.Last, note.At));
            Assert.Equal(42, store.Folded("shino"));
        }
        using (var again = new MindStore(path))
            Assert.Single(again.Observations("shino"));
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }
}
