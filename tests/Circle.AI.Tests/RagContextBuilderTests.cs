using System;
using System.Threading.Tasks;
using Circle.AI.Memory;
using Xunit;

namespace Circle.AI.Tests;

public sealed class RagContextBuilderTests
{
    // ------------------------------------------------------------------
    // Constructor guards
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RagContextBuilder(null!));
    }

    // ------------------------------------------------------------------
    // Empty / missing query
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildContextAsync_EmptyQuery_ReturnsEmpty()
    {
        var builder = new RagContextBuilder(new InMemoryEpisodicStore());
        Assert.Equal(string.Empty, await builder.BuildContextAsync(""));
    }

    [Fact]
    public async Task BuildContextAsync_WhitespaceQuery_ReturnsEmpty()
    {
        var builder = new RagContextBuilder(new InMemoryEpisodicStore());
        Assert.Equal(string.Empty, await builder.BuildContextAsync("   "));
    }

    // ------------------------------------------------------------------
    // Empty store
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildContextAsync_EmptyStore_ReturnsEmpty()
    {
        var builder = new RagContextBuilder(new InMemoryEpisodicStore());
        var result = await builder.BuildContextAsync("hello");
        Assert.Equal(string.Empty, result);
    }

    // ------------------------------------------------------------------
    // Non-empty store — recency fallback (no embedder)
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildContextAsync_WithEntries_ReturnsFormattedBlock()
    {
        var store = new InMemoryEpisodicStore();
        await store.AddAsync(new EpisodicMemoryEntry
        {
            UserText      = "What is SDPKT?",
            AssistantText = "SDPKT is the TGN wallet.",
            RecordedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
        });

        var builder = new RagContextBuilder(store, embedder: null, topK: 3);
        var result = await builder.BuildContextAsync("tell me about the wallet");

        Assert.NotEmpty(result);
        Assert.Contains("What is SDPKT?", result);
        Assert.Contains("SDPKT is the TGN wallet.", result);
        Assert.Contains("[Relevant past exchanges", result);
    }

    [Fact]
    public async Task BuildContextAsync_TopKRespected()
    {
        var store = new InMemoryEpisodicStore();
        for (int i = 0; i < 10; i++)
            await store.AddAsync(new EpisodicMemoryEntry
            {
                UserText      = $"question {i}",
                AssistantText = $"answer {i}",
            });

        var builder = new RagContextBuilder(store, embedder: null, topK: 2);
        var result = await builder.BuildContextAsync("any question");

        // Only 2 entries should appear — count occurrences of the bullet prefix.
        int bulletCount = CountOccurrences(result, "• [");
        Assert.Equal(2, bulletCount);
    }

    [Fact]
    public async Task BuildContextAsync_AppContextIncluded_WhenSet()
    {
        var store = new InMemoryEpisodicStore();
        await store.AddAsync(new EpisodicMemoryEntry
        {
            UserText      = "bid query",
            AssistantText = "bid answer",
            AppContext     = "tgn.bidbaas",
        });

        var builder = new RagContextBuilder(store, topK: 3);
        var result = await builder.BuildContextAsync("bidding");

        Assert.Contains("tgn.bidbaas", result);
    }

    // ------------------------------------------------------------------
    // Resilience — store throws
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildContextAsync_StoreThrows_ReturnsEmpty()
    {
        var builder = new RagContextBuilder(new ThrowingEpisodicStore());
        // Must not throw; RAG is best-effort.
        var result = await builder.BuildContextAsync("query");
        Assert.Equal(string.Empty, result);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static int CountOccurrences(string text, string token)
    {
        int count = 0, start = 0;
        while ((start = text.IndexOf(token, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += token.Length;
        }
        return count;
    }

    /// <summary>Store that always throws — used to test resilience.</summary>
    private sealed class ThrowingEpisodicStore : IEpisodicMemoryStore
    {
        public Task AddAsync(EpisodicMemoryEntry entry, System.Threading.CancellationToken ct = default)
            => throw new InvalidOperationException("store failure");
        public Task<System.Collections.Generic.IReadOnlyList<EpisodicMemoryEntry>> SearchAsync(
            float[]? q, int topK = 5, System.Threading.CancellationToken ct = default)
            => throw new InvalidOperationException("store failure");
        public Task<System.Collections.Generic.IReadOnlyList<EpisodicMemoryEntry>> GetRecentAsync(
            int count = 10, System.Threading.CancellationToken ct = default)
            => throw new InvalidOperationException("store failure");
        public Task<int> CountAsync(System.Threading.CancellationToken ct = default)
            => throw new InvalidOperationException("store failure");
        public Task<int> PruneOlderThanAsync(DateTimeOffset cutoff, System.Threading.CancellationToken ct = default)
            => throw new InvalidOperationException("store failure");
    }
}
