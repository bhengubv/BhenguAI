// MauiPushSender.cs
//
// Local-notification implementation of IPushNotificationSender.
//
// This delivers *local* notifications (model finished responding) using
// platform-native APIs.  The deviceToken parameter is accepted for
// interface compliance and may be used for a future remote FCM/APN path,
// but for the "local" token value (or any null/empty value) it is ignored.
//
// Platform dispatch:
//   Android               — NotificationManager + NotificationChannel + Notification.Builder
//   iOS / macOS Catalyst  — UNUserNotificationCenter
//   Windows               — WinRT ToastNotificationManager
//   net9.0 / other        — Console.WriteLine fallback (headless / test environments)
//
// Registration (MauiProgram.cs):
//   builder.Services.AddSingleton<IPushNotificationSender, MauiPushSender>();

using System;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Hosting;

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
#endif

#if IOS || MACCATALYST
using UserNotifications;
#endif

#if WINDOWS
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
#endif

namespace Circle.AI.Maui;

/// <summary>
/// <see cref="IPushNotificationSender"/> that shows a local device
/// notification using the native platform API.  Intended for signalling
/// that B! has finished generating a response.
/// </summary>
public sealed class MauiPushSender : IPushNotificationSender
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

#if ANDROID
    private const string ChannelId      = "butler_responses";
    private const string ChannelName    = "B! Responses";
    private static int   _notifySeq;    // auto-incremented to avoid overwriting
#endif

#if WINDOWS
    private const string AppUserModelId = "co.za.thegeeknetwork.butler";
#endif

    // -----------------------------------------------------------------------
    // IPushNotificationSender
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The <paramref name="deviceToken"/> is used only when it is non-null
    /// and not equal to <c>"local"</c> — in that case the implementation
    /// reserves the value for a future remote-push (FCM/APN) code path.
    /// For all other values the notification is delivered locally.
    /// </para>
    /// <para>
    /// The method schedules the notification and returns immediately
    /// (fire-and-forget, matching <see cref="PushButlerObserver"/> usage).
    /// </para>
    /// </remarks>
    public Task SendAsync(
        string deviceToken,
        string title,
        string body,
        CancellationToken ct = default)
    {
        // Future remote path: if deviceToken is a real token, hand off to
        // FCM/APN SDK here.  For now always deliver locally.
        _ = SendLocalAsync(title, body);
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Platform implementations
    // -----------------------------------------------------------------------

    private static Task SendLocalAsync(string title, string body)
    {
#if ANDROID
        return Task.Run(() => SendAndroid(title, body));

#elif IOS || MACCATALYST
        return SendAppleAsync(title, body);

#elif WINDOWS
        return Task.Run(() => SendWindows(title, body));

#else
        // Headless / test environments — write to stdout.
        Console.WriteLine($"[MauiPushSender] {title}: {body}");
        return Task.CompletedTask;
#endif
    }

    // -----------------------------------------------------------------------
    // Android
    // -----------------------------------------------------------------------

#if ANDROID
    private static void SendAndroid(string title, string body)
    {
        var context = Android.App.Application.Context;
        var manager = NotificationManagerCompat.From(context);

        // Ensure the response channel exists.
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                ChannelName,
                NotificationImportance.Default);
            manager.CreateNotificationChannel(channel);
        }

        var notificationId = System.Threading.Interlocked.Increment(ref _notifySeq);

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetSmallIcon(Android.Resource.Drawable.StatNotifyMore)
            .SetAutoCancel(true)
            .Build()!;

        manager.Notify(notificationId, notification);
    }
#endif

    // -----------------------------------------------------------------------
    // iOS / macOS Catalyst
    // -----------------------------------------------------------------------

#if IOS || MACCATALYST
    private static Task SendAppleAsync(string title, string body)
    {
        var content = new UNMutableNotificationContent
        {
            Title = title,
            Body  = body,
            Sound = UNNotificationSound.Default
        };

        // No trigger = deliver immediately.
        var request = UNNotificationRequest.FromIdentifier(
            Guid.NewGuid().ToString("N"),
            content,
            trigger: null);

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        UNUserNotificationCenter.Current.AddNotificationRequest(
            request,
            error =>
            {
                // Non-fatal — just complete the TCS either way.
                if (error is not null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[MauiPushSender] UNUserNotificationCenter error: {error}");
                }
                tcs.TrySetResult(true);
            });

        return tcs.Task;
    }
#endif

    // -----------------------------------------------------------------------
    // Windows
    // -----------------------------------------------------------------------

#if WINDOWS
    private static void SendWindows(string title, string body)
    {
        try
        {
            // Build a simple toast XML using the "ToastText02" template which
            // has a bold title line and a body line.
            const string Template = @"<toast>
  <visual>
    <binding template=""ToastText02"">
      <text id=""1"">{0}</text>
      <text id=""2"">{1}</text>
    </binding>
  </visual>
</toast>";

            var xml = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                Template,
                EscapeXml(title),
                EscapeXml(body));

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var toast = new ToastNotification(doc);

            // ToastNotificationManager requires an AppUserModelId on desktop.
            // In a MAUI app the host sets this; fall back gracefully.
            var notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
            notifier.Show(toast);
        }
        catch (Exception ex)
        {
            // WinRT may not be available in all Windows environments
            // (e.g. server SKUs without notification support).
            System.Diagnostics.Debug.WriteLine(
                $"[MauiPushSender] Windows toast error: {ex.Message}");
        }
    }

    private static string EscapeXml(string value) =>
        value
            .Replace("&",  "&amp;",  StringComparison.Ordinal)
            .Replace("<",  "&lt;",   StringComparison.Ordinal)
            .Replace(">",  "&gt;",   StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'",  "&apos;", StringComparison.Ordinal);
#endif
}
