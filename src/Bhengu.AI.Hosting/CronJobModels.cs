// CronJobModels.cs
//
// Domain models for B! scheduled tasks (Track 3).
// These types are intentionally free of any external dependencies.

namespace Bhengu.AI.Hosting;

/// <summary>Delivery channel for a scheduled job's output.</summary>
public enum DeliveryTarget
{
    /// <summary>Deliver via in-process IAIObserver callback.</summary>
    Local,
    /// <summary>Deliver via push notification (requires IPushNotificationSender).</summary>
    Push,
    /// <summary>Deliver as a Telegram message (requires webhook config).</summary>
    Telegram,
    /// <summary>Deliver via email (requires SMTP config).</summary>
    Email,
    /// <summary>Caller handles delivery via custom callback.</summary>
    Custom
}

/// <summary>State of a scheduled job's last execution.</summary>
public enum CronJobState
{
    /// <summary>Job has never run.</summary>
    Pending,
    /// <summary>Job is currently executing.</summary>
    Running,
    /// <summary>Last run completed without error.</summary>
    Succeeded,
    /// <summary>Last run threw an exception or the model returned an error.</summary>
    Failed,
    /// <summary>Job has been manually paused and will not fire until re-enabled.</summary>
    Paused
}

/// <summary>A named, recurring B! task with a cron schedule.</summary>
/// <param name="Id">Unique job identifier.</param>
/// <param name="Name">Human-readable name.</param>
/// <param name="Prompt">The prompt B! will process on schedule.</param>
/// <param name="CronExpression">Cron expression (5-field: min hour dom month dow).</param>
/// <param name="Delivery">Where to deliver the AI response.</param>
/// <param name="LastRunUtc">UTC time of last run. Null = never run.</param>
/// <param name="NextRunUtc">UTC time of next scheduled run.</param>
/// <param name="State">Current execution state.</param>
/// <param name="IsEnabled">Whether this job is active.</param>
public sealed record CronJob(
    string Id,
    string Name,
    string Prompt,
    string CronExpression,
    DeliveryTarget Delivery,
    DateTimeOffset? LastRunUtc = null,
    DateTimeOffset? NextRunUtc = null,
    CronJobState State = CronJobState.Pending,
    bool IsEnabled = true);
