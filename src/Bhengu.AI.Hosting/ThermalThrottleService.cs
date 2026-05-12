// ThermalThrottleService.cs
//
// Cross-platform implementation of IThermalThrottleService. Each platform
// section is guarded by #if so the file compiles on every target without
// pulling platform-specific assemblies into the wrong TFM.
//
// Polling interval: 10 seconds via System.Threading.PeriodicTimer.
// StateChanged is fired on the thread-pool whenever the sampled state
// differs from the previously observed state.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Cross-platform thermal state poller. Detects device temperature using
/// OS-level APIs and surfaces it as a <see cref="ThermalState"/> value so
/// inference workers can pause before the OS forces throttling.
/// </summary>
public sealed class ThermalThrottleService : IThermalThrottleService
{
    // ------------------------------------------------------------------
    // Constants
    // ------------------------------------------------------------------

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    // Kelvin thresholds used for WMI readings (tenths-of-Kelvin → Kelvin).
    private const double KelvinSeriousThreshold  = 348.0; // 75 °C
    private const double KelvinCriticalThreshold = 363.0; // 90 °C

    // Millidegrees-Celsius thresholds used for Linux sysfs readings.
    private const int MilliCelsiusSerious  = 75_000;
    private const int MilliCelsiusCritical = 90_000;

    // ------------------------------------------------------------------
    // Fields
    // ------------------------------------------------------------------

    private readonly ILogger<ThermalThrottleService> _logger;

    // Stored as int so we can use Interlocked operations.
    private volatile int _currentStateRaw = (int)ThermalState.Unknown;

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _disposed;

    // 0 = not running, 1 = running.  Prevents concurrent StartMonitoring calls.
    private int _running;

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a new <see cref="ThermalThrottleService"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostics and exception details.</param>
    public ThermalThrottleService(ILogger<ThermalThrottleService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ------------------------------------------------------------------
    // IThermalThrottleService
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public ThermalState CurrentState => (ThermalState)_currentStateRaw;

    /// <inheritdoc/>
    public bool ShouldPauseInference => CurrentState >= ThermalState.Serious;

    /// <inheritdoc/>
    public event EventHandler<ThermalState>? StateChanged;

    /// <inheritdoc/>
    public void StartMonitoring(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Ensure only one polling loop runs at a time.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        _pollTask = Task.Run(() => PollLoopAsync(token), token);
    }

    /// <inheritdoc/>
    public void StopMonitoring()
    {
        if (_cts is null)
            return;

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed — nothing to do.
        }

        // Reset so StartMonitoring can be called again after a stop.
        Interlocked.Exchange(ref _running, 0);
    }

    // ------------------------------------------------------------------
    // Polling loop
    // ------------------------------------------------------------------

    private async Task PollLoopAsync(CancellationToken ct)
    {
        // Sample immediately so callers get a valid state before the first
        // interval elapses.
        ApplyNewState(SampleThermalState());

        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                ApplyNewState(SampleThermalState());
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — swallow.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ThermalThrottleService polling loop terminated unexpectedly.");
        }
    }

    private void ApplyNewState(ThermalState newState)
    {
        int newRaw      = (int)newState;
        int previousRaw = Interlocked.Exchange(ref _currentStateRaw, newRaw);

        if (previousRaw != newRaw)
        {
            var previous = (ThermalState)previousRaw;
            _logger.LogInformation(
                "ThermalState changed: {Previous} → {New}",
                previous, newState);

            try
            {
                StateChanged?.Invoke(this, newState);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in ThermalState.StateChanged handler.");
            }
        }
    }

    // ------------------------------------------------------------------
    // Platform sampling — safe wrapper
    // ------------------------------------------------------------------

    private ThermalState SampleThermalState()
    {
        try
        {
            return SamplePlatform();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sample thermal state; returning Unknown.");
            return ThermalState.Unknown;
        }
    }

    // ------------------------------------------------------------------
    // Platform-specific sampling implementations
    // ------------------------------------------------------------------

#if ANDROID

    // Android — PowerManager.ThermalStatus (API 29+)
    // ThermalStatus values: NONE=0, LIGHT=1, MODERATE=2, SEVERE=3,
    //                       CRITICAL=4, EMERGENCY=5, SHUTDOWN=6

    [System.Runtime.InteropServices.DllImport("libandroid.so")]
    private static extern int android_get_device_api_level();

    private static int TryGetAndroidApiLevel()
    {
        try { return android_get_device_api_level(); }
        catch { return 0; }
    }

    private static ThermalState SamplePlatform()
    {
        if (TryGetAndroidApiLevel() < 29)
            return ThermalState.Unknown;

        var context = Android.App.Application.Context;
        var pm = context.GetSystemService(Android.Content.Context.PowerService)
                    as Android.OS.PowerManager;
        if (pm is null)
            return ThermalState.Unknown;

        int status = (int)pm.CurrentThermalStatus;
        return status switch
        {
            0 => ThermalState.Normal,
            1 => ThermalState.Normal,
            2 => ThermalState.Fair,
            3 => ThermalState.Serious,
            _ => ThermalState.Critical, // 4 (CRITICAL), 5 (EMERGENCY), 6 (SHUTDOWN)
        };
    }

#elif IOS || MACCATALYST

    // iOS / macCatalyst — NSProcessInfo.ThermalState
    // NSProcessInfoThermalState: Nominal=0, Fair=1, Serious=2, Critical=3

    private static ThermalState SamplePlatform()
    {
        var state = Foundation.NSProcessInfo.ProcessInfo.ThermalState;
        return state switch
        {
            Foundation.NSProcessInfoThermalState.Nominal  => ThermalState.Normal,
            Foundation.NSProcessInfoThermalState.Fair     => ThermalState.Fair,
            Foundation.NSProcessInfoThermalState.Serious  => ThermalState.Serious,
            Foundation.NSProcessInfoThermalState.Critical => ThermalState.Critical,
            _ => ThermalState.Unknown,
        };
    }

#elif WINDOWS

    // Windows — WMI MSAcpi_ThermalZoneTemperature
    // CurrentTemperature is in tenths of a degree Kelvin.

    private static ThermalState SamplePlatform()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\wmi",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            double maxKelvin = 0;
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                if (obj["CurrentTemperature"] is uint raw)
                {
                    double kelvin = raw / 10.0;
                    if (kelvin > maxKelvin)
                        maxKelvin = kelvin;
                }
            }

            if (maxKelvin <= 0)
                return ThermalState.Unknown;

            if (maxKelvin > KelvinCriticalThreshold) return ThermalState.Critical;
            if (maxKelvin > KelvinSeriousThreshold)  return ThermalState.Serious;
            return ThermalState.Normal;
        }
        catch
        {
            // WMI may be unavailable in containers or restricted environments.
            return ThermalState.Unknown;
        }
    }

#else

    // Linux (and other non-MAUI, non-Windows platforms)
    // /sys/class/thermal/thermal_zone0/temp is in millidegrees Celsius.

    private const string LinuxThermalPath = "/sys/class/thermal/thermal_zone0/temp";

    private static ThermalState SamplePlatform()
    {
        if (!System.IO.File.Exists(LinuxThermalPath))
            return ThermalState.Unknown;

        try
        {
            string text = System.IO.File.ReadAllText(LinuxThermalPath).Trim();
            if (!int.TryParse(text, out int milliCelsius))
                return ThermalState.Unknown;

            if (milliCelsius > MilliCelsiusCritical) return ThermalState.Critical;
            if (milliCelsius > MilliCelsiusSerious)  return ThermalState.Serious;
            return ThermalState.Normal;
        }
        catch
        {
            return ThermalState.Unknown;
        }
    }

#endif

    // ------------------------------------------------------------------
    // IDisposable
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopMonitoring();

        _cts?.Dispose();
        _cts = null;
    }
}
