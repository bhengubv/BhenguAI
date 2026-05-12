// IProactiveReasoningService.cs
//
// Contract for the proactive reasoning engine — B!'s ability to initiate
// contact rather than merely respond.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Hosting;

/// <summary>
/// Evaluates trigger conditions and, when any fires, generates a proactive
/// check-in message unprompted by the user.
/// </summary>
public interface IProactiveReasoningService
{
    /// <summary>
    /// Evaluates all trigger conditions and, when any fires, generates a
    /// proactive message and raises <see cref="ProactiveMessageReady"/>.
    /// </summary>
    /// <param name="userId">The user to evaluate triggers for.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CheckAsync(string userId, CancellationToken ct = default);

    /// <summary>Raised when B! has something to say unprompted.</summary>
    event EventHandler<ProactiveMessageEventArgs>? ProactiveMessageReady;
}

/// <summary>
/// Event arguments emitted when B! generates a proactive message.
/// </summary>
/// <param name="UserId">User this message targets.</param>
/// <param name="Message">The generated check-in message.</param>
/// <param name="TriggerName">Name of the trigger condition that fired.</param>
/// <param name="GeneratedUtc">When the message was generated (UTC).</param>
public sealed record ProactiveMessageEventArgs(
    string UserId,
    string Message,
    string TriggerName,
    DateTimeOffset GeneratedUtc);
