// MemoryGapTests.cs
//
// Tests for GAP 3 (SqliteEpisodicStore) and GAP 6 (FeedbackAnalyser).

using System;
using System.Threading.Tasks;
using Bhengu.AI.Memory;
using Xunit;

namespace Bhengu.AI.Tests;

// ---------------------------------------------------------------------------
// SqliteEpisodicStore
// ---------------------------------------------------------------------------

public sealed class SqliteEpisodicStoreTests : IDisposable
{
    // Each test gets its own store backed by a unique named in-memory database
    // so tests are fully isolated without relying on file I/O.
    // "Mode=Memory;Cache=Shared" keeps the DB alive as long as at least one
    // connection is open, and named databases are shared within a process.
    private readonly string _dbName = Guid.NewGuid().ToString("N");
    private readonly SqliteEpisodicStore _store;

    public SqliteEpisodicStoreTests()
    {
        _store = new SqliteEpisodicStore(
            $"Data Source={_dbName};Mode=Memory;Cache=Shared");
    }

    public void Dispose() => _store.Dispose();

    // --- Constructor ---

    [Fact]
    public void Constructor_ValidConnectionString_DoesNotThrow()
    {
        using var s = new SqliteEpisodicStore(
            $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        Assert.NotNull(s);
    }

    [Fact]
    public void Constructor_NullConnectionString_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SqliteEpisodicStore(null!));
    }

    [Fact]
    public void Constructor_EmptyConnectionString_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SqliteEpisodicStore("   "));
    }

    // --- AddAsync / GetRecentAsync ---

    [Fact]
    public async Task AddAsync_ThenGetRecent_ReturnsEntry()
    {
        var entry = new EpisodicMemoryEntry
        {
            UserText      = "hello",
            AssistantText = "world",
        };

        await _store.AddAsync(entry);
        var recent = await _store.GetRecentAsync(10);

        Assert.Single(recent);
        Assert.Equal("hello", recent[0].UserText);
        Assert.Equal("world", recent[0].AssistantText);
    }

    [Fact]
    public async Task AddAsync_PreservesAllFields()
    {
        var id  = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var entry = new EpisodicMemoryEntry
        {
            Id            = id,
            RecordedAtUtc = now,
            UserText      = "user msg",
            AssistantText = "assistant msg",
            AppContext    = "tgn.bidbaas",
            Embedding     = new float[] { 0.1f, 0.2f, 0.3f },
            Tags          = new System.Collections.Generic.Dictionary<string, string>
            {
                ["locale"]    = "en-ZA",
                ["sentiment"] = "positive",
            },
        };

        await _store.AddAsync(entry);
        var results = await _store.GetRecentAsync(1);

        Assert.Single(results);
        var r = results[0];
        Assert.Equal(id,            r.Id);
        Assert.Equal("user msg",    r.UserText);
        Assert.Equal("tgn.bidbaas", r.AppContext);
        Assert.NotNull(r.Embedding);
        Assert.Equal(3,             r.Embedding!.Length);
        Assert.NotNull(r.Tags);
        Assert.Equal("en-ZA",       r.Tags!["locale"]);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirst()
    {
        // Insert older entry first, then a newer one.
        var older = new EpisodicMemoryEntry
        {
            RecordedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-60),
            UserText      = "older",
            AssistantText = string.Empty,
        };
        var newer = new EpisodicMemoryEntry
        {
            RecordedAtUtc = DateTimeOffset.UtcNow,
            UserText      = "newer",
            AssistantText = string.Empty,
        };

        await _store.AddAsync(older);
        await _store.AddAsync(newer);

        var recent = await _store.GetRecentAsync(2);
        Assert.Equal(2,       recent.Count);
        Assert.Equal("newer", recent[0].UserText);
        Assert.Equal("older", recent[1].UserText);
    }

    [Fact]
    public async Task GetRecentAsync_RespectsCountLimit()
    {
        for (var i = 0; i < 5; i++)
            await _store.AddAsync(new EpisodicMemoryEntry
            {
                UserText      = $"msg{i}",
                AssistantText = string.Empty,
            });

        var recent = await _store.GetRecentAsync(3);
        Assert.Equal(3, recent.Count);
    }

    // --- SearchAsync ---

    [Fact]
    public async Task SearchAsync_NullEmbedding_FallsBackToRecency()
    {
        await _store.AddAsync(new EpisodicMemoryEntry { UserText = "first",  AssistantText = string.Empty });
        await _store.AddAsync(new EpisodicMemoryEntry { UserText = "second", AssistantText = string.Empty });

        var results = await _store.SearchAsync(null, topK: 5);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_WithEmbedding_ReturnsMostSimilar()
    {
        // Two entries — one aligned with query, one orthogonal.
        var aligned = new EpisodicMemoryEntry
        {
            UserText      = "aligned",
            AssistantText = string.Empty,
            Embedding     = new float[] { 1f, 0f, 0f },
        };
        var orthogonal = new EpisodicMemoryEntry
        {
            UserText      = "orthogonal",
            AssistantText = string.Empty,
            Embedding     = new float[] { 0f, 1f, 0f },
        };

        await _store.AddAsync(aligned);
        await _store.AddAsync(orthogonal);

        var query   = new float[] { 1f, 0f, 0f };
        var results = await _store.SearchAsync(query, topK: 1);

        Assert.Single(results);
        Assert.Equal("aligned", results[0].UserText);
    }

    [Fact]
    public async Task SearchAsync_NoStoredEmbeddings_ReturnsEmpty()
    {
        await _store.AddAsync(new EpisodicMemoryEntry { UserText = "no embedding", AssistantText = string.Empty });

        // queryEmbedding non-null → only rows with stored embeddings considered.
        var results = await _store.SearchAsync(new float[] { 1f, 0f }, topK: 5);
        Assert.Empty(results);
    }

    // --- CountAsync ---

    [Fact]
    public async Task CountAsync_EmptyStore_ReturnsZero()
    {
        Assert.Equal(0, await _store.CountAsync());
    }

    [Fact]
    public async Task CountAsync_AfterAdds_ReturnsCorrectCount()
    {
        await _store.AddAsync(new EpisodicMemoryEntry { UserText = "a", AssistantText = string.Empty });
        await _store.AddAsync(new EpisodicMemoryEntry { UserText = "b", AssistantText = string.Empty });
        Assert.Equal(2, await _store.CountAsync());
    }

    // --- PruneOlderThanAsync ---

    [Fact]
    public async Task PruneOlderThanAsync_RemovesOldEntries()
    {
        var old = new EpisodicMemoryEntry
        {
            RecordedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
            UserText      = "old",
            AssistantText = string.Empty,
        };
        var fresh = new EpisodicMemoryEntry
        {
            RecordedAtUtc = DateTimeOffset.UtcNow,
            UserText      = "fresh",
            AssistantText = string.Empty,
        };

        await _store.AddAsync(old);
        await _store.AddAsync(fresh);

        var deleted = await _store.PruneOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Equal(1,       deleted);
        Assert.Equal(1,       await _store.CountAsync());
        var remaining = await _store.GetRecentAsync(10);
        Assert.Equal("fresh", remaining[0].UserText);
    }

    [Fact]
    public async Task PruneOlderThanAsync_NothingToDelete_ReturnsZero()
    {
        await _store.AddAsync(new EpisodicMemoryEntry { UserText = "fresh", AssistantText = string.Empty });

        var deleted = await _store.PruneOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-30));
        Assert.Equal(0, deleted);
    }
}

// ---------------------------------------------------------------------------
// FeedbackAnalyser
// ---------------------------------------------------------------------------

public sealed class FeedbackAnalyserTests
{
    [Fact]
    public void Analyse_EmptySignals_ReturnsZeroDeltas()
    {
        var analyser = new FeedbackAnalyser();
        var result = analyser.Analyse(Array.Empty<FeedbackSignal>());

        Assert.Equal(0f, result.VerbosityDelta);
        Assert.Equal(0f, result.FormalityDelta);
        Assert.Empty(result.PreferredTopics);
    }

    [Fact]
    public void Analyse_AllPositive_ReturnsPositiveVerbosityDelta()
    {
        var analyser = new FeedbackAnalyser();
        var signals = new[]
        {
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
        };

        var result = analyser.Analyse(signals);

        Assert.True(result.VerbosityDelta > 0f);
    }

    [Fact]
    public void Analyse_AllNegative_ReturnsNegativeVerbosityDelta()
    {
        var analyser = new FeedbackAnalyser();
        var signals = new[]
        {
            new FeedbackSignal { Polarity = FeedbackPolarity.Negative },
            new FeedbackSignal { Polarity = FeedbackPolarity.Negative },
            new FeedbackSignal { Polarity = FeedbackPolarity.Negative },
        };

        var result = analyser.Analyse(signals);

        Assert.True(result.VerbosityDelta < 0f);
    }

    [Fact]
    public void Analyse_Mixed_ReturnsZeroDelta()
    {
        var analyser = new FeedbackAnalyser();
        var signals = new[]
        {
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Negative },
        };

        var result = analyser.Analyse(signals);

        Assert.Equal(0f, result.VerbosityDelta);
    }

    [Fact]
    public void Analyse_ExactlyAtPositiveThreshold_ReturnsZeroDelta()
    {
        // 70% positive (not OVER 70%) → should stay at 0f.
        var analyser = new FeedbackAnalyser();
        var signals = new[]
        {
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Negative },
            new FeedbackSignal { Polarity = FeedbackPolarity.Negative },
            new FeedbackSignal { Polarity = FeedbackPolarity.Negative },
        };
        // 7/10 = 70.0% positive — not > 70%, so delta must be 0f.
        var result = analyser.Analyse(signals);
        Assert.Equal(0f, result.VerbosityDelta);
    }

    [Fact]
    public void Analyse_JustOverPositiveThreshold_ReturnsPositiveDelta()
    {
        var analyser = new FeedbackAnalyser();
        // 8/10 = 80% positive → +0.05f.
        var signals = new[]
        {
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { Polarity = FeedbackPolarity.Negative },
            new FeedbackSignal { Polarity = FeedbackPolarity.Negative },
        };

        var result = analyser.Analyse(signals);
        Assert.Equal(+0.05f, result.VerbosityDelta);
    }

    [Fact]
    public void Analyse_CorrectionSignals_CountedAsNeutral()
    {
        var analyser = new FeedbackAnalyser();
        var signals = new[]
        {
            new FeedbackSignal { Polarity = FeedbackPolarity.Correction },
            new FeedbackSignal { Polarity = FeedbackPolarity.Correction },
            new FeedbackSignal { Polarity = FeedbackPolarity.Correction },
        };

        // 0 positive, 0 negative — ratio 0 — no threshold crossed.
        var result = analyser.Analyse(signals);
        Assert.Equal(0f, result.VerbosityDelta);
    }

    [Fact]
    public void Analyse_WindowSizeLimit_OnlyConsidersMostRecent()
    {
        // Window of 3. Provide 5 signals — oldest 2 are positive (would push
        // the window positive if included), but the most recent 3 are negative.
        var analyser = new FeedbackAnalyser(windowSize: 3);

        var now = DateTimeOffset.UtcNow;
        var signals = new[]
        {
            new FeedbackSignal { RecordedAtUtc = now.AddSeconds(-40), Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { RecordedAtUtc = now.AddSeconds(-30), Polarity = FeedbackPolarity.Positive },
            new FeedbackSignal { RecordedAtUtc = now.AddSeconds(-20), Polarity = FeedbackPolarity.Negative },
            new FeedbackSignal { RecordedAtUtc = now.AddSeconds(-10), Polarity = FeedbackPolarity.Negative },
            new FeedbackSignal { RecordedAtUtc = now.AddSeconds(  0), Polarity = FeedbackPolarity.Negative },
        };

        var result = analyser.Analyse(signals);
        // 3 negatives in window of 3 → 100% negative → -0.1f
        Assert.True(result.VerbosityDelta < 0f);
    }

    [Fact]
    public void Analyse_PreferredTopics_AlwaysEmpty()
    {
        // FeedbackSignal has no tag/topic fields — adapter always returns [].
        var analyser = new FeedbackAnalyser();
        var signals  = new[]
        {
            new FeedbackSignal { Polarity = FeedbackPolarity.Positive },
        };

        var result = analyser.Analyse(signals);
        Assert.Empty(result.PreferredTopics);
    }

    [Fact]
    public void Constructor_InvalidWindowSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeedbackAnalyser(0));
    }

    [Fact]
    public void Constructor_NegativeWindowSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeedbackAnalyser(-5));
    }
}
