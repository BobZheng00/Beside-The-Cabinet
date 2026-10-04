using System.Globalization;
using Microsoft.Data.Sqlite;

namespace InsertCoin.Mind;

public enum Speaker { User, Her, Cabinet }

/// <summary>One line of the conversation: something the user typed, something she said, or something the cabinet did.</summary>
public sealed record Line(long Id, Speaker Speaker, string Text, DateTimeOffset At);

/// <summary>What the conversation before <see cref="Through"/> (a line id) has been folded into.</summary>
public sealed record Summary(string Text, long Through);

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

/// <summary>A dated summary of one stretch of conversation, lines <see cref="First"/> to <see cref="Last"/>; a week's worth when <see cref="Week"/>.</summary>
/// <summary>The stretch of time a moment covers: a day's talk, or older moments rolled up (see <see cref="Conversation.Consolidate"/>).</summary>
public enum Span { Day, Week, Month, Year }

public sealed record Episode(long Id, string Text, long First, long Last, DateTimeOffset At, Span Span = Span.Day)
{
    public bool Week => Span == Span.Week;

    /// <summary>When it was, as the memory book and recall show it: "Fri 25 Sep", "Week of 7 Sep", "September 2026", "2026".</summary>
    public string Label => Span switch
    {
        Span.Week => "Week of " + At.ToString("d MMM", CultureInfo.InvariantCulture),
        Span.Month => At.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
        Span.Year => At.ToString("yyyy", CultureInfo.InvariantCulture),
        _ => At.ToString("ddd d MMM", CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// Everything she remembers, in one SQLite file in the profile folder: each character's conversation (one continuous
/// thread), the summary its older part has been folded into, the notes that are always in her context, the facts
/// and episodes upkeep draws from the conversation, and how far upkeep has read.
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
        Run("PRAGMA user_version = 5");
    }

    // ---- Vectors (what facts and episodes mean, for search by meaning) --------------------------------------------

    public const string FactKind = "fact", EpisodeKind = "episode", PassageKind = "passage";

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

    /// <summary>The vectors a model has made for facts or episodes, by id.</summary>
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

    /// <summary>Drops vectors whose fact or episode is gone or has changed (it gets a new one at the next indexing).</summary>
    private void DropVectors(string kind, long? id = null)
    {
        using var command = id is { } one
            ? Command("DELETE FROM vectors WHERE kind = $k AND id = $i", ("$k", kind), ("$i", one))
            : Command(kind switch
            {
                FactKind => "DELETE FROM vectors WHERE kind = $k AND id NOT IN (SELECT id FROM facts)",
                PassageKind => "DELETE FROM vectors WHERE kind = $k AND id NOT IN (SELECT id FROM passages)",
                _ => "DELETE FROM vectors WHERE kind = $k AND id NOT IN (SELECT id FROM episodes)",
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

    // ---- Episodes and upkeep ---------------------------------------------------------------------------------------

    public long AddEpisode(string character, string text, long first, long last, DateTimeOffset at, Span span = Span.Day)
    {
        using var command = Command("INSERT INTO episodes (character, text, first, last, at, week) VALUES ($c, $t, $f, $l, $a, $w) RETURNING id",
            ("$c", character), ("$t", text.Trim()), ("$f", first), ("$l", last), ("$a", Stamp(at)), ("$w", (int)span));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>A moment rewritten to take in more of its day (its vector goes: it no longer means the same).</summary>
    public void UpdateEpisode(long id, string text, long last)
    {
        using var command = Command("UPDATE episodes SET text = $t, last = $l WHERE id = $id", ("$t", text.Trim()), ("$l", last), ("$id", id));
        command.ExecuteNonQuery();
        DropVectors(EpisodeKind, id);
    }

    public IReadOnlyList<Episode> Episodes(string character)
    {
        using var command = Command("SELECT id, text, first, last, at, week FROM episodes WHERE character = $c ORDER BY at, id", ("$c", character));
        using var reader = command.ExecuteReader();
        var episodes = new List<Episode>();
        while (reader.Read())
            episodes.Add(new Episode(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), Parse(reader.GetString(4)), (Span)reader.GetInt32(5)));
        return episodes;
    }

    public void DeleteEpisode(long id)
    {
        using var command = Command("DELETE FROM episodes WHERE id = $id", ("$id", id));
        command.ExecuteNonQuery();
        DropVectors(EpisodeKind, id);
    }

    // ---- Passages (the whole conversation, searchable; see History) -------------------------------------------------

    public long AddPassage(string character, long first, long last, DateTimeOffset at, string text)
    {
        using var command = Command("INSERT INTO passages (character, first, last, at, text) VALUES ($c, $f, $l, $a, $t) RETURNING id",
            ("$c", character), ("$f", first), ("$l", last), ("$a", Stamp(at)), ("$t", text));
        return (long)command.ExecuteScalar()!;
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

    private static string Stamp(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string stamp) => DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture);

    public Summary? SummaryOf(string character)
    {
        using var command = Command("SELECT text, through FROM summaries WHERE character = $c", ("$c", character));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new Summary(reader.GetString(0), reader.GetInt64(1)) : null;
    }

    /// <summary>Replaces the summary: it now covers every line up to and including <paramref name="through"/>.</summary>
    public void Fold(string character, string text, long through, DateTimeOffset at)
    {
        using var command = Command("""
            INSERT INTO summaries (character, text, through, at) VALUES ($c, $t, $i, $a)
            ON CONFLICT (character) DO UPDATE SET text = excluded.text, through = excluded.through, at = excluded.at
            """, ("$c", character), ("$t", text), ("$i", through), ("$a", at.ToString("O", CultureInfo.InvariantCulture)));
        command.ExecuteNonQuery();
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
            DELETE FROM facts WHERE scope = $c; DELETE FROM episodes WHERE character = $c; DELETE FROM digested WHERE character = $c;
            DELETE FROM passages WHERE character = $c;
            """, ("$c", character));
        command.ExecuteNonQuery();
        DropVectors(FactKind);
        DropVectors(EpisodeKind);
        DropVectors(PassageKind);
        Changes++;
    }

    /// <summary>
    /// Clears the conversation with this character (its lines, their summary, and upkeep's place in them) and keeps
    /// everything she took from it: notes, facts and dated moments. The moments now count as from before the
    /// conversation, so they can be recalled at once whatever ids the new lines get (ids are reused once a table is
    /// empty). Lines upkeep had not yet read are lost with the rest.
    /// </summary>
    public void ForgetConversation(string character)
    {
        using var command = Command("""
            DELETE FROM lines WHERE character = $c; DELETE FROM summaries WHERE character = $c; DELETE FROM digested WHERE character = $c;
            DELETE FROM passages WHERE character = $c;
            UPDATE episodes SET first = 0, last = 0 WHERE character = $c;
            """, ("$c", character));
        command.ExecuteNonQuery();
        DropVectors(PassageKind);
        Changes++;
    }

    /// <summary>Forgets what every character knows about the player: the shared note and facts.</summary>
    public void ForgetPlayer()
    {
        using var command = Command("DELETE FROM notes WHERE scope = $s; DELETE FROM facts WHERE scope = $s;", ("$s", Shared));
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
