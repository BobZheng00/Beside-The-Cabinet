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
/// the two of them (a character's id). Importance 1–10; recalled is when it last came up in a conversation.
/// </summary>
public sealed record Fact(long Id, string Scope, string Text, int Importance, DateTimeOffset Learned, DateTimeOffset? Recalled, bool Pinned);

/// <summary>A dated summary of one stretch of conversation, lines <see cref="First"/> to <see cref="Last"/>.</summary>
public sealed record Episode(long Id, string Text, long First, long Last, DateTimeOffset At);

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
            PRAGMA user_version = 2;
            """);
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
    }

    /// <summary>What this character knows: facts about the player and about the two of them.</summary>
    public IReadOnlyList<Fact> Facts(string character)
    {
        using var command = Command("SELECT id, scope, text, importance, learned, recalled, pinned FROM facts WHERE scope IN ($s, $c) ORDER BY id",
            ("$s", Shared), ("$c", character));
        using var reader = command.ExecuteReader();
        var facts = new List<Fact>();
        while (reader.Read())
            facts.Add(new Fact(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), Parse(reader.GetString(4)),
                               reader.IsDBNull(5) ? null : Parse(reader.GetString(5)), reader.GetInt32(6) != 0));
        return facts;
    }

    public void Recalled(IEnumerable<long> ids, DateTimeOffset at)
    {
        foreach (var id in ids)
        {
            using var command = Command("UPDATE facts SET recalled = $a WHERE id = $id", ("$id", id), ("$a", Stamp(at)));
            command.ExecuteNonQuery();
        }
    }

    // ---- Episodes and upkeep ---------------------------------------------------------------------------------------

    public long AddEpisode(string character, string text, long first, long last, DateTimeOffset at)
    {
        using var command = Command("INSERT INTO episodes (character, text, first, last, at) VALUES ($c, $t, $f, $l, $a) RETURNING id",
            ("$c", character), ("$t", text.Trim()), ("$f", first), ("$l", last), ("$a", Stamp(at)));
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<Episode> Episodes(string character)
    {
        using var command = Command("SELECT id, text, first, last, at FROM episodes WHERE character = $c ORDER BY id", ("$c", character));
        using var reader = command.ExecuteReader();
        var episodes = new List<Episode>();
        while (reader.Read())
            episodes.Add(new Episode(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), Parse(reader.GetString(4))));
        return episodes;
    }

    public void DeleteEpisode(long id)
    {
        using var command = Command("DELETE FROM episodes WHERE id = $id", ("$id", id));
        command.ExecuteNonQuery();
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
            """, ("$c", character));
        command.ExecuteNonQuery();
    }

    /// <summary>Forgets what every character knows about the player: the shared note and facts.</summary>
    public void ForgetPlayer()
    {
        using var command = Command("DELETE FROM notes WHERE scope = $s; DELETE FROM facts WHERE scope = $s;", ("$s", Shared));
        command.ExecuteNonQuery();
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
