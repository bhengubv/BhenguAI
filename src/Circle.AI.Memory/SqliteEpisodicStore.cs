// SqliteEpisodicStore.cs
//
// IEpisodicMemoryStore persisted to a SQLite database.
// Uses Microsoft.Data.Sqlite for cross-platform compatibility.
//
// A single SqliteConnection is kept open for the lifetime of the store.
// This is required for "Data Source=:memory:" (in-memory databases are
// per-connection in SQLite — a new connection would get an empty database).
// For file-backed databases the persistent connection also avoids repeated
// open/close overhead.
//
// Embedding vector search: embeddings are stored as comma-delimited float
// strings. When queryEmbedding is non-null the store performs cosine
// similarity in managed code over all rows that have an embedding. For null
// queryEmbedding the method falls back to recency order.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Circle.AI.Memory;

/// <summary>
/// SQLite-backed episodic memory store.
/// Pass <c>"Data Source=:memory:"</c> for an in-process test instance.
/// Dispose the store when done to close the underlying connection.
/// </summary>
public sealed class SqliteEpisodicStore : IEpisodicMemoryStore, IDisposable
{
    // Single long-lived connection. Required for :memory: databases and more
    // efficient for file databases.
    private readonly SqliteConnection _conn;
    private bool _disposed;

    // ------------------------------------------------------------------
    // Construction / Schema
    // ------------------------------------------------------------------

    /// <param name="connectionString">
    /// SQLite connection string, e.g. <c>"Data Source=episodes.db"</c>.
    /// </param>
    public SqliteEpisodicStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));

        _conn = new SqliteConnection(connectionString);
        _conn.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS episodes (
                id              TEXT PRIMARY KEY NOT NULL,
                recorded_at_utc TEXT NOT NULL,
                user_text       TEXT NOT NULL,
                assistant_text  TEXT NOT NULL,
                app_context     TEXT,
                embedding       TEXT,
                tags            TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_episodes_recorded
                ON episodes (recorded_at_utc DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------
    // IEpisodicMemoryStore
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public Task AddAsync(EpisodicMemoryEntry entry, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO episodes
                (id, recorded_at_utc, user_text, assistant_text, app_context, embedding, tags)
            VALUES
                ($id, $recordedAtUtc, $userText, $assistantText, $appContext, $embedding, $tags);
            """;

        cmd.Parameters.AddWithValue("$id",            entry.Id.ToString("N"));
        cmd.Parameters.AddWithValue("$recordedAtUtc", entry.RecordedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$userText",      entry.UserText);
        cmd.Parameters.AddWithValue("$assistantText", entry.AssistantText);
        cmd.Parameters.AddWithValue("$appContext",    (object?)entry.AppContext ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$embedding",     (object?)SerialiseEmbedding(entry.Embedding) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags",          (object?)SerialiseTags(entry.Tags) ?? DBNull.Value);

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// When <paramref name="queryEmbedding"/> is non-null the store loads all
    /// stored embeddings, computes cosine similarity in managed code, and returns
    /// the top-<paramref name="topK"/> matches. When null it falls back to
    /// recency order (same as <see cref="GetRecentAsync"/>).
    /// </remarks>
    public Task<IReadOnlyList<EpisodicMemoryEntry>> SearchAsync(
        float[]? queryEmbedding,
        int topK = 5,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();

        if (queryEmbedding is null)
        {
            // Fallback: recency order.
            cmd.CommandText = """
                SELECT id, recorded_at_utc, user_text, assistant_text, app_context, embedding, tags
                FROM   episodes
                ORDER  BY recorded_at_utc DESC
                LIMIT  $topK;
                """;
            cmd.Parameters.AddWithValue("$topK", topK);
            return Task.FromResult<IReadOnlyList<EpisodicMemoryEntry>>(ReadEntries(cmd));
        }

        // Load all rows that have an embedding and rank by cosine similarity.
        cmd.CommandText = """
            SELECT id, recorded_at_utc, user_text, assistant_text, app_context, embedding, tags
            FROM   episodes
            WHERE  embedding IS NOT NULL
            ORDER  BY recorded_at_utc DESC;
            """;

        var allWithEmbedding = ReadEntries(cmd);

        var ranked = allWithEmbedding
            .Select(e => (Entry: e, Score: CosineSimilarity(queryEmbedding, e.Embedding)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Entry)
            .ToList();

        return Task.FromResult<IReadOnlyList<EpisodicMemoryEntry>>(ranked);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EpisodicMemoryEntry>> GetRecentAsync(
        int count = 10,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, recorded_at_utc, user_text, assistant_text, app_context, embedding, tags
            FROM   episodes
            ORDER  BY recorded_at_utc DESC
            LIMIT  $count;
            """;
        cmd.Parameters.AddWithValue("$count", count);

        return Task.FromResult<IReadOnlyList<EpisodicMemoryEntry>>(ReadEntries(cmd));
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM episodes;";
        var result = (long)cmd.ExecuteScalar()!;
        return Task.FromResult((int)result);
    }

    /// <inheritdoc />
    public Task<int> PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM episodes WHERE recorded_at_utc < $cutoff;";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
        var deleted = cmd.ExecuteNonQuery();
        return Task.FromResult(deleted);
    }

    // ------------------------------------------------------------------
    // IDisposable
    // ------------------------------------------------------------------

    /// <summary>Closes the underlying SQLite connection.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conn.Dispose();
    }

    // ------------------------------------------------------------------
    // Helpers — guard
    // ------------------------------------------------------------------

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqliteEpisodicStore));
    }

    // ------------------------------------------------------------------
    // Helpers — row mapping
    // ------------------------------------------------------------------

    private static List<EpisodicMemoryEntry> ReadEntries(SqliteCommand cmd)
    {
        var results = new List<EpisodicMemoryEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new EpisodicMemoryEntry
            {
                Id            = Guid.Parse(reader.GetString(0)),
                RecordedAtUtc = DateTimeOffset.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind),
                UserText      = reader.GetString(2),
                AssistantText = reader.GetString(3),
                AppContext     = reader.IsDBNull(4) ? null : reader.GetString(4),
                Embedding     = reader.IsDBNull(5) ? null : DeserialiseEmbedding(reader.GetString(5)),
                Tags          = reader.IsDBNull(6) ? null : DeserialiseTags(reader.GetString(6)),
            });
        }
        return results;
    }

    // ------------------------------------------------------------------
    // Helpers — embedding serialisation (CSV of float values)
    // ------------------------------------------------------------------

    private static string? SerialiseEmbedding(float[]? embedding)
    {
        if (embedding is null || embedding.Length == 0) return null;
        return string.Join(",", embedding.Select(f => f.ToString("R", CultureInfo.InvariantCulture)));
    }

    private static float[]? DeserialiseEmbedding(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split(',');
        var result = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            result[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
        return result;
    }

    // ------------------------------------------------------------------
    // Helpers — tags serialisation (key=value pairs, pipe-delimited)
    // ------------------------------------------------------------------

    private static string? SerialiseTags(Dictionary<string, string>? tags)
    {
        if (tags is null || tags.Count == 0) return null;
        return string.Join("|", tags.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    private static Dictionary<string, string>? DeserialiseTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in raw.Split('|'))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0) continue;
            var key   = Uri.UnescapeDataString(pair[..eq]);
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            dict[key] = value;
        }
        return dict.Count > 0 ? dict : null;
    }

    // ------------------------------------------------------------------
    // Helpers — cosine similarity
    // ------------------------------------------------------------------

    private static float CosineSimilarity(float[] a, float[]? b)
    {
        if (b is null || a.Length != b.Length) return 0f;

        double dot = 0d, magA = 0d, magB = 0d;
        for (var i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom < double.Epsilon ? 0f : (float)(dot / denom);
    }
}
