// GoalStoreTests.cs
//
// Unit tests for InMemoryGoalStore and SqliteGoalStore (Track 4).

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bhengu.AI.Memory;
using Xunit;

namespace Bhengu.AI.Tests;

public sealed class GoalStoreTests
{
    // ------------------------------------------------------------------
    // Shared factory helpers
    // ------------------------------------------------------------------

    private static Goal MakeGoal(
        string id = "goal-1",
        string userId = "user-a",
        GoalStatus status = GoalStatus.Active,
        GoalPriority priority = GoalPriority.Normal) =>
        new(
            Id:          id,
            UserId:      userId,
            Title:       $"Test goal {id}",
            Description: $"Description for {id}",
            Status:      status,
            Priority:    priority,
            CreatedUtc:  DateTimeOffset.UtcNow);

    // ==================================================================
    // InMemoryGoalStore
    // ==================================================================

    [Fact]
    public async Task InMemory_Upsert_ThenList_ReturnsSameGoal()
    {
        var store = new InMemoryGoalStore();
        var goal  = MakeGoal();

        await store.UpsertAsync(goal);
        var list = await store.ListAsync(goal.UserId);

        Assert.Single(list);
        Assert.Equal(goal.Id,    list[0].Id);
        Assert.Equal(goal.Title, list[0].Title);
    }

    [Fact]
    public async Task InMemory_Get_ReturnsCorrectGoal()
    {
        var store = new InMemoryGoalStore();
        var goal  = MakeGoal("g1");

        await store.UpsertAsync(goal);
        var fetched = await store.GetAsync("g1");

        Assert.NotNull(fetched);
        Assert.Equal("g1", fetched!.Id);
    }

    [Fact]
    public async Task InMemory_Get_ReturnsNull_WhenNotFound()
    {
        var store = new InMemoryGoalStore();

        var result = await store.GetAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task InMemory_Delete_RemovesGoal()
    {
        var store = new InMemoryGoalStore();
        var goal  = MakeGoal();

        await store.UpsertAsync(goal);
        await store.DeleteAsync(goal.Id);
        var list = await store.ListAsync(goal.UserId);

        Assert.Empty(list);
    }

    [Fact]
    public async Task InMemory_Delete_NoopWhenNotFound()
    {
        var store = new InMemoryGoalStore();

        // Must not throw.
        await store.DeleteAsync("missing-id");
    }

    [Fact]
    public async Task InMemory_GetActiveAsync_ReturnsOnlyActiveGoals()
    {
        var store = new InMemoryGoalStore();
        const string user = "user-x";

        await store.UpsertAsync(MakeGoal("a1", user, GoalStatus.Active));
        await store.UpsertAsync(MakeGoal("a2", user, GoalStatus.Active));
        await store.UpsertAsync(MakeGoal("c1", user, GoalStatus.Completed));
        await store.UpsertAsync(MakeGoal("ab", user, GoalStatus.Abandoned));

        var active = await store.GetActiveAsync(user);

        Assert.Equal(2, active.Count);
        Assert.All(active, g => Assert.Equal(GoalStatus.Active, g.Status));
    }

    [Fact]
    public async Task InMemory_List_IsolatesPerUser()
    {
        var store = new InMemoryGoalStore();

        await store.UpsertAsync(MakeGoal("g1", "alice"));
        await store.UpsertAsync(MakeGoal("g2", "bob"));

        var alice = await store.ListAsync("alice");
        var bob   = await store.ListAsync("bob");

        Assert.Single(alice);
        Assert.Equal("alice", alice[0].UserId);
        Assert.Single(bob);
        Assert.Equal("bob", bob[0].UserId);
    }

    [Fact]
    public async Task InMemory_Upsert_OverwritesExistingGoal()
    {
        var store = new InMemoryGoalStore();
        var goal  = MakeGoal();

        await store.UpsertAsync(goal);

        var updated = goal with { Title = "Updated title", Status = GoalStatus.Completed };
        await store.UpsertAsync(updated);

        var fetched = await store.GetAsync(goal.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Updated title",       fetched!.Title);
        Assert.Equal(GoalStatus.Completed,  fetched.Status);
    }

    // ==================================================================
    // SqliteGoalStore
    // ==================================================================

    /// <summary>
    /// Creates a SqliteGoalStore backed by a temp file so we can verify
    /// persistence (rather than :memory: which is per-connection).
    /// </summary>
    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"goals_test_{Guid.NewGuid():N}.db");

    /// <summary>
    /// Ensures all SQLite connections are released before attempting file deletion.
    /// SQLitePCL on Windows keeps a file handle briefly after Dispose(); forcing
    /// a GC cycle releases the finalizable connection objects.
    /// </summary>
    private static void TryDeleteFile(string path)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup — test temp file */ }
    }

    [Fact]
    public async Task Sqlite_Upsert_ThenGet_RoundTrip()
    {
        var path  = TempDbPath();
        try
        {
            using var store = new SqliteGoalStore($"Data Source={path}");

            var goal = MakeGoal("sqlite-1", "user-s");
            await store.UpsertAsync(goal);

            var fetched = await store.GetAsync("sqlite-1");
            Assert.NotNull(fetched);
            Assert.Equal("sqlite-1",      fetched!.Id);
            Assert.Equal("user-s",        fetched.UserId);
            Assert.Equal(goal.Title,      fetched.Title);
            Assert.Equal(goal.Description,fetched.Description);
            Assert.Equal(goal.Status,     fetched.Status);
            Assert.Equal(goal.Priority,   fetched.Priority);
        }
        finally { TryDeleteFile(path); }
    }

    [Fact]
    public async Task Sqlite_List_ReturnsAllGoalsForUser()
    {
        var path = TempDbPath();
        try
        {
            using var store = new SqliteGoalStore($"Data Source={path}");
            const string user = "user-list";

            await store.UpsertAsync(MakeGoal("l1", user));
            await store.UpsertAsync(MakeGoal("l2", user));
            await store.UpsertAsync(MakeGoal("other", "other-user"));

            var list = await store.ListAsync(user);
            Assert.Equal(2, list.Count);
            Assert.All(list, g => Assert.Equal(user, g.UserId));
        }
        finally { TryDeleteFile(path); }
    }

    [Fact]
    public async Task Sqlite_GetActiveAsync_FiltersByStatus()
    {
        var path = TempDbPath();
        try
        {
            using var store = new SqliteGoalStore($"Data Source={path}");
            const string user = "user-active";

            await store.UpsertAsync(MakeGoal("a1", user, GoalStatus.Active));
            await store.UpsertAsync(MakeGoal("a2", user, GoalStatus.Active));
            await store.UpsertAsync(MakeGoal("c1", user, GoalStatus.Completed));

            var active = await store.GetActiveAsync(user);
            Assert.Equal(2, active.Count);
            Assert.All(active, g => Assert.Equal(GoalStatus.Active, g.Status));
        }
        finally { TryDeleteFile(path); }
    }

    [Fact]
    public async Task Sqlite_Delete_RemovesGoal()
    {
        var path = TempDbPath();
        try
        {
            using var store = new SqliteGoalStore($"Data Source={path}");
            var goal = MakeGoal("del-1", "user-d");

            await store.UpsertAsync(goal);
            await store.DeleteAsync(goal.Id);

            var fetched = await store.GetAsync(goal.Id);
            Assert.Null(fetched);
        }
        finally { TryDeleteFile(path); }
    }

    [Fact]
    public async Task Sqlite_OptionalFields_RoundTripCorrectly()
    {
        var path = TempDbPath();
        try
        {
            using var store = new SqliteGoalStore($"Data Source={path}");

            var due       = DateTimeOffset.UtcNow.AddDays(7);
            var completed = DateTimeOffset.UtcNow.AddDays(-1);
            var goal = new Goal(
                Id:           "opt-1",
                UserId:       "user-opt",
                Title:        "With optionals",
                Description:  "desc",
                Status:       GoalStatus.Completed,
                Priority:     GoalPriority.High,
                CreatedUtc:   DateTimeOffset.UtcNow,
                DueUtc:       due,
                CompletedUtc: completed,
                Notes:        "some notes");

            await store.UpsertAsync(goal);
            var fetched = await store.GetAsync("opt-1");

            Assert.NotNull(fetched);
            Assert.NotNull(fetched!.DueUtc);
            Assert.NotNull(fetched.CompletedUtc);
            Assert.Equal("some notes", fetched.Notes);
            // Round-tripped dates should be within 1 second (ISO-8601 'O' format).
            Assert.Equal(due.ToString("O"), fetched.DueUtc!.Value.ToString("O"));
        }
        finally { TryDeleteFile(path); }
    }

    [Fact]
    public async Task Sqlite_Get_ReturnsNull_WhenNotFound()
    {
        using var store = new SqliteGoalStore("Data Source=:memory:");
        var result = await store.GetAsync("no-such-id");
        Assert.Null(result);
    }

    [Fact]
    public async Task Sqlite_Disposed_ThrowsObjectDisposedException()
    {
        var store = new SqliteGoalStore("Data Source=:memory:");
        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ListAsync("u"));
    }
}
