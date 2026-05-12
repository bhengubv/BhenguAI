// IdleTrigger.cs
//
// Fires when the user has been idle for longer than the configured threshold.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Hosting;

/// <summary>
/// Fires when <see cref="ProactiveContext.TimeSinceLastInteraction"/> exceeds
/// <see cref="IdleThreshold"/>. Useful for generating a warm check-in after
/// the user has been away for a while.
/// </summary>
public sealed class IdleTrigger : ITriggerCondition
{
    private readonly TimeSpan _idleThreshold;

    /// <summary>
    /// Constructs an <see cref="IdleTrigger"/>.
    /// </summary>
    /// <param name="idleThreshold">
    /// How long the user must be idle before the trigger fires.
    /// Defaults to 4 hours.
    /// </param>
    public IdleTrigger(TimeSpan? idleThreshold = null)
    {
        _idleThreshold = idleThreshold ?? TimeSpan.FromHours(4);
    }

    /// <summary>Idle threshold used by this trigger.</summary>
    public TimeSpan IdleThreshold => _idleThreshold;

    /// <inheritdoc />
    public string Name => "idle";

    /// <inheritdoc />
    public ValueTask<bool> IsMetAsync(ProactiveContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ValueTask<bool>(context.TimeSinceLastInteraction > _idleThreshold);
    }
}
