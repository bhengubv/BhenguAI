// ScheduledTaskTests.cs
//
// Tests for CronScheduleParser, InMemoryScheduledTaskStore, and related models.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Circle.AI.Hosting;
using Xunit;

namespace Circle.AI.Tests;

// ===========================================================================
// CronScheduleParser
// ===========================================================================

public sealed class CronScheduleParserTests
{
    // -----------------------------------------------------------------------
    // Basic wildcard — "* * * * *"
    // -----------------------------------------------------------------------

    [Fact]
    public void EveryMinute_NextOccurrence_IsWithin60Seconds()
    {
        var now  = DateTimeOffset.UtcNow;
        var next = CronScheduleParser.GetNextOccurrence("* * * * *", now);

        Assert.True(next > now, "Next occurrence must be strictly after 'now'.");
        Assert.True((next - now).TotalSeconds <= 60,
            $"Next occurrence should be within 60 seconds, was {(next - now).TotalSeconds:F1}s.");
    }

    [Fact]
    public void EveryMinute_NextOccurrenceIsStrictlyAfterAnchor()
    {
        // "* * * * *" on a whole minute boundary should return the NEXT minute.
        var anchor = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("* * * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 1, 12, 1, 0, TimeSpan.Zero), next);
    }

    // -----------------------------------------------------------------------
    // Daily at 09:00 — "0 9 * * *"
    // -----------------------------------------------------------------------

    [Fact]
    public void DailyAt09_BeforeMidnight_ReturnsNineAMSameDay()
    {
        // Anchor: midnight (00:00). Next 09:00 is the same calendar day.
        var anchor = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("0 9 * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void DailyAt09_After09_ReturnsNineAMNextDay()
    {
        // Anchor: 10:00 — today's 09:00 has already passed.
        var anchor = new DateTimeOffset(2025, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("0 9 * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 2, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void DailyAt09_ExactlyAt09_ReturnsNextDay()
    {
        // Anchor is exactly 09:00 — next occurrence is tomorrow 09:00.
        var anchor = new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("0 9 * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 2, 9, 0, 0, TimeSpan.Zero), next);
    }

    // -----------------------------------------------------------------------
    // Every Monday at 09:00 — "0 9 * * 1"
    // -----------------------------------------------------------------------

    [Fact]
    public void MondayAt09_FromSunday_ReturnsNextMondayNine()
    {
        // 2025-06-01 is a Sunday.
        var anchor = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("0 9 * * 1", anchor);

        // Next Monday is 2025-06-02.
        Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);
        Assert.Equal(9, next.Hour);
        Assert.Equal(0, next.Minute);
    }

    [Fact]
    public void MondayAt09_FromMonday_ReturnsFutureMondayIfPast()
    {
        // 2025-06-02 is a Monday. Anchor is 10:00, so today's 09:00 has passed.
        var anchor = new DateTimeOffset(2025, 6, 2, 10, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("0 9 * * 1", anchor);

        // Must be the NEXT Monday (2025-06-09).
        Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);
        Assert.True(next.Date > anchor.Date);
    }

    // -----------------------------------------------------------------------
    // Every 15 minutes — "*/15 * * * *"
    // -----------------------------------------------------------------------

    [Fact]
    public void Every15Minutes_At14h05_Returns14h15()
    {
        var anchor = new DateTimeOffset(2025, 6, 1, 14, 5, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("*/15 * * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 1, 14, 15, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Every15Minutes_At14h00_Returns14h15()
    {
        // Exact boundary: 14:00 is a valid slot, but the anchor is exactly 14:00,
        // so next is 14:15.
        var anchor = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("*/15 * * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 1, 14, 15, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Every15Minutes_At14h50_Returns15h00()
    {
        var anchor = new DateTimeOffset(2025, 6, 1, 14, 50, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("*/15 * * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 1, 15, 0, 0, TimeSpan.Zero), next);
    }

    // -----------------------------------------------------------------------
    // Twice daily — "0 9,18 * * *"
    // -----------------------------------------------------------------------

    [Fact]
    public void TwiceDaily_Before09_Returns09()
    {
        var anchor = new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("0 9,18 * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void TwiceDaily_Between09And18_Returns18()
    {
        var anchor = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("0 9,18 * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 1, 18, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void TwiceDaily_After18_ReturnsNextDay09()
    {
        var anchor = new DateTimeOffset(2025, 6, 1, 19, 0, 0, TimeSpan.Zero);
        var next   = CronScheduleParser.GetNextOccurrence("0 9,18 * * *", anchor);

        Assert.Equal(new DateTimeOffset(2025, 6, 2, 9, 0, 0, TimeSpan.Zero), next);
    }

    // -----------------------------------------------------------------------
    // Error handling
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespaceExpression_ThrowsArgumentException(string expr)
    {
        Assert.Throws<ArgumentException>(() =>
            CronScheduleParser.GetNextOccurrence(expr, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void TooFewFields_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            CronScheduleParser.GetNextOccurrence("* * * *", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void TooManyFields_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            CronScheduleParser.GetNextOccurrence("* * * * * *", DateTimeOffset.UtcNow));
    }
}

// ===========================================================================
// InMemoryScheduledTaskStore
// ===========================================================================

public sealed class InMemoryScheduledTaskStoreTests
{
    private static CronJob MakeJob(
        string id            = "job-1",
        bool isEnabled       = true,
        DateTimeOffset? next = null,
        CronJobState state   = CronJobState.Pending) =>
        new CronJob(
            Id             : id,
            Name           : $"Test job {id}",
            Prompt         : "Tell me the time.",
            CronExpression : "*/15 * * * *",
            Delivery       : DeliveryTarget.Local,
            IsEnabled      : isEnabled,
            NextRunUtc     : next,
            State          : state);

    // -----------------------------------------------------------------------
    // Upsert / List / Get
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Upsert_NewJob_CanBeRetrievedViaGet()
    {
        var store = new InMemoryScheduledTaskStore();
        var job   = MakeJob("j1");

        await store.UpsertAsync(job);
        var retrieved = await store.GetAsync("j1");

        Assert.NotNull(retrieved);
        Assert.Equal("j1", retrieved!.Id);
        Assert.Equal("Test job j1", retrieved.Name);
    }

    [Fact]
    public async Task Upsert_ExistingJob_ReplacesRecord()
    {
        var store = new InMemoryScheduledTaskStore();
        await store.UpsertAsync(MakeJob("j2", state: CronJobState.Pending));

        var updated = MakeJob("j2", state: CronJobState.Succeeded);
        await store.UpsertAsync(updated);

        var retrieved = await store.GetAsync("j2");
        Assert.Equal(CronJobState.Succeeded, retrieved!.State);
    }

    [Fact]
    public async Task List_MultipleJobs_ReturnsAll()
    {
        var store = new InMemoryScheduledTaskStore();
        await store.UpsertAsync(MakeJob("a"));
        await store.UpsertAsync(MakeJob("b"));
        await store.UpsertAsync(MakeJob("c"));

        var list = await store.ListAsync();
        Assert.Equal(3, list.Count);
        Assert.Contains(list, j => j.Id == "a");
        Assert.Contains(list, j => j.Id == "b");
        Assert.Contains(list, j => j.Id == "c");
    }

    [Fact]
    public async Task Get_NonExistentId_ReturnsNull()
    {
        var store = new InMemoryScheduledTaskStore();
        var result = await store.GetAsync("does-not-exist");
        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Delete
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Delete_ExistingJob_RemovesIt()
    {
        var store = new InMemoryScheduledTaskStore();
        await store.UpsertAsync(MakeJob("del-me"));

        await store.DeleteAsync("del-me");

        var retrieved = await store.GetAsync("del-me");
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task Delete_NonExistentJob_IsNoOp()
    {
        var store = new InMemoryScheduledTaskStore();
        // Should not throw.
        await store.DeleteAsync("ghost");
    }

    // -----------------------------------------------------------------------
    // GetDueJobsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDueJobs_PastNextRunUtc_ReturnsDueJob()
    {
        var store = new InMemoryScheduledTaskStore();
        var dueJob = MakeJob("due", isEnabled: true,
            next: DateTimeOffset.UtcNow.AddMinutes(-1));

        await store.UpsertAsync(dueJob);

        var due = await store.GetDueJobsAsync();
        Assert.Single(due);
        Assert.Equal("due", due[0].Id);
    }

    [Fact]
    public async Task GetDueJobs_FutureNextRunUtc_NotReturned()
    {
        var store = new InMemoryScheduledTaskStore();
        var futureJob = MakeJob("future", isEnabled: true,
            next: DateTimeOffset.UtcNow.AddMinutes(5));

        await store.UpsertAsync(futureJob);

        var due = await store.GetDueJobsAsync();
        Assert.Empty(due);
    }

    [Fact]
    public async Task GetDueJobs_DisabledJob_NotReturnedEvenIfPastDue()
    {
        var store = new InMemoryScheduledTaskStore();
        var disabled = MakeJob("disabled", isEnabled: false,
            next: DateTimeOffset.UtcNow.AddMinutes(-1));

        await store.UpsertAsync(disabled);

        var due = await store.GetDueJobsAsync();
        Assert.Empty(due);
    }

    [Fact]
    public async Task GetDueJobs_NullNextRunUtc_NotReturned()
    {
        var store = new InMemoryScheduledTaskStore();
        var noNext = MakeJob("no-next", isEnabled: true, next: null);

        await store.UpsertAsync(noNext);

        var due = await store.GetDueJobsAsync();
        Assert.Empty(due);
    }

    [Fact]
    public async Task GetDueJobs_MixedJobs_ReturnsOnlyEligible()
    {
        var store = new InMemoryScheduledTaskStore();

        await store.UpsertAsync(MakeJob("due1", true,  DateTimeOffset.UtcNow.AddMinutes(-2)));
        await store.UpsertAsync(MakeJob("due2", true,  DateTimeOffset.UtcNow.AddMinutes(-1)));
        await store.UpsertAsync(MakeJob("future", true, DateTimeOffset.UtcNow.AddMinutes(10)));
        await store.UpsertAsync(MakeJob("disabled", false, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await store.UpsertAsync(MakeJob("no-next", true, null));

        var due = await store.GetDueJobsAsync();
        Assert.Equal(2, due.Count);
        Assert.All(due, j => Assert.Contains(j.Id, new[] { "due1", "due2" }));
    }

    // -----------------------------------------------------------------------
    // CronJob record equality (value semantics)
    // -----------------------------------------------------------------------

    [Fact]
    public void CronJob_WithExpression_EqualityWorks()
    {
        var a = new CronJob("id", "Name", "prompt", "* * * * *", DeliveryTarget.Local);
        var b = new CronJob("id", "Name", "prompt", "* * * * *", DeliveryTarget.Local);
        Assert.Equal(a, b);
    }

    [Fact]
    public void CronJob_With_MutatesCorrectly()
    {
        var original = new CronJob("j", "Job", "prompt", "0 9 * * *", DeliveryTarget.Push);
        var updated  = original with { State = CronJobState.Succeeded };
        Assert.Equal(CronJobState.Pending,   original.State);
        Assert.Equal(CronJobState.Succeeded, updated.State);
    }
}
