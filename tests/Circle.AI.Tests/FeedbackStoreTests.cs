using System;
using System.Threading.Tasks;
using Circle.AI.Memory;
using Xunit;

namespace Circle.AI.Tests;

public sealed class FeedbackSignalTests
{
    [Fact]
    public void NewSignal_HasUniqueId()
    {
        var a = new FeedbackSignal();
        var b = new FeedbackSignal();
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void RecordedAtUtc_DefaultsToRecentTime()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var s = new FeedbackSignal();
        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.InRange(s.RecordedAtUtc, before, after);
    }

    [Fact]
    public void Polarity_CanBeSetToAllValues()
    {
        var pos = new FeedbackSignal { Polarity = FeedbackPolarity.Positive };
        var neg = new FeedbackSignal { Polarity = FeedbackPolarity.Negative };
        var cor = new FeedbackSignal { Polarity = FeedbackPolarity.Correction };

        Assert.Equal(FeedbackPolarity.Positive,   pos.Polarity);
        Assert.Equal(FeedbackPolarity.Negative,   neg.Polarity);
        Assert.Equal(FeedbackPolarity.Correction, cor.Polarity);
    }
}

// ============================================================================
// InMemoryFeedbackStore
// ============================================================================

public sealed class InMemoryFeedbackStoreTests
{
    [Fact]
    public async Task AddAsync_NullSignal_Throws()
    {
        var store = new InMemoryFeedbackStore();
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AddAsync(null!));
    }

    [Fact]
    public async Task AddAsync_IncrementsCount()
    {
        var store = new InMemoryFeedbackStore();
        await store.AddAsync(Make(FeedbackPolarity.Positive));
        Assert.Equal(1, await store.CountAsync());
    }

    [Fact]
    public async Task GetRecentAsync_EmptyStore_ReturnsEmpty()
    {
        var store = new InMemoryFeedbackStore();
        var result = await store.GetRecentAsync(10);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirst()
    {
        var store = new InMemoryFeedbackStore();
        var now = DateTimeOffset.UtcNow;

        await store.AddAsync(Make(FeedbackPolarity.Positive,
            at: now.AddMinutes(-10), user: "old"));
        await store.AddAsync(Make(FeedbackPolarity.Negative,
            at: now, user: "new"));

        var result = await store.GetRecentAsync(10);
        Assert.Equal(2, result.Count);
        Assert.Equal("new", result[0].UserText);
    }

    [Fact]
    public async Task PositiveRatioAsync_NoSignals_ReturnsNull()
    {
        var store = new InMemoryFeedbackStore();
        Assert.Null(await store.PositiveRatioAsync());
    }

    [Fact]
    public async Task PositiveRatioAsync_AllPositive_ReturnsOne()
    {
        var store = new InMemoryFeedbackStore();
        await store.AddAsync(Make(FeedbackPolarity.Positive));
        await store.AddAsync(Make(FeedbackPolarity.Positive));

        var ratio = await store.PositiveRatioAsync();
        Assert.NotNull(ratio);
        Assert.Equal(1.0, ratio!.Value, precision: 5);
    }

    [Fact]
    public async Task PositiveRatioAsync_MixedSignals_ReturnsCorrectRatio()
    {
        var store = new InMemoryFeedbackStore();
        await store.AddAsync(Make(FeedbackPolarity.Positive));
        await store.AddAsync(Make(FeedbackPolarity.Positive));
        await store.AddAsync(Make(FeedbackPolarity.Negative));

        var ratio = await store.PositiveRatioAsync();
        Assert.NotNull(ratio);
        Assert.InRange(ratio!.Value, 0.66, 0.68); // 2/3
    }

    [Fact]
    public async Task MaxSignals_EvictsOldestWhenExceeded()
    {
        var store = new InMemoryFeedbackStore(maxSignals: 3);
        for (int i = 0; i < 5; i++)
            await store.AddAsync(Make(FeedbackPolarity.Positive, user: $"u{i}"));

        Assert.Equal(3, await store.CountAsync());
    }

    [Fact]
    public async Task CountAsync_CancelledToken_Throws()
    {
        var store = new InMemoryFeedbackStore();
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.CountAsync(cts.Token));
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    private static FeedbackSignal Make(
        FeedbackPolarity polarity,
        DateTimeOffset? at = null,
        string user = "user") =>
        new()
        {
            UserText      = user,
            AssistantText = "response",
            Polarity      = polarity,
            RecordedAtUtc = at ?? DateTimeOffset.UtcNow,
        };
}
