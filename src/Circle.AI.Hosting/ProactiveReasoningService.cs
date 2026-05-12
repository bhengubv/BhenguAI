// ProactiveReasoningService.cs
//
// Evaluates trigger conditions and generates proactive check-in messages
// when any condition is met. Only one trigger fires per CheckAsync call
// (highest-priority first, i.e. list order).

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Circle.AI.Hosting;

/// <summary>
/// Default <see cref="IProactiveReasoningService"/> implementation.
/// Evaluates a prioritised list of <see cref="ITriggerCondition"/> instances
/// and calls <see cref="IAIService.AskAsync"/> to generate a warm,
/// goal-aware check-in message when any condition fires.
/// </summary>
public sealed class ProactiveReasoningService : IProactiveReasoningService
{
    private readonly IAIService _butler;
    private readonly IGoalStore? _goalStore;
    private readonly IAffectStore? _affectStore;
    private readonly IReadOnlyList<ITriggerCondition> _triggers;
    private readonly ILogger _logger;

    /// <summary>
    /// Constructs the proactive reasoning service.
    /// </summary>
    /// <param name="butler">
    /// The butler service used to generate the proactive message.
    /// </param>
    /// <param name="goalStore">
    /// Optional goal store. When provided, active goals are loaded and
    /// summarised in the prompt so B! can give contextually relevant nudges.
    /// </param>
    /// <param name="affectStore">
    /// Optional affect store. When provided, the current affect state is
    /// loaded and included in the context snapshot.
    /// </param>
    /// <param name="triggers">
    /// Ordered list of trigger conditions. Evaluated in order; the first
    /// condition that fires causes the check-in. Pass an empty list to
    /// disable all proactive messaging.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public ProactiveReasoningService(
        IAIService butler,
        IGoalStore? goalStore,
        IAffectStore? affectStore,
        IReadOnlyList<ITriggerCondition> triggers,
        ILogger? logger = null)
    {
        _butler      = butler ?? throw new ArgumentNullException(nameof(butler));
        _goalStore   = goalStore;
        _affectStore = affectStore;
        _triggers    = triggers ?? throw new ArgumentNullException(nameof(triggers));
        _logger      = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public event EventHandler<ProactiveMessageEventArgs>? ProactiveMessageReady;

    /// <inheritdoc />
    public async Task CheckAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (_triggers.Count == 0) return;

        // 1. Load affect state.
        AffectState? affect = null;
        if (_affectStore is not null)
        {
            try
            {
                affect = await _affectStore.LoadAsync(userId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ProactiveReasoningService: affect load failed; continuing.");
            }
        }

        // 2. Load active goals.
        IReadOnlyList<Goal> activeGoals = Array.Empty<Goal>();
        if (_goalStore is not null)
        {
            try
            {
                activeGoals = await _goalStore.GetActiveAsync(userId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ProactiveReasoningService: goal load failed; continuing.");
            }
        }

        // 3. Build context snapshot.
        var now = DateTimeOffset.UtcNow;
        var timeSinceLast = affect is not null
            ? now - affect.LastUpdatedUtc
            : TimeSpan.Zero;

        var context = new ProactiveContext(
            UserId:                    userId,
            NowUtc:                    now,
            TimeSinceLastInteraction:  timeSinceLast,
            AffectState:               affect,
            ActiveGoals:               activeGoals);

        // 4. Check triggers in order — fire only the first one.
        foreach (var trigger in _triggers)
        {
            bool met;
            try
            {
                met = await trigger.IsMetAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ProactiveReasoningService: trigger '{TriggerName}' threw; skipping.",
                    trigger.Name);
                continue;
            }

            if (!met) continue;

            // 5. Build a proactive prompt.
            var prompt = BuildProactivePrompt(userId, timeSinceLast, activeGoals);

            // 6. Generate the message.
            string message;
            try
            {
                message = await _butler.AskAsync(prompt, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ProactiveReasoningService: butler.AskAsync failed for trigger '{TriggerName}'.",
                    trigger.Name);
                return;
            }

            // 7. Raise the event.
            var args = new ProactiveMessageEventArgs(
                UserId:       userId,
                Message:      message,
                TriggerName:  trigger.Name,
                GeneratedUtc: DateTimeOffset.UtcNow);

            try
            {
                ProactiveMessageReady?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ProactiveReasoningService: event handler threw; non-fatal.");
            }

            // Only fire one trigger per call.
            return;
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string BuildProactivePrompt(
        string userId,
        TimeSpan timeSinceLastInteraction,
        IReadOnlyList<Goal> activeGoals)
    {
        var sb = new StringBuilder();
        sb.Append("You are B!. ");

        if (timeSinceLastInteraction.TotalMinutes > 5)
        {
            var hours = (int)timeSinceLastInteraction.TotalHours;
            var minutes = (int)(timeSinceLastInteraction.TotalMinutes % 60);
            if (hours > 0)
                sb.Append($"The user has been away for approximately {hours} hour{(hours == 1 ? "" : "s")}. ");
            else
                sb.Append($"The user has been away for approximately {minutes} minute{(minutes == 1 ? "" : "s")}. ");
        }

        if (activeGoals.Count > 0)
        {
            sb.Append($"They have {activeGoals.Count} active goal{(activeGoals.Count == 1 ? "" : "s")}: ");
            for (int i = 0; i < activeGoals.Count; i++)
            {
                sb.Append('"');
                sb.Append(activeGoals[i].Title);
                sb.Append('"');
                if (i < activeGoals.Count - 1) sb.Append(", ");
            }
            sb.Append(". ");
        }

        sb.Append("Generate a brief, friendly check-in message (1-2 sentences). ");
        sb.Append("Be warm, specific to their goals if you know them, and not intrusive.");

        return sb.ToString();
    }
}
