// ScheduleTrigger.cs
//
// Fires at a specific time of day (once per calendar day).

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Fires at a specific time of day. The trigger is active for a 5-minute window
/// starting at <see cref="TriggerTime"/> and fires at most once per calendar day.
/// </summary>
public sealed class ScheduleTrigger : ITriggerCondition
{
    private readonly TimeOnly _triggerTime;
    private DateOnly? _lastFireDate;

    /// <summary>
    /// Constructs a <see cref="ScheduleTrigger"/>.
    /// </summary>
    /// <param name="triggerTime">Local time of day at which the trigger fires.</param>
    /// <param name="name">Optional stable name for this trigger. Defaults to <c>"schedule"</c>.</param>
    public ScheduleTrigger(TimeOnly triggerTime, string name = "schedule")
    {
        _triggerTime = triggerTime;
        Name = name;
    }

    /// <summary>Time of day at which this trigger fires.</summary>
    public TimeOnly TriggerTime => _triggerTime;

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public ValueTask<bool> IsMetAsync(ProactiveContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Convert NowUtc to local time for comparison.
        var localNow = context.NowUtc.LocalDateTime;
        var localDate = DateOnly.FromDateTime(localNow);
        var localTime = TimeOnly.FromDateTime(localNow);

        // Already fired today — don't fire again.
        if (_lastFireDate.HasValue && _lastFireDate.Value == localDate)
            return new ValueTask<bool>(false);

        // Check whether we are within the 5-minute window after triggerTime.
        var windowStart = _triggerTime;
        var windowEnd   = _triggerTime.AddMinutes(5);

        bool inWindow;
        if (windowEnd >= windowStart)
        {
            // Normal case — window doesn't wrap midnight.
            inWindow = localTime >= windowStart && localTime < windowEnd;
        }
        else
        {
            // Window wraps midnight (e.g. 23:58 + 5 min = 00:03).
            inWindow = localTime >= windowStart || localTime < windowEnd;
        }

        if (!inWindow) return new ValueTask<bool>(false);

        // We are in the window — mark as fired for today and return true.
        _lastFireDate = localDate;
        return new ValueTask<bool>(true);
    }
}
