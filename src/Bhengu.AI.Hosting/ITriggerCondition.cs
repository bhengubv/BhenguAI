// ITriggerCondition.cs
//
// Proactive reasoning trigger conditions. Each condition evaluates a
// ProactiveContext snapshot and signals when B! should initiate a check-in.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Memory;

namespace Bhengu.AI.Hosting;

/// <summary>A condition that, when true, signals B! should check in proactively.</summary>
public interface ITriggerCondition
{
    /// <summary>Stable name used for logging and deduplication.</summary>
    string Name { get; }

    /// <summary>Returns true when the condition is currently met.</summary>
    ValueTask<bool> IsMetAsync(ProactiveContext context, CancellationToken ct = default);
}

/// <summary>Context snapshot passed to trigger conditions.</summary>
/// <param name="UserId">User being evaluated.</param>
/// <param name="NowUtc">Current UTC time.</param>
/// <param name="TimeSinceLastInteraction">How long since the user last interacted.</param>
/// <param name="AffectState">Current affect state (may be null if no store is configured).</param>
/// <param name="ActiveGoals">User's currently active goals.</param>
public sealed record ProactiveContext(
    string UserId,
    DateTimeOffset NowUtc,
    TimeSpan TimeSinceLastInteraction,
    AffectState? AffectState,
    IReadOnlyList<Goal> ActiveGoals);
