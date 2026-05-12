// IThermalThrottleService.cs
//
// Cross-platform thermal state monitor. Implementations poll OS-level
// temperature APIs on a background timer and surface a coarse five-level
// ThermalState so inference schedulers can back off before the kernel does.

using System;
using System.Threading;

namespace Circle.AI.Hosting;

/// <summary>
/// Coarse thermal state, ordered from coolest to hottest so numeric
/// comparisons (e.g. <c>&gt;= ThermalState.Serious</c>) are meaningful.
/// </summary>
public enum ThermalState
{
    /// <summary>State could not be determined (API unavailable or error).</summary>
    Unknown = 0,

    /// <summary>Device is within normal operating temperature.</summary>
    Normal = 1,

    /// <summary>Device is slightly warm; performance may be lightly throttled.</summary>
    Fair = 2,

    /// <summary>Device is hot; OS may have begun throttling CPU/GPU.</summary>
    Serious = 3,

    /// <summary>Device is critically hot; aggressive throttling or shutdown imminent.</summary>
    Critical = 4,
}

/// <summary>
/// Polls platform thermal APIs and exposes the current device temperature
/// state for inference schedulers that wish to pause on-device work when
/// the hardware is under thermal pressure.
/// </summary>
public interface IThermalThrottleService : IDisposable
{
    /// <summary>Most-recently sampled thermal state.</summary>
    ThermalState CurrentState { get; }

    /// <summary>
    /// <c>true</c> when <see cref="CurrentState"/> is
    /// <see cref="ThermalState.Serious"/> or <see cref="ThermalState.Critical"/>.
    /// Inference workers should pause when this returns <c>true</c>.
    /// </summary>
    bool ShouldPauseInference { get; }

    /// <summary>
    /// Raised on the thread-pool whenever <see cref="CurrentState"/> changes.
    /// The <see cref="EventArgs"/> value is the new state.
    /// </summary>
    event EventHandler<ThermalState>? StateChanged;

    /// <summary>
    /// Starts the background polling loop. Safe to call multiple times;
    /// subsequent calls while the loop is running are a no-op.
    /// </summary>
    /// <param name="ct">Token that stops monitoring when cancelled.</param>
    void StartMonitoring(CancellationToken ct = default);

    /// <summary>Stops the polling loop. The current state is retained.</summary>
    void StopMonitoring();
}
