namespace Bhengu.AI.Security.Aether;

using Bhengu.AI.Aether;
using Bhengu.AI.Security;

// ─────────────────────────────────────────────────────────────────────────────
// AetherSecurityBridge
//
// Bridges the Aether telemetry feed (IAetherTelemetry / IAetherTelemetryObserver)
// into the transport-agnostic Bhengu.AI.Security layer (SecurityLayerService).
//
// Responsibilities:
//   1. Implements IAISecurityLayer so existing Aether callers can wire this
//      up without code changes.
//   2. Subscribes to IAetherTelemetry on StartAsync, translates each
//      AetherSecurityEvent into a PeerSecurityEvent and calls
//      SecurityLayerService.HandlePeerEvent().
//   3. Adapts ISecurityDirectiveConsumer (Aether contract) ↔
//      IPeerDirectiveConsumer (transport-agnostic contract).
//   4. Maps SecurityPosture ↔ PeerSecurityPosture.
//
// The SecurityLayerService does all the reasoning; this class is pure translation.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Connects an Aether mesh telemetry feed to the transport-agnostic
/// <see cref="SecurityLayerService"/>. Implements <see cref="IAISecurityLayer"/>
/// so it can be used as a drop-in replacement for the old Aether-coupled layer.
/// </summary>
public sealed class AetherSecurityBridge : IAISecurityLayer
{
    private readonly SecurityLayerService _layer;
    private IDisposable? _telemetrySubscription;

    /// <summary>
    /// Initialises the bridge using an existing transport-agnostic security layer.
    /// </summary>
    /// <param name="layer">
    /// The <see cref="SecurityLayerService"/> that will receive translated events.
    /// Must be already constructed but does not need to be started yet.
    /// </param>
    public AetherSecurityBridge(SecurityLayerService layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _layer = layer;
    }

    // ─── IAISecurityLayer ─────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Subscribes to <paramref name="telemetry"/> and starts the underlying
    /// <see cref="SecurityLayerService"/> background recovery loop.
    /// </remarks>
    public Task StartAsync(IAetherTelemetry telemetry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        _telemetrySubscription = telemetry.Subscribe(new Observer(this));
        return _layer.StartAsync(ct);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        _telemetrySubscription?.Dispose();
        _telemetrySubscription = null;
        await _layer.StopAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Wraps <paramref name="consumer"/> in an adapter that translates
    /// <see cref="PeerDirective"/> → <see cref="SecurityDirective"/> before
    /// forwarding to the Aether consumer.
    /// </remarks>
    public IDisposable SubscribeToDirectives(ISecurityDirectiveConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        return _layer.SubscribeToDirectives(new DirectiveAdapter(consumer));
    }

    /// <inheritdoc />
    public async Task<SecurityPosture> GetPostureAsync(CancellationToken ct = default)
    {
        var posture = await _layer.GetPostureAsync(ct);
        return new SecurityPosture(
            AetherMapper.ToAetherThreatLevel(posture.OverallThreatLevel),
            posture.QuarantinedPeerCount,
            posture.MonitoredPeerCount,
            posture.IsActive,
            posture.GeneratedAt);
    }

    // ─── Telemetry observer ───────────────────────────────────────────────────

    private sealed class Observer : IAetherTelemetryObserver
    {
        private readonly AetherSecurityBridge _bridge;
        internal Observer(AetherSecurityBridge bridge) => _bridge = bridge;

        public void OnSecurityEvent(AetherSecurityEvent e)
        {
            // Translate Aether event → PeerSecurityEvent → security layer.
            var peer = new PeerSecurityEvent(
                NodeId:      e.NodeId,
                Kind:        AetherMapper.ToPeerEventKind(e.Kind),
                ThreatLevel: AetherMapper.ToPeerThreatLevel(e.ThreatLevel),
                Description: e.Description,
                TransportId: "aether",
                OccurredAt:  e.OccurredAt);

            _bridge._layer.HandlePeerEvent(peer);
        }

        public void OnNodeEvent(AetherNodeEvent e)
        {
            if (e.IsExit)
                _bridge._layer.HandlePeerLeft(e.NodeId);
        }

        // Not relevant to security scoring — ignore.
        public void OnTransportEvent(AetherTransportEvent e) { }
        public void OnRouteEvent(AetherRouteEvent e)         { }
        public void OnNetworkEvent(AetherNetworkEvent e)     { }
    }

    // ─── Directive adapter ────────────────────────────────────────────────────

    /// <summary>
    /// Adapts an Aether <see cref="ISecurityDirectiveConsumer"/> so it can
    /// receive <see cref="PeerDirective"/> instances from the transport-agnostic
    /// layer, translating them back to <see cref="SecurityDirective"/> before delivery.
    /// </summary>
    private sealed class DirectiveAdapter : IPeerDirectiveConsumer
    {
        private readonly ISecurityDirectiveConsumer _consumer;
        internal DirectiveAdapter(ISecurityDirectiveConsumer consumer) => _consumer = consumer;

        public void OnDirective(PeerDirective directive)
        {
            var aether = new SecurityDirective(
                Kind:               AetherMapper.ToSecurityDirectiveKind(directive.Kind),
                TargetNodeId:       directive.TargetNodeId,
                TrustScoreOverride: directive.TrustScore,
                ThreatLevel:        AetherMapper.ToAetherThreatLevel(directive.ThreatLevel),
                Reason:             directive.Reason,
                Duration:           directive.Duration,
                IssuedAt:           directive.IssuedAt);

            _consumer.OnDirective(aether);
        }
    }
}
