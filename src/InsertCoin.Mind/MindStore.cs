using System.Globalization;
using Microsoft.Data.Sqlite;

namespace InsertCoin.Mind;

public enum Speaker { User, Her, Cabinet }

/// <summary>One line of the conversation: something the user typed, something she said, or something the cabinet did.</summary>
public sealed record Line(long Id, Speaker Speaker, string Text, DateTimeOffset At);

/// <summary>
/// Something she knows: about the player (<see cref="MindStore.Shared"/> scope, every character knows it) or about
/// the two of them (a character's id). Importance 1–10; recalled is when it last came up in a conversation, recalls
/// how often it has.
/// </summary>
public sealed record Fact(long Id, string Scope, string Text, int Importance, DateTimeOffset Learned, DateTimeOffset? Recalled, bool Pinned,
                          int Recalls = 0, DateTimeOffset? Retired = null, string? Why = null)
{
    /// <summary>
    /// How well she still remembers it, 1 down to 0: MemoryBank's forgetting curve, e^(-t/S), where t is the days
    /// since it last came up and the strength S grows with importance and with every recall.
    /// </summary>
    public double Retention(DateTimeOffset now) =>
        Pinned || Importance >= 9 ? 1 : Math.Exp(-(now - (Recalled ?? Learned)).TotalDays / (10.0 * Importance * (1 + Recalls)));

    /// <summary>Faded: left out of what she recalls (it stays in the memory book, and pinning brings it back).</summary>
    public bool Faded(DateTimeOffset now) => Retention(now) < 0.1;
}

/// <summary>The stretch of time a note of hers covers: a day's talk, or older notes condensed (see <see cref="Conversation.Condense"/>).</summary>
public enum Span { Day, Week, Month, Year }

/// <summary>
/// Everything she remembers, in one SQLite file in the profile folder: each character's conversation (one continuous
/// thread), the log of what she observed in its older part (see <see cref="Log"/>), the notes that are always in her
/// context, the facts upkeep keeps from her notes, and how far upkeep has read.
/// </summary>
public sealed class MindStore : IDisposable
{
    /// <summary>Notes about the user, shared by every character.</summary>
    public const string Shared = "*";
    public const string AboutUser = "about", Us = "us";

    private readonly SqliteConnection _db;

    public MindStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        long was;                                           // the version this store was at: steps that run once go by it
        using (var version = Command("PRAGMA user_version"))
            was = (long)version.ExecuteScalar()!;
        Run("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS lines (
                id INTEGER PRIMARY KEY, character TEXT NOT NULL, speaker TEXT NOT NULL, text TEXT NOT NULL, at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS lines_by_character ON lines (character, id);
            CREATE TABLE IF NOT EXISTS summaries (
                character TEXT PRIMARY KEY, text TEXT NOT NULL, through INTEGER NOT NULL, at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS notes (
                scope TEXT NOT NULL, name TEXT NOT NULL, text TEXT NOT NULL, PRIMARY KEY (scope, name));
            CREATE TABLE IF NOT EXISTS facts (
                id INTEGER PRIMARY KEY, scope TEXT NOT NULL, text TEXT NOT NULL, importance INTEGER NOT NULL,
                learned TEXT NOT NULL, recalled TEXT, pinned INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS episodes (
                id INTEGER PRIMARY KEY, character TEXT NOT NULL, text TEXT NOT NULL, first INTEGER NOT NULL, last INTEGER NOT NULL, at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS digested (character TEXT PRIMARY KEY, through INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS passages (
                id INTEGER PRIMARY KEY, character TEXT NOT NULL, first INTEGER NOT NULL, last INTEGER NOT NULL, at TEXT NOT NULL, text TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS passages_by_character ON passages (character, last);
            CREATE TABLE IF NOT EXISTS vectors (
                kind TEXT NOT NULL, id INTEGER NOT NULL, model TEXT NOT NULL, vector BLOB NOT NULL, PRIMARY KEY (kind, id, model));
            """);
        // version 3: facts count their recalls (a file from version 2 gets the column)
        using (var columns = Command("SELECT count(*) FROM pragma_table_info('facts') WHERE name = 'recalls'"))
            if ((long)columns.ExecuteScalar()! == 0)
                Run("ALTER TABLE facts ADD COLUMN recalls INTEGER NOT NULL DEFAULT 0");
        // version 4: episodes can be a week's digest; the column holds the span since (0 day, 1 week, 2 month, 3 year)
        using (var columns = Command("SELECT count(*) FROM pragma_table_info('episodes') WHERE name = 'week'"))
            if ((long)columns.ExecuteScalar()! == 0)
                Run("ALTER TABLE episodes ADD COLUMN week INTEGER NOT NULL DEFAULT 0");
        // version 5: facts can be retired (no longer recalled, kept to restore), and small marks such as when facts were last reviewed
        using (var columns = Command("SELECT count(*) FROM pragma_table_info('facts') WHERE name = 'retired'"))
            if ((long)columns.ExecuteScalar()! == 0)
                Run("ALTER TABLE facts ADD COLUMN retired TEXT; ALTER TABLE facts ADD COLUMN retired_why TEXT");
        Run("CREATE TABLE IF NOT EXISTS marks (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        // the player's agenda (see Agenda): shared by every character, like what she knows about the player
        Run("""
            CREATE TABLE IF NOT EXISTS agenda (
                id INTEGER PRIMARY KEY, text TEXT NOT NULL, at TEXT NOT NULL, all_day INTEGER NOT NULL, remind INTEGER NOT NULL,
                reminded TEXT, added TEXT NOT NULL, by TEXT, closed TEXT)
            """);
        // version 6: agenda reminders a while before an item (minutes; null: none), and items that repeat
        using (var columns = Command("SELECT count(*) FROM pragma_table_info('agenda') WHERE name = 'before_minutes'"))
            if ((long)columns.ExecuteScalar()! == 0)
                Run("ALTER TABLE agenda ADD COLUMN before_minutes INTEGER; ALTER TABLE agenda ADD COLUMN repeat TEXT; UPDATE agenda SET before_minutes = 0 WHERE remind = 1");
        // version 7: what requests used, a row a day per model and purpose (see Metered); agenda items from a calendar,
        // and when she asked how one went
        Run("""
            CREATE TABLE IF NOT EXISTS usage (
                day TEXT NOT NULL, model TEXT NOT NULL, purpose TEXT NOT NULL, requests INTEGER NOT NULL, input INTEGER NOT NULL, cached INTEGER NOT NULL,
                written INTEGER NOT NULL, output INTEGER NOT NULL, reasoning INTEGER NOT NULL, PRIMARY KEY (day, model, purpose))
            """);
        using (var columns = Command("SELECT count(*) FROM pragma_table_info('agenda') WHERE name = 'source'"))
            if ((long)columns.ExecuteScalar()! == 0)
                Run("ALTER TABLE agenda ADD COLUMN source TEXT; ALTER TABLE agenda ADD COLUMN uid TEXT; ALTER TABLE agenda ADD COLUMN asked TEXT");
        // version 8: memos, long text on paper (see Memo): the player's, shared by every character
        Run("""
            CREATE TABLE IF NOT EXISTS memos (
                id INTEGER PRIMARY KEY, title TEXT NOT NULL, text TEXT NOT NULL, by TEXT, at TEXT NOT NULL, place TEXT NOT NULL, pin INTEGER NOT NULL DEFAULT 0)
            """);
        // version 9: a memo's paper and sticker, and where on the cabinet's side it is pinned (a spot and a tilt, not a slot)
        using (var columns = Command("SELECT count(*) FROM pragma_table_info('memos') WHERE name = 'colour'"))
            if ((long)columns.ExecuteScalar()! == 0)
                Run("ALTER TABLE memos ADD COLUMN colour TEXT; ALTER TABLE memos ADD COLUMN sticker TEXT; " +
                    "ALTER TABLE memos ADD COLUMN a REAL; ALTER TABLE memos ADD COLUMN b REAL; ALTER TABLE memos ADD COLUMN tilt REAL NOT NULL DEFAULT 0");
        // version 10: the activities that count, as she sized them (see Activities), her verdicts on the player's proofs, and
        // activities waiting for proof on a day
        Run("""
            CREATE TABLE IF NOT EXISTS activities (name TEXT PRIMARY KEY, size TEXT NOT NULL, provable INTEGER NOT NULL, added TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS proofs (id INTEGER PRIMARY KEY, at TEXT NOT NULL, activity TEXT NOT NULL, verdict TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS claims (day TEXT NOT NULL, activity TEXT NOT NULL, at TEXT NOT NULL, PRIMARY KEY (day, activity));
            """);
        // version 11: her memory of what has left the window is a dated log of observations (see Log), never rewritten;
        // summaries keeps only how far the window has been folded, and a summary written before becomes the log's first
        // observation (once: its text is cleared)
        Run("""
            CREATE TABLE IF NOT EXISTS observations (
                id INTEGER PRIMARY KEY, character TEXT NOT NULL, at TEXT NOT NULL, priority INTEGER NOT NULL, text TEXT NOT NULL,
                first INTEGER NOT NULL, last INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS observations_by_character ON observations (character, at);
            INSERT INTO observations (character, at, priority, text, first, last) SELECT character, at, 0, text, 1, through FROM summaries WHERE text <> '';
            UPDATE summaries SET text = '' WHERE text <> '';
            """);
        // version 12: notes can be condensed, a week of days into the week and so on (see Conversation.Condense): the span
        // a note covers, and whether it was condensed into a longer one (kept out of view, still recalled)
        using (var columns = Command("SELECT count(*) FROM pragma_table_info('observations') WHERE name = 'span'"))
            if ((long)columns.ExecuteScalar()! == 0)
                Run("ALTER TABLE observations ADD COLUMN span INTEGER NOT NULL DEFAULT 0; ALTER TABLE observations ADD COLUMN condensed INTEGER NOT NULL DEFAULT 0; " +
                    "PRAGMA user_version = 12;");
        // version 13: the day's moments retire into her log, a note each of its span (they were a second summary of the
        // same talk); her facts were drawn from all of it already, so facts are kept from the notes after these. Once only:
        // run again (the steps above used to set the version back each time), it passed over notes whose facts were yet
        // to be kept, so a restart between noting and keeping lost what they told of
        if (was < 13)
            Run("""
                INSERT INTO observations (character, at, priority, text, first, last, span, condensed)
                    SELECT character, at, 1, text, first, last, week, 0 FROM episodes ORDER BY at, id;
                DELETE FROM episodes;
                DELETE FROM vectors WHERE kind = 'episode';
                INSERT OR REPLACE INTO marks (key, value) SELECT 'kept:' || character, max(id) FROM observations GROUP BY character;
                PRAGMA user_version = 13;
                """);
        // version 14: what she always has in mind is listed from her facts for every reply (see Prompt.Known), so the two
        // notes the model rewrote from them go into her log, out of view like condensed notes and still recalled, and the
        // memory book's Notes are the player's own from now on
        if (was < 14)
            Run("""
                INSERT INTO observations (character, at, priority, text, first, last, span, condensed)
                    SELECT l.character, max(l.at), 1, 'Her note about the player, as it stood: ' || n.text, 0, 0, 0, 1
                    FROM notes n, lines l WHERE n.scope = '*' AND n.name = 'about' AND n.text <> '' GROUP BY l.character;
                INSERT INTO observations (character, at, priority, text, first, last, span, condensed)
                    SELECT n.scope, (SELECT max(at) FROM lines WHERE character = n.scope), 1, 'Her note about the two of them, as it stood: ' || n.text, 0, 0, 0, 1
                    FROM notes n WHERE n.name = 'us' AND n.text <> '' AND EXISTS (SELECT 1 FROM lines WHERE character = n.scope);
                UPDATE notes SET text = '' WHERE (name = 'about' AND EXISTS (SELECT 1 FROM lines))
                    OR (name = 'us' AND EXISTS (SELECT 1 FROM lines WHERE character = notes.scope));
                PRAGMA user_version = 14;
                """);
    }

    // ---- Vectors (what facts, notes and old lines mean, for search by meaning) -------------------------------------

    public const string FactKind = "fact", PassageKind = "passage", ObservationKind = "observation";

    /// <summary>Counts clears and forgets, so what is cached from the store (see <see cref="History"/>) knows to read it again.</summary>
    public int Changes { get; private set; }

    public void SetVector(string kind, long id, string model, float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        using var command = Command("INSERT OR REPLACE INTO vectors (kind, id, model, vector) VALUES ($k, $i, $m, $v)",
            ("$k", kind), ("$i", id), ("$m", model), ("$v", bytes));
        command.ExecuteNonQuery();
    }

    /// <summary>The vectors a model has made for facts, notes or passages, by id.</summary>
    public Dictionary<long, float[]> Vectors(string kind, string model)
    {
        using var command = Command("SELECT id, vector FROM vectors WHERE kind = $k AND model = $m", ("$k", kind), ("$m", model));
        using var reader = command.ExecuteReader();
        var vectors = new Dictionary<long, float[]>();
        while (reader.Read())
        {
            var bytes = (byte[])reader[1];
            var vector = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            vectors[reader.GetInt64(0)] = vector;
        }
        return vectors;
    }

    /// <summary>Drops vectors whose fact, note or passage is gone or has changed (it gets a new one at the next indexing).</summary>
    private void DropVectors(string kind, long? id = null)
    {
        using var command = id is { } one
            ? Command("DELETE FROM vectors WHERE kind = $k AND id = $i", ("$k", kind), ("$i", one))
            : Command(kind switch
            {
                FactKind => "DELETE FROM vectors WHERE kind = $k AND id NOT IN (SELECT id FROM facts)",
                PassageKind => "DELETE FROM vectors WHERE kind = $k AND id NOT IN (SELECT id FROM passages)",
                _ => "DELETE FROM vectors WHERE kind = $k AND id NOT IN (SELECT id FROM observations)",
            }, ("$k", kind));
        command.ExecuteNonQuery();
    }

    // ---- Facts -----------------------------------------------------------------------------------------------------

    public long AddFact(string scope, string text, int importance, DateTimeOffset at)
    {
        using var command = Command("INSERT INTO facts (scope, text, importance, learned) VALUES ($s, $t, $i, $a) RETURNING id",
            ("$s", scope), ("$t", text.Trim()), ("$i", Math.Clamp(importance, 1, 10)), ("$a", Stamp(at)));
        return (long)command.ExecuteScalar()!;
    }

    public void UpdateFact(long id, string text, int? importance = null)
    {
        using var command = Command("UPDATE facts SET text = $t, importance = coalesce($i, importance) WHERE id = $id",
            ("$id", id), ("$t", text.Trim()), ("$i", importance is { } i ? Math.Clamp(i, 1, 10) : DBNull.Value));
        command.ExecuteNonQuery();
        DropVectors(FactKind, id);
    }

    public void PinFact(long id, bool pinned)
    {
        using var command = Command("UPDATE facts SET pinned = $p WHERE id = $id", ("$id", id), ("$p", pinned ? 1 : 0));
        command.ExecuteNonQuery();
    }

    public void DeleteFact(long id)
    {
        using var command = Command("DELETE FROM facts WHERE id = $id", ("$id", id));
        command.ExecuteNonQuery();
        DropVectors(FactKind, id);
    }

    /// <summary>What this character knows: facts about the player and about the two of them.</summary>
    /// <summary>What she knows, about the player and about the two of them: the facts not retired.</summary>
    public IReadOnlyList<Fact> Facts(string character) => ReadFacts(character, retired: false);

    /// <summary>Facts retired by a review (see <see cref="Conversation.Review"/>): no longer recalled, kept to restore.</summary>
    public IReadOnlyList<Fact> Retired(string character) => ReadFacts(character, retired: true);

    private List<Fact> ReadFacts(string character, bool retired)
    {
        using var command = Command($"SELECT id, scope, text, importance, learned, recalled, pinned, recalls, retired, retired_why FROM facts " +
                                    $"WHERE scope IN ($s, $c) AND retired IS {(retired ? "NOT " : "")}NULL ORDER BY id", ("$s", Shared), ("$c", character));
        using var reader = command.ExecuteReader();
        var facts = new List<Fact>();
        while (reader.Read())
            facts.Add(new Fact(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), Parse(reader.GetString(4)),
                               reader.IsDBNull(5) ? null : Parse(reader.GetString(5)), reader.GetInt32(6) != 0, reader.GetInt32(7),
                               reader.IsDBNull(8) ? null : Parse(reader.GetString(8)), reader.IsDBNull(9) ? null : reader.GetString(9)));
        return facts;
    }

    /// <summary>Moves a fact to the background, saying why; pinned ones are left alone.</summary>
    public void Retire(long id, string why, DateTimeOffset at)
    {
        using var command = Command("UPDATE facts SET retired = $a, retired_why = $w WHERE id = $id AND pinned = 0", ("$a", Stamp(at)), ("$w", why), ("$id", id));
        command.ExecuteNonQuery();
    }

    /// <summary>Brings a retired fact back, fresh (it counts as just come up).</summary>
    public void Restore(long id, DateTimeOffset at)
    {
        using var command = Command("UPDATE facts SET retired = NULL, retired_why = NULL, recalled = $a WHERE id = $id", ("$a", Stamp(at)), ("$id", id));
        command.ExecuteNonQuery();
    }

    /// <summary>A small value kept between runs (when facts were last reviewed, say), or null.</summary>
    public string? Mark(string key)
    {
        using var command = Command("SELECT value FROM marks WHERE key = $k", ("$k", key));
        return command.ExecuteScalar() as string;
    }

    public void SetMark(string key, string value)
    {
        using var command = Command("INSERT INTO marks (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = excluded.value", ("$k", key), ("$v", value));
        command.ExecuteNonQuery();
    }

    /// <summary>These facts came up again: they count as fresh and a little stronger.</summary>
    public void Recalled(IEnumerable<long> ids, DateTimeOffset at)
    {
        foreach (var id in ids)
        {
            using var command = Command("UPDATE facts SET recalled = $a, recalls = recalls + 1 WHERE id = $id", ("$id", id), ("$a", Stamp(at)));
            command.ExecuteNonQuery();
        }
    }

    // ---- The agenda ------------------------------------------------------------------------------------------------

    public long AddPlan(string text, DateTimeOffset at, bool allDay, TimeSpan? before, DateTimeOffset added, string? by, Repeat repeat = Repeat.Once)
    {
        using var command = Command("""
            INSERT INTO agenda (text, at, all_day, remind, before_minutes, repeat, added, by) VALUES ($t, $a, $d, $r, $m, $p, $n, $b) RETURNING id
            """, ("$t", text.Trim()), ("$a", Stamp(at)), ("$d", allDay ? 1 : 0), ("$r", before is null ? 0 : 1), ("$m", Minutes(before)),
            ("$p", Repeated(repeat)), ("$n", Stamp(added)), ("$b", (object?)by ?? DBNull.Value));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>What is on the agenda, by time: items not yet closed (see <see cref="ClosePast"/>).</summary>
    public IReadOnlyList<Plan> Agenda()
    {
        using var command = Command("SELECT id, text, at, all_day, before_minutes, reminded, added, by, repeat, source, asked FROM agenda WHERE closed IS NULL ORDER BY at, id");
        using var reader = command.ExecuteReader();
        var plans = new List<Plan>();
        while (reader.Read())
            plans.Add(new Plan(reader.GetInt64(0), reader.GetString(1), Parse(reader.GetString(2)), reader.GetInt32(3) != 0,
                               reader.IsDBNull(4) ? null : TimeSpan.FromMinutes(reader.GetInt64(4)),
                               reader.IsDBNull(5) ? null : Parse(reader.GetString(5)), Parse(reader.GetString(6)), reader.IsDBNull(7) ? null : reader.GetString(7),
                               reader.IsDBNull(8) || !Enum.TryParse<Repeat>(reader.GetString(8), out var repeat) ? Repeat.Once : repeat,
                               reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : Parse(reader.GetString(10))));
        return plans;
    }

    /// <summary>Changes an item; a reminder moved to a new time (or to a new lead) goes off again.</summary>
    public void UpdatePlan(long id, string text, DateTimeOffset at, bool allDay, TimeSpan? before, Repeat repeat)
    {
        using var command = Command("""
            UPDATE agenda SET text = $t, all_day = $d, remind = $r, repeat = $p,
                reminded = CASE WHEN at = $a AND before_minutes IS $m THEN reminded ELSE NULL END, at = $a, before_minutes = $m
            WHERE id = $id
            """, ("$t", text.Trim()), ("$a", Stamp(at)), ("$d", allDay ? 1 : 0), ("$r", before is null ? 0 : 1), ("$m", Minutes(before)),
            ("$p", Repeated(repeat)), ("$id", id));
        command.ExecuteNonQuery();
    }

    /// <summary>An event from a calendar (see <see cref="CalendarSync"/>): marked with the calendar's name and the event's id there.</summary>
    public long AddCalendarPlan(string source, string uid, string text, DateTimeOffset at, bool allDay, DateTimeOffset added)
    {
        using var command = Command("""
            INSERT INTO agenda (text, at, all_day, remind, added, source, uid) VALUES ($t, $a, $d, 0, $n, $s, $u) RETURNING id
            """, ("$t", text.Trim()), ("$a", Stamp(at)), ("$d", allDay ? 1 : 0), ("$n", Stamp(added)), ("$s", source), ("$u", uid));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>The open items read from <paramref name="source"/>, by the event's id there.</summary>
    public Dictionary<string, long> CalendarUids(string source)
    {
        using var command = Command("SELECT uid, id FROM agenda WHERE source = $s AND uid IS NOT NULL AND closed IS NULL", ("$s", source));
        using var reader = command.ExecuteReader();
        var uids = new Dictionary<string, long>();
        while (reader.Read())
            uids[reader.GetString(0)] = reader.GetInt64(1);
        return uids;
    }

    /// <summary>A calendar is no longer read: its items leave the agenda.</summary>
    public void DeletePlansFrom(string source)
    {
        using var command = Command("DELETE FROM agenda WHERE source = $s", ("$s", source));
        command.ExecuteNonQuery();
    }

    /// <summary>She asked how it went.</summary>
    public void Asked(long id, DateTimeOffset at)
    {
        using var command = Command("UPDATE agenda SET asked = $a WHERE id = $id", ("$a", Stamp(at)), ("$id", id));
        command.ExecuteNonQuery();
    }

    /// <summary>A repeating item moves on to its next time, its reminder to go off again.</summary>
    public void Advance(long id, DateTimeOffset at)
    {
        using var command = Command("UPDATE agenda SET at = $a, reminded = NULL WHERE id = $id", ("$a", Stamp(at)), ("$id", id));
        command.ExecuteNonQuery();
    }

    public void DeletePlan(long id)
    {
        using var command = Command("DELETE FROM agenda WHERE id = $id", ("$id", id));
        command.ExecuteNonQuery();
    }

    // ---- Memos -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A new memo, hanging from the slot; the one hanging there before goes to the archive. Titles and text too long
    /// are cut to <see cref="Memo.TitleLength"/> and <see cref="Memo.TextLength"/>.
    /// </summary>
    /// <param name="by">The character who printed it, or null for one the player wrote.</param>
    /// <param name="place">Where it goes: hanging from the slot, or straight to the archive (not pinned: that needs a spot, see <see cref="PinMemo"/>).</param>
    /// <param name="colour">Its paper; unset, <see cref="Memo.ColourFor"/>.</param>
    public long AddMemo(string title, string text, string? by, DateTimeOffset at, MemoPlace place = MemoPlace.Hanging, string? colour = null, string? sticker = null)
    {
        if (place == MemoPlace.Hanging)
            Run($"UPDATE memos SET place = '{MemoPlace.Archived}' WHERE place = '{MemoPlace.Hanging}'");
        title = title.Trim();
        text = text.Trim();
        using var command = Command("INSERT INTO memos (title, text, by, at, place, colour, sticker) VALUES ($t, $x, $b, $a, $p, $c, $s) RETURNING id",
            ("$t", Prompt.Fit(title, Memo.TitleLength)), ("$x", Prompt.Fit(text, Memo.TextLength)),
            ("$b", (object?)by ?? DBNull.Value), ("$a", Stamp(at)), ("$p", place == MemoPlace.Pinned ? MemoPlace.Archived.ToString() : place.ToString()),
            ("$c", colour is not null && Memo.Colours.Contains(colour) ? colour : Memo.ColourFor(by)),
            ("$s", sticker is not null && Memo.Stickers.Contains(sticker) ? sticker : DBNull.Value));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Every memo, the newest first.</summary>
    public IReadOnlyList<Memo> Memos()
    {
        using var command = Command("SELECT id, title, text, by, at, place, pin, colour, sticker, a, b, tilt FROM memos ORDER BY at DESC, id DESC");
        using var reader = command.ExecuteReader();
        var memos = new List<Memo>();
        while (reader.Read())
        {
            var by = reader.IsDBNull(3) ? null : reader.GetString(3);
            var place = Enum.TryParse<MemoPlace>(reader.GetString(5), out var p) ? p : MemoPlace.Archived;
            (double, double)? spot = reader.IsDBNull(9) || reader.IsDBNull(10) ? null : (reader.GetDouble(9), reader.GetDouble(10));
            if (place == MemoPlace.Pinned && spot is null)
                place = MemoPlace.Archived;                 // pinned before spots: it has nowhere to be, so it waits in the archive
            memos.Add(new Memo(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), by, Parse(reader.GetString(4)), place, reader.GetInt32(6),
                               reader.IsDBNull(7) ? Memo.ColourFor(by) : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), spot,
                               reader.GetDouble(11)));
        }
        return memos;
    }

    /// <summary>
    /// Pins a memo on the cabinet's side at a spot (or moves it there, if pinned already), over the others pinned
    /// before it. False when the side is full (<see cref="Memo.PinnedAtMost"/>): it stays where it was.
    /// </summary>
    public bool PinMemo(long id, double a, double b, double tilt)
    {
        var memos = Memos();
        var pinned = memos.Where(m => m.Place == MemoPlace.Pinned).ToList();
        if (pinned.All(m => m.Id != id) && pinned.Count >= Memo.PinnedAtMost)
            return false;
        var top = pinned.Count == 0 ? 0 : pinned.Max(m => m.Pin) + 1;
        using var command = Command("UPDATE memos SET place = $p, pin = $n, a = $a, b = $b, tilt = $t WHERE id = $id",
            ("$p", MemoPlace.Pinned.ToString()), ("$n", top), ("$a", a), ("$b", b), ("$t", tilt), ("$id", id));
        command.ExecuteNonQuery();
        return true;
    }

    /// <summary>Moves a memo to hang from the slot (the one hanging there goes to the archive) or to the archive.</summary>
    public void MoveMemo(long id, MemoPlace place)
    {
        if (place == MemoPlace.Pinned)
            throw new ArgumentException("a pinned memo needs a spot: see PinMemo", nameof(place));
        if (place == MemoPlace.Hanging)
            Run($"UPDATE memos SET place = '{MemoPlace.Archived}' WHERE place = '{MemoPlace.Hanging}'");
        using var command = Command("UPDATE memos SET place = $p WHERE id = $id", ("$p", place.ToString()), ("$id", id));
        command.ExecuteNonQuery();
    }

    /// <summary>A memo's paper and sticker (unknown ones are left as they were; a null sticker takes it off).</summary>
    public void StyleMemo(long id, string colour, string? sticker)
    {
        using var command = Command("UPDATE memos SET colour = CASE WHEN $c IN ('" + string.Join("','", Memo.Colours) + "') THEN $c ELSE colour END, sticker = $s WHERE id = $id",
            ("$c", colour), ("$s", sticker is not null && Memo.Stickers.Contains(sticker) ? sticker : DBNull.Value), ("$id", id));
        command.ExecuteNonQuery();
    }

    /// <summary>The player changed one (their own, or hers: it's theirs to keep).</summary>
    public void UpdateMemo(long id, string title, string text)
    {
        title = title.Trim();
        text = text.Trim();
        using var command = Command("UPDATE memos SET title = $t, text = $x WHERE id = $id",
            ("$t", Prompt.Fit(title, Memo.TitleLength)), ("$x", Prompt.Fit(text, Memo.TextLength)), ("$id", id));
        command.ExecuteNonQuery();
    }

    public void DeleteMemo(long id)
    {
        using var command = Command("DELETE FROM memos WHERE id = $id", ("$id", id));
        command.ExecuteNonQuery();
    }

    private static object Minutes(TimeSpan? before) => before is { } b ? (long)b.TotalMinutes : DBNull.Value;
    private static object Repeated(Repeat repeat) => repeat == Repeat.Once ? DBNull.Value : repeat.ToString();

    /// <summary>The reminder went off (or, missed, was mentioned).</summary>
    public void Reminded(long id, DateTimeOffset at)
    {
        using var command = Command("UPDATE agenda SET reminded = $a WHERE id = $id", ("$a", Stamp(at)), ("$id", id));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A repeating item without a reminder of its own moves on once it is over (one with a reminder moves on when the
    /// reminder goes off). A one-off item over for longer than <see cref="Mind.Agenda.Behind"/> leaves the agenda,
    /// unless a reminder the player asked for has yet to be mentioned; a year on it is deleted.
    /// </summary>
    /// <param name="zone">The player's time zone, so a repeat keeps its time of day when the clocks change.</param>
    public int ClosePast(DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var plans = Agenda();
        foreach (var plan in plans.Where(p => p.Repeat != Repeat.Once && p.Before is null && p.End <= now))
            Advance(plan.Id, Mind.Agenda.NextAfter(plan, now, zone: zone));
        var closed = 0;
        foreach (var plan in plans.Where(p => p.Repeat == Repeat.Once && p.End <= now - Mind.Agenda.Behind
                                              && (p.Reminded is not null || p.RemindAt() is not { } at || p.Added > at)))
        {
            using var command = Command("UPDATE agenda SET closed = $n WHERE id = $id", ("$n", Stamp(now)), ("$id", plan.Id));
            closed += command.ExecuteNonQuery();
        }
        using (var old = Command("DELETE FROM agenda WHERE closed IS NOT NULL AND closed < $y", ("$y", Stamp(now.AddYears(-1)))))
            old.ExecuteNonQuery();
        return closed;
    }

    // ---- Usage (see Metered) -----------------------------------------------------------------------------------------

    public void AddUsage(DateOnly day, string model, string purpose, long input, long cached, long written, long output, long reasoning)
    {
        using var command = Command("""
            INSERT INTO usage (day, model, purpose, requests, input, cached, written, output, reasoning) VALUES ($d, $m, $p, 1, $i, $c, $w, $o, $r)
            ON CONFLICT (day, model, purpose) DO UPDATE SET requests = requests + 1, input = input + $i, cached = cached + $c, written = written + $w,
                output = output + $o, reasoning = reasoning + $r
            """, ("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$m", model), ("$p", purpose), ("$i", input), ("$c", cached),
            ("$w", written), ("$o", output), ("$r", reasoning));
        command.ExecuteNonQuery();
    }

    /// <summary>What requests used from <paramref name="from"/> on, by day.</summary>
    public IReadOnlyList<UsageRow> Usage(DateOnly from)
    {
        using var command = Command("SELECT day, model, purpose, requests, input, cached, written, output, reasoning FROM usage WHERE day >= $d ORDER BY day, model, purpose",
            ("$d", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        using var reader = command.ExecuteReader();
        var rows = new List<UsageRow>();
        while (reader.Read())
            rows.Add(new UsageRow(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetString(1), reader.GetString(2),
                                  reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8)));
        return rows;
    }

    // ---- Passages (the whole conversation, searchable; see History) -------------------------------------------------

    public long AddPassage(string character, long first, long last, DateTimeOffset at, string text)
    {
        using var command = Command("INSERT INTO passages (character, first, last, at, text) VALUES ($c, $f, $l, $a, $t) RETURNING id",
            ("$c", character), ("$f", first), ("$l", last), ("$a", Stamp(at)), ("$t", text));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Many passages at once (a long history the first time), in one transaction; returns them with their ids.</summary>
    public IReadOnlyList<Passage> AddPassages(string character, IReadOnlyList<Passage> passages)
    {
        if (passages.Count == 0)
            return [];
        using var transaction = _db.BeginTransaction();
        var added = new List<Passage>();
        foreach (var p in passages)
        {
            using var command = Command("INSERT INTO passages (character, first, last, at, text) VALUES ($c, $f, $l, $a, $t) RETURNING id",
                ("$c", character), ("$f", p.First), ("$l", p.Last), ("$a", Stamp(p.At)), ("$t", p.Text));
            command.Transaction = transaction;
            added.Add(p with { Id = (long)command.ExecuteScalar()! });
        }
        transaction.Commit();
        return added;
    }

    public IReadOnlyList<Passage> Passages(string character)
    {
        using var command = Command("SELECT id, first, last, at, text FROM passages WHERE character = $c ORDER BY last", ("$c", character));
        using var reader = command.ExecuteReader();
        var passages = new List<Passage>();
        while (reader.Read())
            passages.Add(new Passage(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), Parse(reader.GetString(3)), reader.GetString(4)));
        return passages;
    }

    /// <summary>The character's lines from <paramref name="first"/> to <paramref name="last"/>, oldest first.</summary>
    public IReadOnlyList<Line> Between(string character, long first, long last)
    {
        using var command = Command("SELECT id, speaker, text, at FROM lines WHERE character = $c AND id >= $f AND id <= $l ORDER BY id",
            ("$c", character), ("$f", first), ("$l", last));
        return ReadLines(command);
    }

    /// <summary>The last line of this character's conversation that upkeep has read (0: none).</summary>
    public long Digested(string character)
    {
        using var command = Command("SELECT through FROM digested WHERE character = $c", ("$c", character));
        return command.ExecuteScalar() as long? ?? 0;
    }

    public void SetDigested(string character, long through)
    {
        using var command = Command("""
            INSERT INTO digested (character, through) VALUES ($c, $t) ON CONFLICT (character) DO UPDATE SET through = excluded.through
            """, ("$c", character), ("$t", through));
        command.ExecuteNonQuery();
    }

    /// <summary>The character's lines up to and including <paramref name="id"/>, newest <paramref name="limit"/> of them, oldest first.</summary>
    public IReadOnlyList<Line> Through(string character, long id, int limit)
    {
        using var command = Command("""
            SELECT id, speaker, text, at FROM (SELECT * FROM lines WHERE character = $c AND id <= $i ORDER BY id DESC LIMIT $n) ORDER BY id
            """, ("$c", character), ("$i", id), ("$n", limit));
        return ReadLines(command);
    }

    public long Add(string character, Speaker speaker, string text, DateTimeOffset at)
    {
        using var command = Command("INSERT INTO lines (character, speaker, text, at) VALUES ($c, $s, $t, $a) RETURNING id",
            ("$c", character), ("$s", speaker.ToString()), ("$t", text), ("$a", Stamp(at)));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>A line says something else now (her reply, cut off where the player stopped hearing it).</summary>
    public void Rewrite(long id, string text)
    {
        using var command = Command("UPDATE lines SET text = $t WHERE id = $i", ("$t", text), ("$i", id));
        command.ExecuteNonQuery();
    }

    /// <summary>The character's lines after the given id, oldest first.</summary>
    public IReadOnlyList<Line> After(string character, long id)
    {
        using var command = Command("SELECT id, speaker, text, at FROM lines WHERE character = $c AND id > $i ORDER BY id", ("$c", character), ("$i", id));
        return ReadLines(command);
    }

    private static List<Line> ReadLines(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var lines = new List<Line>();
        while (reader.Read())
            lines.Add(new Line(reader.GetInt64(0), Enum.Parse<Speaker>(reader.GetString(1)), reader.GetString(2), Parse(reader.GetString(3))));
        return lines;
    }

    // ---- Activities (see Activities): what counts, her verdicts on proof, and what waits for proof ----------------------

    public IReadOnlyList<Activity> Activities()
    {
        using var command = Command("SELECT name, size, provable, added FROM activities ORDER BY added, name");
        using var reader = command.ExecuteReader();
        var activities = new List<Activity>();
        while (reader.Read())
            activities.Add(new Activity(reader.GetString(0), Enum.TryParse<InsertCoin.Core.Size>(reader.GetString(1), out var size) ? size : InsertCoin.Core.Size.Small,
                                        reader.GetInt64(2) != 0, Parse(reader.GetString(3))));
        return activities;
    }

    /// <summary>Keeps a new activity (one she knows already keeps its size).</summary>
    public void KeepActivity(Activity activity)
    {
        using var command = Command("INSERT OR IGNORE INTO activities (name, size, provable, added) VALUES ($n, $s, $p, $a)",
            ("$n", activity.Name), ("$s", activity.Size.ToString()), ("$p", activity.Provable ? 1 : 0), ("$a", Stamp(activity.Added)));
        command.ExecuteNonQuery();
    }

    public void ForgetActivity(string name)
    {
        using var command = Command("DELETE FROM activities WHERE name = $n; DELETE FROM claims WHERE activity = $n", ("$n", name));
        command.ExecuteNonQuery();
    }

    /// <summary>Her latest verdicts on the player's proofs, the newest first.</summary>
    public IReadOnlyList<Conviction> Convictions(int count)
    {
        using var command = Command("SELECT verdict FROM proofs ORDER BY id DESC LIMIT $c", ("$c", count));
        using var reader = command.ExecuteReader();
        var verdicts = new List<Conviction>();
        while (reader.Read())
            if (Enum.TryParse<Conviction>(reader.GetString(0), out var verdict))
                verdicts.Add(verdict);
        return verdicts;
    }

    public void AddConviction(string activity, Conviction verdict, DateTimeOffset at)
    {
        using var command = Command("INSERT INTO proofs (at, activity, verdict) VALUES ($a, $n, $v)", ("$a", Stamp(at)), ("$n", activity), ("$v", verdict.ToString()));
        command.ExecuteNonQuery();
    }

    /// <summary>The activities waiting for proof on a game day, in the order they were logged.</summary>
    public IReadOnlyList<string> Claims(DateOnly day)
    {
        using var command = Command("SELECT activity FROM claims WHERE day = $d ORDER BY at", ("$d", day.ToString("O", CultureInfo.InvariantCulture)));
        using var reader = command.ExecuteReader();
        var claims = new List<string>();
        while (reader.Read())
            claims.Add(reader.GetString(0));
        return claims;
    }

    public void Claim(DateOnly day, string activity, DateTimeOffset at)
    {
        using var command = Command("INSERT OR REPLACE INTO claims (day, activity, at) VALUES ($d, $n, $a)",
            ("$d", day.ToString("O", CultureInfo.InvariantCulture)), ("$n", activity), ("$a", Stamp(at)));
        command.ExecuteNonQuery();
    }

    /// <summary>An activity's proof is done (it counted in full).</summary>
    public void Settle(DateOnly day, string activity)
    {
        using var command = Command("DELETE FROM claims WHERE day = $d AND activity = $n", ("$d", day.ToString("O", CultureInfo.InvariantCulture)), ("$n", activity));
        command.ExecuteNonQuery();
    }

    private static string Stamp(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string stamp) => DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture);

    /// <summary>The last line that has left the window (0: none has): what came after it she reads word for word.</summary>
    public long Folded(string character)
    {
        using var command = Command("SELECT through FROM summaries WHERE character = $c", ("$c", character));
        return command.ExecuteScalar() is long through ? through : 0;
    }

    /// <summary>
    /// Lines up to and including <paramref name="through"/> have been observed, and these are her notes on them: added
    /// to her log together, so neither happens without the other. The lines stay in the window until it is folded.
    /// </summary>
    public void Observe(string character, IReadOnlyList<Observation> observed, long through)
    {
        using var transaction = _db.BeginTransaction();
        AddObservations(character, observed);
        SetDigested(character, through);
        transaction.Commit();
    }

    /// <summary>Lines up to and including <paramref name="through"/> leave the window: her notes on them are what she sees of them now.</summary>
    public void Fold(string character, long through, DateTimeOffset at)
    {
        using var fold = Command("""
            INSERT INTO summaries (character, text, through, at) VALUES ($c, '', $i, $a)
            ON CONFLICT (character) DO UPDATE SET through = excluded.through, at = excluded.at
            """, ("$c", character), ("$i", through), ("$a", Stamp(at)));
        fold.ExecuteNonQuery();
    }

    /// <summary>A note the player had her forget.</summary>
    public void DeleteObservation(long id)
    {
        using var command = Command("DELETE FROM observations WHERE id = $id", ("$id", id));
        command.ExecuteNonQuery();
        DropVectors(ObservationKind, id);
    }

    /// <summary>
    /// Notes condensed into <paramref name="condensed"/>, together: the originals stay in her log, marked as condensed,
    /// so they are out of view but still recalled.
    /// </summary>
    public void Condense(string character, IReadOnlyList<long> originals, IReadOnlyList<Observation> condensed)
    {
        using var transaction = _db.BeginTransaction();
        AddObservations(character, condensed);
        foreach (var id in originals)
        {
            using var mark = Command("UPDATE observations SET condensed = 1 WHERE id = $i", ("$i", id));
            mark.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private void AddObservations(string character, IReadOnlyList<Observation> observations)
    {
        foreach (var o in observations)
        {
            using var add = Command("INSERT INTO observations (character, at, priority, text, first, last, span) VALUES ($c, $a, $p, $t, $f, $l, $s)",
                ("$c", character), ("$a", Stamp(o.At)), ("$p", (int)o.Priority), ("$t", o.Text.Trim()), ("$f", o.First), ("$l", o.Last), ("$s", (int)o.Span));
            add.ExecuteNonQuery();
        }
    }

    /// <summary>Her log, oldest first, the notes that were condensed into others too.</summary>
    public IReadOnlyList<Observation> Observations(string character)
    {
        using var command = Command("SELECT id, at, priority, text, first, last, span, condensed FROM observations WHERE character = $c ORDER BY at, id", ("$c", character));
        using var reader = command.ExecuteReader();
        var log = new List<Observation>();
        while (reader.Read())
            log.Add(new Observation(reader.GetInt64(0), Parse(reader.GetString(1)), (Priority)reader.GetInt32(2), reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5),
                                    (Span)reader.GetInt32(6), reader.GetInt32(7) != 0));
        return log;
    }

    public string Note(string scope, string name)
    {
        using var command = Command("SELECT text FROM notes WHERE scope = $s AND name = $n", ("$s", scope), ("$n", name));
        return command.ExecuteScalar() as string ?? "";
    }

    public void SetNote(string scope, string name, string text)
    {
        using var command = Command("""
            INSERT INTO notes (scope, name, text) VALUES ($s, $n, $t) ON CONFLICT (scope, name) DO UPDATE SET text = excluded.text
            """, ("$s", scope), ("$n", name), ("$t", text));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Forgets the conversation with this character and everything about the two of you. What every character knows
    /// about the player stays (see <see cref="ForgetPlayer"/>).
    /// </summary>
    public void Forget(string character)
    {
        using var command = Command("""
            DELETE FROM lines WHERE character = $c; DELETE FROM summaries WHERE character = $c; DELETE FROM notes WHERE scope = $c;
            DELETE FROM facts WHERE scope = $c; DELETE FROM digested WHERE character = $c; DELETE FROM marks WHERE key = 'kept:' || $c;
            DELETE FROM passages WHERE character = $c; DELETE FROM observations WHERE character = $c;
            """, ("$c", character));
        command.ExecuteNonQuery();
        DropVectors(FactKind);
        DropVectors(PassageKind);
        DropVectors(ObservationKind);
        Changes++;
    }

    /// <summary>
    /// Clears the conversation with this character (its lines, how far it was folded, and upkeep's place in them) and
    /// keeps everything she took from it: the always-present notes, her facts and her log. Her notes now count as from
    /// before the conversation, so they are in view at once whatever ids the new lines get (ids are reused once a table
    /// is empty). Lines upkeep had not yet read are lost with the rest.
    /// </summary>
    public void ForgetConversation(string character)
    {
        using var command = Command("""
            DELETE FROM lines WHERE character = $c; DELETE FROM summaries WHERE character = $c; DELETE FROM digested WHERE character = $c;
            DELETE FROM passages WHERE character = $c;
            UPDATE observations SET first = 0, last = 0 WHERE character = $c;
            """, ("$c", character));
        command.ExecuteNonQuery();
        DropVectors(PassageKind);
        Changes++;
    }

    /// <summary>Forgets what every character knows about the player: the shared note and facts, the agenda, their activities and proofs.</summary>
    public void ForgetPlayer()
    {
        using var command = Command("DELETE FROM notes WHERE scope = $s; DELETE FROM facts WHERE scope = $s; DELETE FROM agenda; " +
                                    "DELETE FROM activities; DELETE FROM proofs; DELETE FROM claims;", ("$s", Shared));
        command.ExecuteNonQuery();
        DropVectors(FactKind);
    }

    public void Dispose() => _db.Dispose();

    private void Run(string sql)
    {
        using var command = Command(sql);
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        var command = _db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }
}
