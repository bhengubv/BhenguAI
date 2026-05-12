// SystemInfoDeviceContext.cs
//
// Cross-platform IDeviceContext implementation using only System.*
// APIs — no MAUI, Android, or iOS SDK dependency.
//
// For a full MAUI implementation (battery, GPS, connectivity) register
// a platform-specific subclass via MauiDeviceContextAdapter in the
// .NET MAUI host project and pass it via AIOptions.DeviceContext.

using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Circle.AI.Core
{
    /// <summary>
    /// Cross-platform <see cref="IDeviceContext"/> that populates identity and
    /// locale from <see cref="RuntimeInformation"/> / <see cref="CultureInfo"/>.
    /// Platform-specific sensors (GPS, battery, connectivity) return <c>null</c>
    /// and must be supplied by a richer platform adapter.
    /// </summary>
    public sealed class SystemInfoDeviceContext : IDeviceContext
    {
        // ------------------------------------------------------------------
        // Identity / locale — populated from the runtime environment.
        // ------------------------------------------------------------------

        /// <inheritdoc />
        public string? ActiveAppId { get; }

        /// <inheritdoc />
        public string? Locale { get; } =
            CultureInfo.CurrentUICulture.Name; // e.g. "en-ZA"

        /// <inheritdoc />
        public string? TimeZoneId { get; } =
            TimeZoneInfo.Local.Id; // e.g. "Africa/Johannesburg" on Linux/macOS

        /// <inheritdoc />
        public DateTimeOffset? LocalTime => DateTimeOffset.Now;

        // ------------------------------------------------------------------
        // Location — unavailable without platform APIs.
        // ------------------------------------------------------------------

        /// <inheritdoc />
        public double? Latitude => null;

        /// <inheritdoc />
        public double? Longitude => null;

        /// <inheritdoc />
        public string? LocationHint => null;

        // ------------------------------------------------------------------
        // Device health — unavailable without platform APIs.
        // ------------------------------------------------------------------

        /// <inheritdoc />
        public float? BatteryLevel => null;

        /// <inheritdoc />
        public bool? IsCharging => null;

        /// <inheritdoc />
        public string? NetworkType => null;

        // ------------------------------------------------------------------
        // User signals
        // ------------------------------------------------------------------

        /// <inheritdoc />
        public DateTimeOffset? LastActiveUtc { get; private set; }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        /// <param name="activeAppId">
        /// The app identifier to report (e.g. <c>"tgn.butler"</c>).
        /// </param>
        public SystemInfoDeviceContext(string? activeAppId = null)
        {
            ActiveAppId = activeAppId;
            LastActiveUtc = DateTimeOffset.UtcNow;
        }

        /// <summary>Update the last-active timestamp to now.</summary>
        public void RecordInteraction() => LastActiveUtc = DateTimeOffset.UtcNow;
    }
}
