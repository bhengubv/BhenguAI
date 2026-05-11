// ProactiveReasoningTests.cs
//
// Unit tests for IdleTrigger, ScheduleTrigger, and ProactiveReasoningService
// (Track 4 — proactive reasoning engine).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Hosting;
using Bhengu.AI.Inference;
using Bhengu.AI.Memory;
using Bhengu.AI.Tools;
using Xunit;

namespace Bhengu.AI.Tests;

// ---------------------------------------------------------------------------
// Minimal IAIService fake — only AskAsync is exercised by the service
// ---------------------------------------------------------------------------

internal sealed class FakeAIService : IAIService
{
    private readonly string _reply;
    public FakeAIService(string reply = "Hello!") => _reply = reply;

    public int AskCallCount { get; private set; }
    public string? LastPrompt { get; private set; }

    public bool IsReady => true;

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<string> AskAsync(string question, CancellationToken ct = default)
    {
        AskCallCount++;
        LastPrompt = question;
        return Task.FromResult(_reply);
    }

    public Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
        => Task.FromResult(_reply);

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return _reply;
    }

    public Task<ToolResult> InvokeToolAsync(ToolInvocation invocation, CancellationToken ct = default)
        => Task.FromResult(new ToolResult { ToolName = "none", Success = false });

    public Task<string> AgenticChatAsync(
        string prompt,
        GenerationOptions? options = null,
        CancellationToken ct = default)
        => Task.FromResult(_reply);

    public Task SubmitFeedbackAsync(FeedbackSignal signal, CancellationToken ct = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// ---------------------------------------------------------------------------
// IdleTrigger tests
// ---------------------------------------------------------------------------

public sealed class IdleTriggerTests
{
    private static ProactiveContext MakeContext(TimeSpan idle) =>
        new(
            UserId:                   "u1",
            NowUtc:                   DateTimeOffset.UtcNow,
            TimeSinceLastInteraction: idle,
            AffectState:              null,
            ActiveGoals:              Array.Empty<Goal>());

    [Fact]
    public async Task IdleTrigger_FiresWhenAboveThreshold()
    {
        var trigger = new IdleTrigger(TimeSpan.FromHours(4));
        var context = MakeContext(TimeSpan.FromHours(5)); // 5 h > 4 h threshold

        var met = await trigger.IsMetAsync(context);

        Assert.True(met);
    }

    [Fact]
    public async Task IdleTrigger_DoesNotFireWhenBelowThreshold()
    {
        var trigger = new IdleTrigger(TimeSpan.FromHours(4));
        var context = MakeContext(TimeSpan.FromHours(2)); // 2 h < 4 h threshold

        var met = await trigger.IsMetAsync(context);

        Assert.False(met);
    }

    [Fact]
    public async Task IdleTrigger_DoesNotFireWhenExactlyAtThreshold()
    {
        var trigger = new IdleTrigger(TimeSpan.FromHours(4));
        // Exactly equal — trigger uses > (strictly greater than).
        var context = MakeContext(TimeSpan.FromHours(4));

        var met = await trigger.IsMetAsync(context);

        Assert.False(met);
    }

    [Fact]
    public void IdleTrigger_HasCorrectName()
    {
        var trigger = new IdleTrigger();
        Assert.Equal("idle", trigger.Name);
    }

    [Fact]
    public void IdleTrigger_DefaultThresholdIs4Hours()
    {
        var trigger = new IdleTrigger();
        Assert.Equal(TimeSpan.FromHours(4), trigger.IdleThreshold);
    }
}

// ---------------------------------------------------------------------------
// ScheduleTrigger tests
// ---------------------------------------------------------------------------

public sealed class ScheduleTriggerTests
{
    /// <summary>
    /// Returns a ProactiveContext where NowUtc converts to the given local time.
    /// We use DateTime.SpecifyKind(Local) so LocalDateTime returns the expected value.
    /// </summary>
    private static ProactiveContext ContextAt(DateTime localTime)
    {
        // Build a DateTimeOffset whose LocalDateTime equals localTime.
        var dto = new DateTimeOffset(
            DateTime.SpecifyKind(localTime, DateTimeKind.Local));

        return new ProactiveContext(
            UserId:                   "u1",
            NowUtc:                   dto.ToUniversalTime(),
            TimeSinceLastInteraction: TimeSpan.Zero,
            AffectState:              null,
            ActiveGoals:              Array.Empty<Goal>());
    }

    [Fact]
    public async Task ScheduleTrigger_FiresInsideWindow()
    {
        var now = DateTime.Now;
        var triggerTime = TimeOnly.FromDateTime(now.AddSeconds(30)); // 30 s into the window
        var trigger = new ScheduleTrigger(triggerTime, "morning");

        var ctx = ContextAt(now.AddSeconds(30));
        var met = await trigger.IsMetAsync(ctx);

        Assert.True(met);
    }

    [Fact]
    public async Task ScheduleTrigger_DoesNotFireBeforeWindow()
    {
        var now = DateTime.Now;
        // trigger is 1 hour in the future
        var triggerTime = TimeOnly.FromDateTime(now.AddHours(1));
        var trigger = new ScheduleTrigger(triggerTime, "morning");

        var ctx = ContextAt(now);
        var met = await trigger.IsMetAsync(ctx);

        Assert.False(met);
    }

    [Fact]
    public async Task ScheduleTrigger_DoesNotFireAfterWindow()
    {
        var now = DateTime.Now;
        // trigger was 10 minutes ago (past the 5-minute window)
        var triggerTime = TimeOnly.FromDateTime(now.AddMinutes(-10));
        var trigger = new ScheduleTrigger(triggerTime, "morning");

        var ctx = ContextAt(now);
        var met = await trigger.IsMetAsync(ctx);

        Assert.False(met);
    }

    [Fact]
    public async Task ScheduleTrigger_FiresOncePerDay()
    {
        var now = DateTime.Now;
        var triggerTime = TimeOnly.FromDateTime(now.AddSeconds(30));
        var trigger = new ScheduleTrigger(triggerTime, "once");

        var ctx = ContextAt(now.AddSeconds(30));

        var first  = await trigger.IsMetAsync(ctx);
        var second = await trigger.IsMetAsync(ctx); // same calendar day

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void ScheduleTrigger_HasCorrectName()
    {
        var trigger = new ScheduleTrigger(new TimeOnly(9, 0), "my-trigger");
        Assert.Equal("my-trigger", trigger.Name);
    }

    [Fact]
    public void ScheduleTrigger_DefaultNameIsSchedule()
    {
        var trigger = new ScheduleTrigger(new TimeOnly(9, 0));
        Assert.Equal("schedule", trigger.Name);
    }
}

// ---------------------------------------------------------------------------
// ProactiveReasoningService tests
// ---------------------------------------------------------------------------

public sealed class ProactiveReasoningServiceTests
{
    /// <summary>Always-true trigger for testing event flow.</summary>
    private sealed class AlwaysTrigger : ITriggerCondition
    {
        public string Name => "always";
        public ValueTask<bool> IsMetAsync(ProactiveContext context, CancellationToken ct = default)
            => new ValueTask<bool>(true);
    }

    /// <summary>Always-false trigger for testing no-fire path.</summary>
    private sealed class NeverTrigger : ITriggerCondition
    {
        public string Name => "never";
        public ValueTask<bool> IsMetAsync(ProactiveContext context, CancellationToken ct = default)
            => new ValueTask<bool>(false);
    }

    [Fact]
    public async Task RaisesProactiveMessageReady_WhenTriggerFires()
    {
        var butler   = new FakeAIService("Hey, how are you?");
        var service  = new ProactiveReasoningService(
            butler, goalStore: null, affectStore: null,
            triggers: new[] { (ITriggerCondition)new AlwaysTrigger() });

        ProactiveMessageEventArgs? captured = null;
        service.ProactiveMessageReady += (_, args) => captured = args;

        await service.CheckAsync("user1");

        Assert.NotNull(captured);
        Assert.Equal("user1",   captured!.UserId);
        Assert.Equal("Hey, how are you?", captured.Message);
        Assert.Equal("always",  captured.TriggerName);
        Assert.Equal(1, butler.AskCallCount);
    }

    [Fact]
    public async Task DoesNotRaiseEvent_WhenNoTriggersFire()
    {
        var butler  = new FakeAIService("Hello!");
        var service = new ProactiveReasoningService(
            butler, goalStore: null, affectStore: null,
            triggers: new[] { (ITriggerCondition)new NeverTrigger() });

        bool raised = false;
        service.ProactiveMessageReady += (_, _) => raised = true;

        await service.CheckAsync("user1");

        Assert.False(raised);
        Assert.Equal(0, butler.AskCallCount);
    }

    [Fact]
    public async Task DoesNotRaiseEvent_WhenTriggerListIsEmpty()
    {
        var butler  = new FakeAIService("Hello!");
        var service = new ProactiveReasoningService(
            butler, goalStore: null, affectStore: null,
            triggers: Array.Empty<ITriggerCondition>());

        bool raised = false;
        service.ProactiveMessageReady += (_, _) => raised = true;

        await service.CheckAsync("user1");

        Assert.False(raised);
    }

    [Fact]
    public async Task OnlyFirstTriggerFires_WhenMultipleAreMet()
    {
        var butler = new FakeAIService("Hello!");

        // Two always-fire triggers — only the first should cause a call.
        var t1 = new AlwaysTrigger();
        var t2 = new AlwaysTrigger();
        var service = new ProactiveReasoningService(
            butler, goalStore: null, affectStore: null,
            triggers: new ITriggerCondition[] { t1, t2 });

        var triggerNames = new List<string>();
        service.ProactiveMessageReady += (_, args) => triggerNames.Add(args.TriggerName);

        await service.CheckAsync("user1");

        // Only one event, one butler call.
        Assert.Single(triggerNames);
        Assert.Equal(1, butler.AskCallCount);
    }

    [Fact]
    public async Task IncludesActiveGoals_InPrompt()
    {
        var butler   = new FakeAIService("Checking in!");
        var goalStore = new InMemoryGoalStore();
        await goalStore.UpsertAsync(new Goal(
            Id: "g1", UserId: "user2", Title: "Write book", Description: "Finish the novel",
            Status: GoalStatus.Active, Priority: GoalPriority.High,
            CreatedUtc: DateTimeOffset.UtcNow));

        var service = new ProactiveReasoningService(
            butler, goalStore, affectStore: null,
            triggers: new[] { (ITriggerCondition)new AlwaysTrigger() });

        service.ProactiveMessageReady += (_, _) => { };

        await service.CheckAsync("user2");

        // The prompt should mention the goal title.
        Assert.NotNull(butler.LastPrompt);
        Assert.Contains("Write book", butler.LastPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EventArgs_ContainCorrectGeneratedUtc()
    {
        var before  = DateTimeOffset.UtcNow;
        var butler  = new FakeAIService("Hi!");
        var service = new ProactiveReasoningService(
            butler, goalStore: null, affectStore: null,
            triggers: new[] { (ITriggerCondition)new AlwaysTrigger() });

        ProactiveMessageEventArgs? captured = null;
        service.ProactiveMessageReady += (_, args) => captured = args;

        await service.CheckAsync("u3");
        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(captured);
        Assert.True(captured!.GeneratedUtc >= before);
        Assert.True(captured.GeneratedUtc  <= after);
    }
}
