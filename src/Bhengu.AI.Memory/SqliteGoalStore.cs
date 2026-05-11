// SqliteGoalStore.cs
//
// IGoalStore persisted to a SQLite database.
// Uses Microsoft.Data.Sqlite for cross-platform compatibility.
//
// A single SqliteConnection is kept open for the lifetime of the store.
// This is required for "Data Source=:memory:" (in-memory databases are
// per-connection in SQLite — a new connection would get an empty database).
// For file-backed databases the persistent connection also avoids repeated
// open/close overhead.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Bhengu.AI.Memory;

/// <summary>
/// SQLite-backed <see cref="IGoalStore"/>.
/// Pass <c>"Data Source=:memory:"</c> for an in-process test instance.
/// Dispose the store when done to close the underlying connection.
/// </summary>
public sealed class SqliteGoalStore : IGoalStore, IDisposable
{
    // Single long-lived connection. Required for :memory: databases and more
    // efficient for file databases.
    private readonly SqliteConnection _conn;
    private bool _disposed;

    // ------------------------------------------------------------------
    // Construction / Schema
    // ------------------------------------------------------------------

    /// <param name="connectionString">
    /// SQLite connection string, e.g. <c>"Data Source=goals.db"</c>.
    /// </param>
    public SqliteGoalStore(string connectionString)
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
            CREATE TABLE IF NOT EXISTS goals (
                id            TEXT PRIMARY KEY,
                user_id       TEXT NOT NULL,
                title         TEXT NOT NULL,
                description   TEXT NOT NULL,
                status        INTEGER NOT NULL,
                priority      INTEGER NOT NULL,
                created_utc   TEXT NOT NULL,
                due_utc       TEXT,
                completed_utc TEXT,
                notes         TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_goals_user_id
                ON goals (user_id);
            """;
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------
    // IGoalStore
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<Goal>> ListAsync(string userId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, title, description, status, priority,
                   created_utc, due_utc, completed_utc, notes
            FROM   goals
            WHERE  user_id = $userId;
            """;
        cmd.Parameters.AddWithValue("$userId", userId);

        return Task.FromResult<IReadOnlyList<Goal>>(ReadGoals(cmd));
    }

    /// <inheritdoc />
    public Task<Goal?> GetAsync(string id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, title, description, status, priority,
                   created_utc, due_utc, completed_utc, notes
            FROM   goals
            WHERE  id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);

        var results = ReadGoals(cmd);
        return Task.FromResult<Goal?>(results.Count > 0 ? results[0] : null);
    }

    /// <inheritdoc />
    public Task<Goal> UpsertAsync(Goal goal, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(goal);
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO goals
                (id, user_id, title, description, status, priority,
                 created_utc, due_utc, completed_utc, notes)
            VALUES
                ($id, $userId, $title, $description, $status, $priority,
                 $createdUtc, $dueUtc, $completedUtc, $notes);
            """;

        cmd.Parameters.AddWithValue("$id",           goal.Id);
        cmd.Parameters.AddWithValue("$userId",       goal.UserId);
        cmd.Parameters.AddWithValue("$title",        goal.Title);
        cmd.Parameters.AddWithValue("$description",  goal.Description);
        cmd.Parameters.AddWithValue("$status",       (int)goal.Status);
        cmd.Parameters.AddWithValue("$priority",     (int)goal.Priority);
        cmd.Parameters.AddWithValue("$createdUtc",   goal.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$dueUtc",       (object?)goal.DueUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$completedUtc", (object?)goal.CompletedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes",        (object?)goal.Notes ?? DBNull.Value);

        cmd.ExecuteNonQuery();
        return Task.FromResult(goal);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM goals WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Goal>> GetActiveAsync(string userId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ct.ThrowIfCancellationRequested();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, title, description, status, priority,
                   created_utc, due_utc, completed_utc, notes
            FROM   goals
            WHERE  user_id = $userId
              AND  status  = $active;
            """;
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$active", (int)GoalStatus.Active);

        return Task.FromResult<IReadOnlyList<Goal>>(ReadGoals(cmd));
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
            throw new ObjectDisposedException(nameof(SqliteGoalStore));
    }

    // ------------------------------------------------------------------
    // Helpers — row mapping
    // ------------------------------------------------------------------

    private static List<Goal> ReadGoals(SqliteCommand cmd)
    {
        var results = new List<Goal>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Goal(
                Id:           reader.GetString(0),
                UserId:       reader.GetString(1),
                Title:        reader.GetString(2),
                Description:  reader.GetString(3),
                Status:       (GoalStatus)reader.GetInt32(4),
                Priority:     (GoalPriority)reader.GetInt32(5),
                CreatedUtc:   DateTimeOffset.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
                DueUtc:       reader.IsDBNull(7)  ? null : DateTimeOffset.Parse(reader.GetString(7),  null, DateTimeStyles.RoundtripKind),
                CompletedUtc: reader.IsDBNull(8)  ? null : DateTimeOffset.Parse(reader.GetString(8),  null, DateTimeStyles.RoundtripKind),
                Notes:        reader.IsDBNull(9)  ? null : reader.GetString(9)
            ));
        }
        return results;
    }
}
