using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bhengu.AI.Memory;
using Xunit;

namespace Bhengu.AI.Tests;

// ============================================================================
// InMemoryEpisodicStore
// ============================================================================

public sealed class InMemoryEpisodicStoreTests
{
    // ------------------------------------------------------------------
    // Constructor guards
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_ZeroMaxEntries_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryEpisodicStore(0));
    }

    [Fact]
    public void Constructor_NegativeMaxEntries_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryEpisodicStore(-1));
    }

    // ------------------------------------------------------------------
    // AddAsync / CountAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task AddAsync_NullEntry_Throws()
    {
        var store = new InMemoryEpisodicStore();
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AddAsync(null!));
    }

    [Fact]
    public async Task AddAsync_SingleEntry_CountIsOne()
    {
        var store = new InMemoryEpisodicStore();
        await store.AddAsync(MakeEntry("hi", "hello"));
        Assert.Equal(1, await store.CountAsync());
    }

    [Fact]
    public async Task AddAsync_BeyondCapacity_EvictsOldest()
    {
        var store = new InMemoryEpisodicStore(maxEntries: 3);

        for (int i = 0; i < 5; i++)
            await store.AddAsync(MakeEntry($"user {i}", $"asst {i}"));

        // Count is capped.
        Assert.Equal(3, await store.CountAsync());

        // The 3 most-recent entries survive.
        var recent = await store.GetRecentAsync(10);
        Assert.Equal(3, recent.Count);
        var texts = recent.Select(e => e.UserText).ToHashSet();
        Assert.Contains("user 4", texts);
        Assert.Contains("user 3", texts);
        Assert.Contains("user 2", texts);
    }

    // ------------------------------------------------------------------
    // GetRecentAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetRecentAsync_EmptyStore_ReturnsEmptyList()
    {
        var store = new InMemoryEpisodicStore();
        var result = await store.GetRecentAsync(5);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirst()
    {
        var store = new InMemoryEpisodicStore();
        // Add entries with explicit timestamps to control ordering.
        var now = DateTimeOffset.UtcNow;
        await store.AddAsync(MakeEntry("old", "old-asst",
            at: now.AddMinutes(-10)));
        await store.AddAsync(MakeEntry("new", "new-asst",
            at: now));

        var result = await store.GetRecentAsync(5);
        Assert.Equal(2, result.Count);
        Assert.Equal("new", result[0].UserText);
        Assert.Equal("old", result[1].UserText);
    }

    [Fact]
    public async Task GetRecentAsync_LimitRespected()
    {
        var store = new InMemoryEpisodicStore();
        for (int i = 0; i < 10; i++)
            await store.AddAsync(MakeEntry($"u{i}", $"a{i}"));

        var result = await store.GetRecentAsync(3);
        Assert.Equal(3, result.Count);
    }

    // ------------------------------------------------------------------
    // SearchAsync — recency fallback (null embedding)
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_NullEmbedding_FallsBackToRecency()
    {
        var store = new InMemoryEpisodicStore();
        var now = DateTimeOffset.UtcNow;
        await store.AddAsync(MakeEntry("first",  "a1", at: now.AddMinutes(-5)));
        await store.AddAsync(MakeEntry("second", "a2", at: now));

        var result = await store.SearchAsync(null, topK: 5);

        Assert.Equal(2, result.Count);
        Assert.Equal("second", result[0].UserText);
    }

    // ------------------------------------------------------------------
    // SearchAsync — cosine similarity
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WithEmbedding_ReturnsByDescendingSimilarity()
    {
        var store = new InMemoryEpisodicStore();

        // Entry A: embedding close to the query [1, 0, 0, 0].
        await store.AddAsync(MakeEntry("A", "a",
            embedding: new float[] { 0.99f, 0.01f, 0f, 0f }));

        // Entry B: embedding far from the query.
        await store.AddAsync(MakeEntry("B", "b",
            embedding: new float[] { 0f, 0f, 0f, 1f }));

        var query = new float[] { 1f, 0f, 0f, 0f };
        var result = await store.SearchAsync(query, topK: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0].UserText); // most similar first
    }

    [Fact]
    public async Task SearchAsync_EntriesWithNoEmbedding_AreExcludedFromSimilaritySearch()
    {
        var store = new InMemoryEpisodicStore();
        await store.AddAsync(MakeEntry("no-embd", "x", embedding: null));
        await store.AddAsync(MakeEntry("has-embd", "y",
            embedding: new float[] { 1f, 0f }));

        var query = new float[] { 1f, 0f };
        var result = await store.SearchAsync(query, topK: 5);

        Assert.Single(result);
        Assert.Equal("has-embd", result[0].UserText);
    }

    [Fact]
    public async Task SearchAsync_EmbeddingDimensionMismatch_EntriesExcluded()
    {
        var store = new InMemoryEpisodicStore();
        // 4-D embedding stored, but query is 2-D.
        await store.AddAsync(MakeEntry("dim4", "a",
            embedding: new float[] { 1f, 0f, 0f, 0f }));

        var query = new float[] { 1f, 0f };
        var result = await store.SearchAsync(query, topK: 5);

        // Dimension mismatch → entry excluded.
        Assert.Empty(result);
    }

    // ------------------------------------------------------------------
    // PruneOlderThanAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task PruneOlderThanAsync_RemovesOldEntries_ReturnsCount()
    {
        var store = new InMemoryEpisodicStore();
        var cutoff = DateTimeOffset.UtcNow;

        await store.AddAsync(MakeEntry("old",  "ao", at: cutoff.AddHours(-2)));
        await store.AddAsync(MakeEntry("new",  "an", at: cutoff.AddHours(+1)));

        int removed = await store.PruneOlderThanAsync(cutoff);

        Assert.Equal(1, removed);
        Assert.Equal(1, await store.CountAsync());
    }

    [Fact]
    public async Task PruneOlderThanAsync_NothingOld_ReturnsZero()
    {
        var store = new InMemoryEpisodicStore();
        await store.AddAsync(MakeEntry("fresh", "a", at: DateTimeOffset.UtcNow));

        int removed = await store.PruneOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Equal(0, removed);
    }

    // ------------------------------------------------------------------
    // Cancellation
    // ------------------------------------------------------------------

    [Fact]
    public async Task AddAsync_CancelledToken_Throws()
    {
        var store = new InMemoryEpisodicStore();
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.AddAsync(MakeEntry("x", "y"), cts.Token));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static EpisodicMemoryEntry MakeEntry(
        string user,
        string asst,
        DateTimeOffset? at = null,
        float[]? embedding = null)
        => new()
        {
            UserText      = user,
            AssistantText = asst,
            RecordedAtUtc = at ?? DateTimeOffset.UtcNow,
            Embedding     = embedding,
        };
}

// ============================================================================
// EpisodicMemoryEntry — model tests
// ============================================================================

public sealed class EpisodicMemoryEntryTests
{
    [Fact]
    public void NewEntry_HasUniqueId()
    {
        var a = new EpisodicMemoryEntry();
        var b = new EpisodicMemoryEntry();
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void RecordedAtUtc_DefaultsToRecentTime()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var entry = new EpisodicMemoryEntry();
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.InRange(entry.RecordedAtUtc, before, after);
    }
}
