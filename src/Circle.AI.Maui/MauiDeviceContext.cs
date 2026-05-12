// MauiDeviceContext.cs
//
// Full MAUI implementation of IDeviceContext.  Uses real platform APIs
// (Battery, Connectivity, DeviceInfo) that are only available in a .NET MAUI
// host — Android, iOS, macOS, or Windows.
//
// GPS (Latitude / Longitude / LocationHint) intentionally returns null.
// Fine-grained location requires an explicit runtime permission request that
// must be initiated by the host app, not a library.  Host apps that need GPS
// context should subclass or wrap MauiDeviceContext and override those three
// members after obtaining permission.
//
// Register via DI in MauiProgram.cs:
//
//   builder.Services.AddSingleton<IDeviceContext>(
//       sp => new MauiDeviceContext("tgn.butler"));
//
//   builder.Services.Configure<AIOptions>(o =>
//       o.DeviceContext = sp.GetRequiredService<IDeviceContext>());

using System;
using System.Globalization;
using Circle.AI.Core;
#if ANDROID || IOS || MACCATALYST || WINDOWS
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
#endif

namespace Circle.AI.Maui
{
    /// <summary>
    /// <see cref="IDeviceContext"/> backed by real MAUI platform APIs.
    /// Provides battery level, charging state, and network type without
    /// requiring GPS permissions.
    /// </summary>
    public sealed class MauiDeviceContext : IDeviceContext
    {
        // ------------------------------------------------------------------
        // Private state
        // ------------------------------------------------------------------

        private DateTimeOffset? _lastActiveUtc;

        // ------------------------------------------------------------------
        // Identity / locale
        // ------------------------------------------------------------------

        /// <inheritdoc />
        /// <remarks>
        /// Supplied by the host app at construction (e.g. <c>"tgn.sdpkt"</c>).
        /// </remarks>
        public string? ActiveAppId { get; }

        /// <inheritdoc />
        /// <remarks>Derived from <see cref="CultureInfo.CurrentUICulture"/>.</remarks>
        public string? Locale => CultureInfo.CurrentUICulture.Name;

        /// <inheritdoc />
        /// <remarks>Derived from <see cref="TimeZoneInfo.Local"/>.</remarks>
        public string? TimeZoneId => TimeZoneInfo.Local.Id;

        /// <inheritdoc />
        public DateTimeOffset? LocalTime => DateTimeOffset.Now;

        // ------------------------------------------------------------------
        // Location — not requested here; host app must handle permissions.
        // ------------------------------------------------------------------

        /// <inheritdoc />
        /// <remarks>
        /// Always <c>null</c> — GPS requires an explicit permission request
        /// that must be driven by the host app, not a library.
        /// Override in a subclass after obtaining permission.
        /// </remarks>
        public double? Latitude => null;

        /// <inheritdoc />
        public double? Longitude => null;

        /// <inheritdoc />
        public string? LocationHint => null;

        // ------------------------------------------------------------------
        // Device health — read from MAUI essentials.
        // ------------------------------------------------------------------

        /// <inheritdoc />
        /// <remarks>
        /// <see cref="IBattery.ChargeLevel"/> returns a <c>double</c> in the
        /// range [0.0, 1.0]; we narrow to <c>float</c> here.  Returns
        /// <c>null</c> on simulators or when the platform throws.
        /// </remarks>
        public float? BatteryLevel
        {
            get
            {
#if ANDROID || IOS || MACCATALYST || WINDOWS
                try
                {
                    var level = Battery.Default.ChargeLevel;
                    // ChargeLevel returns -1 when the value is unknown.
                    return level < 0 ? null : (float)level;
                }
                catch
                {
                    return null;
                }
#else
                return null;
#endif
            }
        }

        /// <inheritdoc />
        public bool? IsCharging
        {
            get
            {
#if ANDROID || IOS || MACCATALYST || WINDOWS
                try
                {
                    return Battery.Default.State switch
                    {
                        BatteryState.Charging => true,
                        BatteryState.Full     => true,  // plugged in and full
                        BatteryState.NotCharging
                            or BatteryState.Discharging => false,
                        _                               => null,
                    };
                }
                catch
                {
                    return null;
                }
#else
                return null;
#endif
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Returns one of <c>"wifi"</c>, <c>"cellular"</c>, <c>"ethernet"</c>,
        /// <c>"bluetooth"</c>, <c>"none"</c>, <c>"constrained"</c>, or
        /// <c>"local"</c>.
        /// </remarks>
        public string? NetworkType
        {
            get
            {
#if ANDROID || IOS || MACCATALYST || WINDOWS
                try
                {
                    var access = Connectivity.Current.NetworkAccess;

                    if (access == NetworkAccess.None)                return "none";
                    if (access == NetworkAccess.Local)               return "local";
                    if (access == NetworkAccess.ConstrainedInternet) return "constrained";
                    if (access == NetworkAccess.Unknown)             return null;

                    // Internet — inspect the active connection profiles.
                    var profiles = Connectivity.Current.ConnectionProfiles;

                    foreach (var profile in profiles)
                    {
                        if (profile == ConnectionProfile.WiFi)      return "wifi";
                        if (profile == ConnectionProfile.Cellular)  return "cellular";
                        if (profile == ConnectionProfile.Ethernet)  return "ethernet";
                        if (profile == ConnectionProfile.Bluetooth) return "bluetooth";
                    }

                    return "internet"; // Connected but profile is unrecognised.
                }
                catch
                {
                    return null;
                }
#else
                return null;
#endif
            }
        }

        // ------------------------------------------------------------------
        // User signals
        // ------------------------------------------------------------------

        /// <inheritdoc />
        public DateTimeOffset? LastActiveUtc => _lastActiveUtc;

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        /// <summary>
        /// Initialises the context for the given app identifier.
        /// </summary>
        /// <param name="activeAppId">
        /// The package / bundle ID of the host app, e.g. <c>"tgn.butler"</c>.
        /// </param>
        public MauiDeviceContext(string? activeAppId = null)
        {
            ActiveAppId   = activeAppId;
            _lastActiveUtc = DateTimeOffset.UtcNow;
        }

        // ------------------------------------------------------------------
        // Interaction tracking
        // ------------------------------------------------------------------

        /// <summary>
        /// Records a user interaction by updating <see cref="LastActiveUtc"/>
        /// to the current UTC time.  Call this from the MAUI Shell's
        /// <c>Navigated</c> event or any significant user action.
        /// </summary>
        public void RecordInteraction() =>
            _lastActiveUtc = DateTimeOffset.UtcNow;
    }
}
