using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Hosting;
using Circle.AI.Inference;
using Circle.AI.Tools;
using Xunit;

namespace Circle.AI.Tests;

// ============================================================================
// AetherAIObserver (Gap 8)
// ============================================================================

public sealed class AetherAIObserverTests
{
    private static readonly IReadOnlyList<ChatMessage> SampleMessages =
        new[] { new ChatMessage("user", "hello") };

    private sealed class FakeTransport : ICircleAetherTransport
    {
        public List<(string Topic, byte[] Payload)> Published { get; } = new();

        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
        {
            Published.Add((topic, payload.ToArray()));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Constructor_NullTransport_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AetherAIObserver(null!));
    }

    [Fact]
    public async Task OnChatCompletedAsync_PublishesToButlerResponseTopic()
    {
        var transport = new FakeTransport();
        var observer = new AetherAIObserver(transport);
        var chatEvent = new AIChatEvent(
            Guid.NewGuid(), SampleMessages, "Hello world", TimeSpan.Zero, DateTimeOffset.UtcNow);

        await observer.OnChatCompletedAsync(chatEvent);

        // Allow fire-and-forget to complete
        await Task.Delay(50);

        Assert.Contains(transport.Published, p => p.Topic == "butler/response");
    }

    [Fact]
    public async Task OnChatCompletedAsync_PayloadContainsResponse()
    {
        var transport = new FakeTransport();
        var observer = new AetherAIObserver(transport);
        var chatEvent = new AIChatEvent(
            Guid.NewGuid(), SampleMessages, "The answer", TimeSpan.Zero, DateTimeOffset.UtcNow);

        await observer.OnChatCompletedAsync(chatEvent);
        await Task.Delay(50);

        var entry = Assert.Single(transport.Published);
        var doc = JsonDocument.Parse(entry.Payload);
        Assert.Equal("The answer", doc.RootElement.GetProperty("response").GetString());
    }

    [Fact]
    public async Task OnError_PublishesToButlerErrorTopic()
    {
        var transport = new FakeTransport();
        var observer = new AetherAIObserver(transport);

        observer.OnError(new InvalidOperationException("oops"));

        await Task.Delay(50);

        Assert.Contains(transport.Published, p => p.Topic == "butler/error");
    }

    [Fact]
    public async Task OnError_PayloadContainsErrorDetails()
    {
        var transport = new FakeTransport();
        var observer = new AetherAIObserver(transport);

        observer.OnError(new InvalidOperationException("bad state"));

        await Task.Delay(50);

        var entry = Assert.Single(transport.Published);
        var doc = JsonDocument.Parse(entry.Payload);
        Assert.Equal("InvalidOperationException", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("bad state", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void OnError_NullException_Throws()
    {
        var transport = new FakeTransport();
        var observer = new AetherAIObserver(transport);
        Assert.Throws<ArgumentNullException>(() => observer.OnError(null!));
    }

    [Fact]
    public async Task OnStartedAsync_DoesNotPublish()
    {
        var transport = new FakeTransport();
        var observer = new AetherAIObserver(transport);

        await observer.OnStartedAsync();
        await Task.Delay(50);

        Assert.Empty(transport.Published);
    }

    [Fact]
    public async Task OnStoppedAsync_DoesNotPublish()
    {
        var transport = new FakeTransport();
        var observer = new AetherAIObserver(transport);

        await observer.OnStoppedAsync();
        await Task.Delay(50);

        Assert.Empty(transport.Published);
    }

    [Fact]
    public async Task OnStreamStartedAsync_DoesNotPublish()
    {
        var transport = new FakeTransport();
        var observer = new AetherAIObserver(transport);
        var streamEvent = new AIStreamEvent(
            Guid.NewGuid(), SampleMessages, TimeSpan.Zero, 0, DateTimeOffset.UtcNow);

        await observer.OnStreamStartedAsync(streamEvent);
        await Task.Delay(50);

        Assert.Empty(transport.Published);
    }

    [Fact]
    public async Task OnStreamCompletedAsync_DoesNotPublish()
    {
        var transport = new FakeTransport();
        var observer = new AetherAIObserver(transport);
        var streamEvent = new AIStreamEvent(
            Guid.NewGuid(), SampleMessages, TimeSpan.FromSeconds(1), 10, DateTimeOffset.UtcNow);

        await observer.OnStreamCompletedAsync(streamEvent);
        await Task.Delay(50);

        Assert.Empty(transport.Published);
    }
}

// ============================================================================
// PushAIObserver (Gap 9)
// ============================================================================

public sealed class PushAIObserverTests
{
    private static readonly IReadOnlyList<ChatMessage> SampleMessages =
        new[] { new ChatMessage("user", "push me") };

    private sealed class FakeSender : IPushNotificationSender
    {
        public List<(string Token, string Title, string Body)> Sent { get; } = new();

        public Task SendAsync(string deviceToken, string title, string body, CancellationToken ct = default)
        {
            Sent.Add((deviceToken, title, body));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Constructor_NullSender_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PushAIObserver(null!, "tok"));
    }

    [Fact]
    public void Constructor_EmptyDeviceToken_Throws()
    {
        var sender = new FakeSender();
        Assert.Throws<ArgumentException>(() => new PushAIObserver(sender, ""));
    }

    [Fact]
    public void Constructor_WhitespaceDeviceToken_Throws()
    {
        var sender = new FakeSender();
        Assert.Throws<ArgumentException>(() => new PushAIObserver(sender, "   "));
    }

    [Fact]
    public async Task OnChatCompletedAsync_Short_SendsFullBody()
    {
        var sender = new FakeSender();
        var observer = new PushAIObserver(sender, "device-abc");
        var chatEvent = new AIChatEvent(
            Guid.NewGuid(), SampleMessages, "Hi there", TimeSpan.Zero, DateTimeOffset.UtcNow);

        await observer.OnChatCompletedAsync(chatEvent);
        await Task.Delay(50);

        var entry = Assert.Single(sender.Sent);
        Assert.Equal("Hi there", entry.Body);
        Assert.Equal("B!", entry.Title);
        Assert.Equal("device-abc", entry.Token);
    }

    [Fact]
    public async Task OnChatCompletedAsync_Long_TruncatesBody()
    {
        var sender = new FakeSender();
        var observer = new PushAIObserver(sender, "device-abc");
        var longText = new string('x', 200);
        var chatEvent = new AIChatEvent(
            Guid.NewGuid(), SampleMessages, longText, TimeSpan.Zero, DateTimeOffset.UtcNow);

        await observer.OnChatCompletedAsync(chatEvent);
        await Task.Delay(50);

        var entry = Assert.Single(sender.Sent);
        // 100 chars + 1 ellipsis character = 101 max
        Assert.True(entry.Body.Length <= 101);
        Assert.EndsWith("…", entry.Body);
    }

    [Fact]
    public async Task OnError_SendsErrorPush()
    {
        var sender = new FakeSender();
        var observer = new PushAIObserver(sender, "device-abc");

        observer.OnError(new InvalidOperationException("something failed"));
        await Task.Delay(50);

        var entry = Assert.Single(sender.Sent);
        Assert.Equal("B! Error", entry.Title);
        Assert.Equal("something failed", entry.Body);
    }

    [Fact]
    public async Task OnError_LongMessage_Truncates()
    {
        var sender = new FakeSender();
        var observer = new PushAIObserver(sender, "device-xyz");
        var longMsg = new string('e', 200);

        observer.OnError(new Exception(longMsg));
        await Task.Delay(50);

        var entry = Assert.Single(sender.Sent);
        Assert.True(entry.Body.Length <= 101);
        Assert.EndsWith("…", entry.Body);
    }

    [Fact]
    public void OnError_NullException_Throws()
    {
        var sender = new FakeSender();
        var observer = new PushAIObserver(sender, "device-abc");
        Assert.Throws<ArgumentNullException>(() => observer.OnError(null!));
    }

    [Fact]
    public async Task OnStartedAsync_DoesNotSendPush()
    {
        var sender = new FakeSender();
        var observer = new PushAIObserver(sender, "device-abc");

        await observer.OnStartedAsync();
        await Task.Delay(50);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task OnStoppedAsync_DoesNotSendPush()
    {
        var sender = new FakeSender();
        var observer = new PushAIObserver(sender, "device-abc");

        await observer.OnStoppedAsync();
        await Task.Delay(50);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task OnStreamStartedAsync_DoesNotSendPush()
    {
        var sender = new FakeSender();
        var observer = new PushAIObserver(sender, "device-abc");
        var streamEvent = new AIStreamEvent(
            Guid.NewGuid(), SampleMessages, TimeSpan.Zero, 0, DateTimeOffset.UtcNow);

        await observer.OnStreamStartedAsync(streamEvent);
        await Task.Delay(50);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task OnStreamCompletedAsync_DoesNotSendPush()
    {
        var sender = new FakeSender();
        var observer = new PushAIObserver(sender, "device-abc");
        var streamEvent = new AIStreamEvent(
            Guid.NewGuid(), SampleMessages, TimeSpan.FromSeconds(2), 15, DateTimeOffset.UtcNow);

        await observer.OnStreamCompletedAsync(streamEvent);
        await Task.Delay(50);

        Assert.Empty(sender.Sent);
    }
}
