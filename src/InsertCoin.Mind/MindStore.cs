using System.Globalization;
using Microsoft.Data.Sqlite;

namespace InsertCoin.Mind;

public enum Speaker { User, Her, Cabinet }

/// <summary>One line of the conversation: something the user typed, something she said, or something the cabinet did.</summary>
public sealed record Line(long Id, Speaker Speaker, string Text, DateTimeOffset At);

/// <summary>What the conversation before <see cref="Through"/> (a line id) has been folded into.</summary>
public sealed record Summary(string Text, long Through);

/// <summary>
/// Everything she remembers, in one SQLite file in the profile folder: each character's conversation (one continuous
/// thread), the summary its older part has been folded into, and the notes that are always in her context.
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
            PRAGMA user_version = 1;
            """);
    }

    public long Add(string character, Speaker speaker, string text, DateTimeOffset at)
    {
        using var command = Command("INSERT INTO lines (character, speaker, text, at) VALUES ($c, $s, $t, $a) RETURNING id",
            ("$c", character), ("$s", speaker.ToString()), ("$t", text), ("$a", at.ToString("O", CultureInfo.InvariantCulture)));
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>The character's lines after the given id, oldest first.</summary>
    public IReadOnlyList<Line> After(string character, long id)
    {
        using var command = Command("SELECT id, speaker, text, at FROM lines WHERE character = $c AND id > $i ORDER BY id", ("$c", character), ("$i", id));
        using var reader = command.ExecuteReader();
        var lines = new List<Line>();
        while (reader.Read())
            lines.Add(new Line(reader.GetInt64(0), Enum.Parse<Speaker>(reader.GetString(1)), reader.GetString(2),
                               DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        return lines;
    }

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

    /// <summary>Forgets the conversation with this character and what was noted about the two of you.</summary>
    public void Forget(string character)
    {
        using var command = Command("""
            DELETE FROM lines WHERE character = $c; DELETE FROM summaries WHERE character = $c; DELETE FROM notes WHERE scope = $c;
            """, ("$c", character));
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
