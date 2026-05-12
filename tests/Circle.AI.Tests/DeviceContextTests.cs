using System;
using Circle.AI.Core;
using Xunit;

namespace Circle.AI.Tests;

public sealed class NullDeviceContextTests
{
    [Fact]
    public void Instance_IsSingleton()
    {
        Assert.Same(NullDeviceContext.Instance, NullDeviceContext.Instance);
    }

    [Fact]
    public void AllProperties_ReturnNull()
    {
        var ctx = NullDeviceContext.Instance;

        Assert.Null(ctx.ActiveAppId);
        Assert.Null(ctx.Locale);
        Assert.Null(ctx.TimeZoneId);
        Assert.Null(ctx.LocalTime);
        Assert.Null(ctx.Latitude);
        Assert.Null(ctx.Longitude);
        Assert.Null(ctx.LocationHint);
        Assert.Null(ctx.BatteryLevel);
        Assert.Null(ctx.IsCharging);
        Assert.Null(ctx.NetworkType);
        Assert.Null(ctx.LastActiveUtc);
    }

    [Fact]
    public void ImplementsIDeviceContext()
    {
        Assert.IsAssignableFrom<IDeviceContext>(NullDeviceContext.Instance);
    }
}

public sealed class FakeDeviceContextTests
{
    [Fact]
    public void Properties_CanBeSetAndRead()
    {
        var ctx = new FakeDeviceContext
        {
            ActiveAppId   = "tgn.bidbaas",
            Locale        = "en-ZA",
            TimeZoneId    = "Africa/Johannesburg",
            LocalTime     = new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.FromHours(2)),
            Latitude      = -26.2041,
            Longitude     = 28.0473,
            LocationHint  = "Johannesburg, South Africa",
            BatteryLevel  = 0.72f,
            IsCharging    = false,
            NetworkType   = "wifi",
            LastActiveUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        };

        Assert.Equal("tgn.bidbaas", ctx.ActiveAppId);
        Assert.Equal("en-ZA", ctx.Locale);
        Assert.Equal("Africa/Johannesburg", ctx.TimeZoneId);
        Assert.Equal(-26.2041, ctx.Latitude);
        Assert.Equal(28.0473, ctx.Longitude);
        Assert.Equal("Johannesburg, South Africa", ctx.LocationHint);
        Assert.Equal(0.72f, ctx.BatteryLevel);
        Assert.False(ctx.IsCharging);
        Assert.Equal("wifi", ctx.NetworkType);
        Assert.NotNull(ctx.LastActiveUtc);
    }
}

// Gap 4a — MauiDeviceContext (headless net9.0 TFM — MAUI sensors stubbed via #if)
public sealed class MauiDeviceContextTests
{
    [Fact]
    public void ImplementsIDeviceContext()
    {
        IDeviceContext ctx = new Circle.AI.Maui.MauiDeviceContext("tgn.butler");
        Assert.NotNull(ctx);
    }

    [Fact]
    public void ActiveAppId_MatchesConstructorArg()
    {
        var ctx = new Circle.AI.Maui.MauiDeviceContext("tgn.butler");
        Assert.Equal("tgn.butler", ctx.ActiveAppId);
    }

    [Fact]
    public void ActiveAppId_DefaultsToNull()
    {
        var ctx = new Circle.AI.Maui.MauiDeviceContext();
        Assert.Null(ctx.ActiveAppId);
    }

    [Fact]
    public void Locale_IsNonEmpty()
    {
        var ctx = new Circle.AI.Maui.MauiDeviceContext();
        Assert.False(string.IsNullOrWhiteSpace(ctx.Locale));
    }

    [Fact]
    public void TimeZoneId_IsNonEmpty()
    {
        var ctx = new Circle.AI.Maui.MauiDeviceContext();
        Assert.False(string.IsNullOrWhiteSpace(ctx.TimeZoneId));
    }

    [Fact]
    public void LocalTime_IsCloseToNow()
    {
        var ctx  = new Circle.AI.Maui.MauiDeviceContext();
        var diff = Math.Abs((ctx.LocalTime!.Value - DateTimeOffset.Now).TotalSeconds);
        Assert.True(diff < 5, $"LocalTime was {diff:F1}s away from now");
    }

    [Fact]
    public void GpsProperties_AlwaysNull()
    {
        var ctx = new Circle.AI.Maui.MauiDeviceContext();
        Assert.Null(ctx.Latitude);
        Assert.Null(ctx.Longitude);
        Assert.Null(ctx.LocationHint);
    }

    [Fact]
    public void PlatformSensors_ReturnNullWithoutMauiRuntime()
    {
        // Under the net9.0 headless TFM, Battery/Connectivity are #if-guarded
        // and return null — verifies no unguarded MAUI API calls exist.
        var ctx = new Circle.AI.Maui.MauiDeviceContext();
        Assert.Null(ctx.BatteryLevel);
        Assert.Null(ctx.IsCharging);
        Assert.Null(ctx.NetworkType);
    }

    [Fact]
    public void LastActiveUtc_SetOnConstruction()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var ctx    = new Circle.AI.Maui.MauiDeviceContext();
        var after  = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.True(ctx.LastActiveUtc >= before && ctx.LastActiveUtc <= after);
    }

    [Fact]
    public void RecordInteraction_UpdatesLastActiveUtc()
    {
        var ctx    = new Circle.AI.Maui.MauiDeviceContext();
        var before = ctx.LastActiveUtc;
        System.Threading.Thread.Sleep(10);
        ctx.RecordInteraction();
        Assert.True(ctx.LastActiveUtc >= before);
    }
}

// Gap 4b — SystemInfoDeviceContext (cross-platform, no MAUI SDK dependency)
public sealed class SystemInfoDeviceContextTests
{
    [Fact]
    public void ImplementsIDeviceContext()
    {
        IDeviceContext ctx = new SystemInfoDeviceContext("tgn.butler");
        Assert.NotNull(ctx);
    }

    [Fact]
    public void ActiveAppId_MatchesConstructorArg()
    {
        var ctx = new SystemInfoDeviceContext("tgn.butler");
        Assert.Equal("tgn.butler", ctx.ActiveAppId);
    }

    [Fact]
    public void ActiveAppId_DefaultsToNull()
    {
        var ctx = new SystemInfoDeviceContext();
        Assert.Null(ctx.ActiveAppId);
    }

    [Fact]
    public void Locale_IsNonEmpty()
    {
        var ctx = new SystemInfoDeviceContext();
        Assert.False(string.IsNullOrWhiteSpace(ctx.Locale));
    }

    [Fact]
    public void TimeZoneId_IsNonEmpty()
    {
        var ctx = new SystemInfoDeviceContext();
        Assert.False(string.IsNullOrWhiteSpace(ctx.TimeZoneId));
    }

    [Fact]
    public void LocalTime_IsCloseToNow()
    {
        var ctx  = new SystemInfoDeviceContext();
        var diff = Math.Abs((ctx.LocalTime!.Value - DateTimeOffset.Now).TotalSeconds);
        Assert.True(diff < 5, $"LocalTime was {diff:F1}s away from now");
    }

    [Fact]
    public void PlatformSensors_ReturnNull()
    {
        var ctx = new SystemInfoDeviceContext();
        Assert.Null(ctx.Latitude);
        Assert.Null(ctx.Longitude);
        Assert.Null(ctx.LocationHint);
        Assert.Null(ctx.BatteryLevel);
        Assert.Null(ctx.IsCharging);
        Assert.Null(ctx.NetworkType);
    }

    [Fact]
    public void LastActiveUtc_SetOnConstruction()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var ctx    = new SystemInfoDeviceContext();
        var after  = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.True(ctx.LastActiveUtc >= before && ctx.LastActiveUtc <= after);
    }

    [Fact]
    public void RecordInteraction_UpdatesLastActiveUtc()
    {
        var ctx    = new SystemInfoDeviceContext();
        var before = ctx.LastActiveUtc;
        System.Threading.Thread.Sleep(10);
        ctx.RecordInteraction();
        Assert.True(ctx.LastActiveUtc >= before);
    }
}
