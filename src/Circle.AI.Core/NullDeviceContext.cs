// NullDeviceContext.cs
//
// Default no-op implementation of IDeviceContext. All members return null /
// defaults. Used when no platform adapter is wired up (tests, CLI, headless).

using System;

namespace Circle.AI.Core
{
    /// <summary>
    /// Safe-null implementation of <see cref="IDeviceContext"/>. All members
    /// return <c>null</c>. Used as the default when
    /// <c>AIOptions.DeviceContext</c> is not set.
    /// </summary>
    public sealed class NullDeviceContext : IDeviceContext
    {
        /// <summary>Shared singleton — stateless, safe to reuse.</summary>
        public static readonly NullDeviceContext Instance = new();

        private NullDeviceContext() { }

        public string? ActiveAppId    => null;
        public string? Locale         => null;
        public string? TimeZoneId     => null;
        public DateTimeOffset? LocalTime => null;
        public double? Latitude       => null;
        public double? Longitude      => null;
        public string? LocationHint   => null;
        public float? BatteryLevel    => null;
        public bool? IsCharging       => null;
        public string? NetworkType    => null;
        public DateTimeOffset? LastActiveUtc => null;
    }
}
