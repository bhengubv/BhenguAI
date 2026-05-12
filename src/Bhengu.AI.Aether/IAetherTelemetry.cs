namespace Bhengu.AI.Aether;

// ──────────────────────────────────────────────────────────────────────────
// Contract 1 — Telemetry
//
// Aether publishes. BhenguAI subscribes. Aether never calls into BhenguAI.
// External Aether adopters can implement IAetherTelemetry without pulling
// in any AI dependency.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Receives events emitted by Aether. Implement this to react to mesh
/// activity — nodes, transports, routes, security signals, and topology.
/// </summary>
public interface IAetherTelemetryObserver
{
    void OnNodeEvent(AetherNodeEvent e);
    void OnTransportEvent(AetherTransportEvent e);
    void OnRouteEvent(AetherRouteEvent e);
    void OnSecurityEvent(AetherSecurityEvent e);
    void OnNetworkEvent(AetherNetworkEvent e);
}

/// <summary>
/// The outward-facing telemetry surface of Aether. The AI Security Layer
/// and any other BhenguAI component subscribes here. Aether owns this
/// interface and publishes; consumers subscribe and dispose.
/// </summary>
public interface IAetherTelemetry
{
    /// <summary>
    /// Subscribe to all Aether telemetry events.
    /// Dispose the returned handle to unsubscribe.
    /// </summary>
    IDisposable Subscribe(IAetherTelemetryObserver observer);
}

/// <summary>
/// No-op telemetry — useful for unit tests and environments where Aether
/// is absent. Subscribe returns a no-op disposable; no events are emitted.
/// </summary>
public sealed class NullAetherTelemetry : IAetherTelemetry
{
    public static readonly NullAetherTelemetry Instance = new();

    public IDisposable Subscribe(IAetherTelemetryObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        return NullDisposable.Instance;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
