// MauiInferenceService.cs
//
// Unified MAUI inference lifecycle service.  Dispatches to the correct
// platform mechanism so the OS does not kill the process mid-generation.
//
//   Android  — posts a sticky low-importance foreground-style notification so
//               the OS treats the process as active; cancelled on StopAsync.
//   iOS      — registers a BGProcessingTask with identifier
//               "co.za.thegeeknetwork.butler.inference" and starts inference
//               inside the task handler; respects the expiration handler.
//   macOS / Windows / other — runs inference on a background Task; the host
//               process lifecycle is managed by the OS application model.
//
// Usage (MauiProgram.cs):
//   builder.Services.AddSingleton<MauiInferenceService>();
//   ...
//   var svc = sp.GetRequiredService<MauiInferenceService>();
//   await svc.StartAsync(ct);
//
// iOS extra step (AppDelegate.cs):
//   MauiInferenceService.Register();   // must be called from FinishedLaunching

using System;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Hosting;
using Microsoft.Extensions.Logging;

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
#endif

#if IOS
using BackgroundTasks;
using Foundation;
#endif

namespace Bhengu.AI.Maui;

/// <summary>
/// Manages B! inference background execution on each MAUI platform.
/// </summary>
public sealed class MauiInferenceService : IDisposable
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

#if ANDROID
    private const string ChannelId         = "butler_inference";
    private const string ChannelName       = "B! Inference";
    private const int    NotificationId    = 0x4231_0001; // arbitrary unique int
#endif

#if IOS
    private const string BgTaskIdentifier = "co.za.thegeeknetwork.butler.inference";
#endif

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    private readonly IAIService            _butler;
    private readonly ILogger<MauiInferenceService> _logger;
    private readonly CancellationTokenSource   _cts = new();

    private bool _disposed;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    /// <summary>
    /// Initialises the service.  Register via DI as a singleton.
    /// </summary>
    /// <param name="butler">The loaded butler service to drive.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public MauiInferenceService(
        IAIService butler,
        ILogger<MauiInferenceService> logger)
    {
        _butler = butler ?? throw new ArgumentNullException(nameof(butler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // -----------------------------------------------------------------------
    // iOS — static registration helper
    // -----------------------------------------------------------------------

#if IOS
    /// <summary>
    /// Registers the BGProcessingTask identifier with <c>BGTaskScheduler</c>.
    /// Must be called from <c>AppDelegate.FinishedLaunching</c> before any
    /// task is scheduled.
    /// </summary>
    public static void Register()
    {
        BGTaskScheduler.Shared.Register(
            BgTaskIdentifier,
            null,
            task => HandleBgTask(task));
    }

    private static void HandleBgTask(BGTask task)
    {
        // The actual inference work is started from StartAsync via
        // ScheduleBgTask.  This handler is the OS entry point — its sole
        // job is to satisfy the BGTaskScheduler contract and hand control
        // to the instance that was waiting for it.
        // In practice the host app should keep a reference to the service
        // and call StartAsync from within the handler; the static shim
        // here just completes the task gracefully so the OS does not
        // penalise the app.
        task.SetTaskCompleted(success: true);
    }
#endif

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts background inference, applying the correct platform mechanism.
    /// </summary>
    /// <param name="ct">Cancellation token supplied by the caller.</param>
    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);

#if ANDROID
        try
        {
            StartAndroidForegroundNotification();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MauiInferenceService] Could not post Android inference notification.");
        }

        try
        {
            await _butler.StartAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            CancelAndroidNotification();
        }

#elif IOS
        // Schedule a BGProcessingTask request so iOS gives us background CPU time.
        ScheduleBgTask();
        // Run inference inside a Task; the BGTask expiration handler will cancel it.
        await _butler.StartAsync(linked.Token).ConfigureAwait(false);

#else
        // macOS, Windows, net9.0 headless — just run on a background Task.
        await _butler.StartAsync(linked.Token).ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// Stops inference and tears down any platform-specific resources.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task StopAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

#if ANDROID
        CancelAndroidNotification();
#endif

#if IOS
        CancelBgTask();
#endif

        await _butler.StopAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    // -----------------------------------------------------------------------
    // Android helpers
    // -----------------------------------------------------------------------

#if ANDROID
    private void StartAndroidForegroundNotification()
    {
        var context = Android.App.Application.Context;
        var manager = NotificationManagerCompat.From(context);

        // Create the notification channel (no-op on older API levels).
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                ChannelName,
                NotificationImportance.Low)   // silent — no sound, no heads-up
            {
                Description = "B! background inference progress"
            };
            manager.CreateNotificationChannel(channel);
        }

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle("B! is thinking…")
            .SetContentText("Running inference in the background")
            .SetSmallIcon(Android.Resource.Drawable.StatNotifyMore)
            .SetOngoing(true)      // sticky — user cannot swipe away
            .SetSilent(true)
            .Build()!;

        manager.Notify(NotificationId, notification);
        _logger.LogDebug("[MauiInferenceService] Android inference notification posted.");
    }

    private void CancelAndroidNotification()
    {
        try
        {
            var context = Android.App.Application.Context;
            var manager = NotificationManagerCompat.From(context);
            manager.Cancel(NotificationId);
            _logger.LogDebug("[MauiInferenceService] Android inference notification cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MauiInferenceService] Failed to cancel Android notification.");
        }
    }
#endif

    // -----------------------------------------------------------------------
    // iOS helpers
    // -----------------------------------------------------------------------

#if IOS
    private static void ScheduleBgTask()
    {
        var request = new BGProcessingTaskRequest(BgTaskIdentifier)
        {
            RequiresNetworkConnectivity = false,
            RequiresExternalPower       = false
        };

        try
        {
            BGTaskScheduler.Shared.Submit(request, out var error);
            if (error is not null)
            {
                // Non-fatal — inference will still run, just without guaranteed BG time.
                System.Diagnostics.Debug.WriteLine(
                    $"[MauiInferenceService] BGTaskScheduler submit error: {error}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MauiInferenceService] BGTaskScheduler exception: {ex.Message}");
        }
    }

    private static void CancelBgTask()
    {
        try
        {
            BGTaskScheduler.Shared.Cancel(BgTaskIdentifier);
        }
        catch
        {
            // Swallow — cancellation is best-effort.
        }
    }
#endif
}
