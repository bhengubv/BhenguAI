// PushAIObserver.cs
//
// Thin IAIObserver -> IPushNotificationSender bridge.
// The push sender interface is defined inline; replace with your APN/FCM
// SDK implementation when integrating real push infrastructure.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Platform-agnostic push notification sender abstraction.
/// Implement this with an APN or FCM SDK for real delivery.
/// </summary>
public interface IPushNotificationSender
{
    /// <summary>Send a push notification to the device identified by <paramref name="deviceToken"/>.</summary>
    Task SendAsync(
        string deviceToken,
        string title,
        string body,
        CancellationToken ct = default);
}

/// <summary>
/// <see cref="IAIObserver"/> that delivers butler responses as push
/// notifications via <see cref="IPushNotificationSender"/>.
/// </summary>
public sealed class PushAIObserver : IAIObserver
{
    private const int MaxBodyLength = 100;

    private readonly IPushNotificationSender _sender;
    private readonly string _deviceToken;

    /// <param name="sender">The push sender to use for delivery.</param>
    /// <param name="deviceToken">Target device token (APN device token or FCM registration ID).</param>
    public PushAIObserver(IPushNotificationSender sender, string deviceToken)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        if (string.IsNullOrWhiteSpace(deviceToken))
            throw new ArgumentException("Device token is required.", nameof(deviceToken));
        _deviceToken = deviceToken;
    }

    /// <inheritdoc />
    public ValueTask OnStartedAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnStoppedAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnChatCompletedAsync(AIChatEvent @event, CancellationToken ct = default)
    {
        SendResponse(@event.Response);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask OnStreamStartedAsync(AIStreamEvent @event, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnStreamCompletedAsync(AIStreamEvent @event, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnToolInvokedAsync(AIToolEvent @event, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    /// <summary>
    /// Sends an error push notification.
    /// Call this from error-handling code that cannot surface through
    /// the standard <see cref="IAIObserver"/> lifecycle.
    /// </summary>
    public void OnError(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var msg = ex.Message;
        var body = msg.Length > MaxBodyLength
            ? string.Concat(msg.AsSpan(0, MaxBodyLength), "…")
            : msg;
        _ = _sender.SendAsync(_deviceToken, "B! Error", body, CancellationToken.None);
    }

    private void SendResponse(string fullResponse)
    {
        var body = fullResponse.Length > MaxBodyLength
            ? string.Concat(fullResponse.AsSpan(0, MaxBodyLength), "…")
            : fullResponse;
        _ = _sender.SendAsync(_deviceToken, "B!", body, CancellationToken.None);
    }
}
