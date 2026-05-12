using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Aether;
using Xunit;

namespace Circle.AI.Tests;

// ─── Events ──────────────────────────────────────────────────────────────────

public sealed class AetherNodeEventTests
{
    [Fact] public void NodeHealth_ValidScore_IsValid() =>
        Assert.True(new AetherNodeHealth(0.85, true, TimeSpan.FromMilliseconds(12), 2).IsValid);

    [Fact] public void NodeHealth_ScoreAboveOne_IsNotValid() =>
        Assert.False(new AetherNodeHealth(1.1, true, TimeSpan.Zero, 1).IsValid);

    [Fact] public void NodeHealth_ScoreNegative_IsNotValid() =>
        Assert.False(new AetherNodeHealth(-0.1, true, TimeSpan.Zero, 1).IsValid);

    [Fact] public void NodeHealth_BoundaryZero_IsValid() =>
        Assert.True(new AetherNodeHealth(0.0, false, TimeSpan.Zero, 0).IsValid);

    [Fact] public void NodeHealth_BoundaryOne_IsValid() =>
        Assert.True(new AetherNodeHealth(1.0, true, TimeSpan.Zero, 1).IsValid);

    [Fact] public void NodeEvent_LeftKind_IsExit() =>
        Assert.True(Node(AetherNodeEventKind.Left).IsExit);

    [Fact] public void NodeEvent_JoinedKind_IsNotExit() =>
        Assert.False(Node(AetherNodeEventKind.Joined).IsExit);

    [Fact] public void NodeEvent_HealthChangedKind_IsNotExit() =>
        Assert.False(Node(AetherNodeEventKind.HealthChanged).IsExit);

    private static AetherNodeEvent Node(AetherNodeEventKind kind) =>
        new("node-1", kind, new AetherNodeHealth(1.0, true, TimeSpan.Zero, 1),
            DateTimeOffset.UtcNow);
}

public sealed class AetherTransportEventTests
{
    [Fact] public void ExceedsLoss_AboveThreshold_True() =>
        Assert.True(Transport(0.4).ExceedsLoss(0.3));

    [Fact] public void ExceedsLoss_BelowThreshold_False() =>
        Assert.False(Transport(0.2).ExceedsLoss(0.3));

    [Fact] public void ExceedsLoss_EqualThreshold_False() =>
        Assert.False(Transport(0.3).ExceedsLoss(0.3));

    [Fact] public void ExceedsLoss_NullPacketLoss_False() =>
        Assert.False(new AetherTransportEvent("n", AetherTransportEventKind.Selected,
            AetherTransportKind.WiFi, null, null, DateTimeOffset.UtcNow).ExceedsLoss(0.1));

    private static AetherTransportEvent Transport(double lossRate) =>
        new("node-1", AetherTransportEventKind.PacketLoss,
            AetherTransportKind.WiFi, TimeSpan.FromMilliseconds(5), lossRate,
            DateTimeOffset.UtcNow);
}

public sealed class AetherRouteEventTests
{
    [Fact] public void HopCount_ReflectsPathLength() =>
        Assert.Equal(3, Route(["a", "b", "c"]).HopCount);

    [Fact] public void HopCount_EmptyPath_Zero() =>
        Assert.Equal(0, Route([]).HopCount);

    [Fact] public void IsFailed_FailedKind_True() =>
        Assert.True(new AetherRouteEvent("s", "d", [], AetherRouteEventKind.Failed,
            "timeout", DateTimeOffset.UtcNow).IsFailed);

    [Fact] public void IsFailed_DiscoveredKind_False() =>
        Assert.False(Route(["a", "b"]).IsFailed);

    private static AetherRouteEvent Route(IReadOnlyList<string> path) =>
        new("src", "dst", path, AetherRouteEventKind.Discovered, null,
            DateTimeOffset.UtcNow);
}

public sealed class AetherSecurityEventTests
{
    [Fact] public void IsHighSeverity_High_True() =>
        Assert.True(Sec(AetherThreatLevel.High).IsHighSeverity);

    [Fact] public void IsHighSeverity_Critical_True() =>
        Assert.True(Sec(AetherThreatLevel.Critical).IsHighSeverity);

    [Fact] public void IsHighSeverity_Medium_False() =>
        Assert.False(Sec(AetherThreatLevel.Medium).IsHighSeverity);

    [Fact] public void IsHighSeverity_None_False() =>
        Assert.False(Sec(AetherThreatLevel.None).IsHighSeverity);

    [Fact] public void Metadata_IsReadOnly() =>
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
            Sec(AetherThreatLevel.Low).Metadata);

    private static AetherSecurityEvent Sec(AetherThreatLevel level) =>
        new("node-1", AetherSecurityEventKind.RoutingAnomaly, level,
            "test", new Dictionary<string, string>(), DateTimeOffset.UtcNow);
}

public sealed class AetherNetworkEventTests
{
    [Fact] public void IsHighCongestion_Above75Percent_True() =>
        Assert.True(Net(0.80).IsHighCongestion);

    [Fact] public void IsHighCongestion_Below75Percent_False() =>
        Assert.False(Net(0.50).IsHighCongestion);

    [Fact] public void IsHighCongestion_ExactlyAt75_False() =>
        Assert.False(Net(0.75).IsHighCongestion);

    private static AetherNetworkEvent Net(double congestion) =>
        new(AetherNetworkEventKind.CongestionDetected, 10, 5, congestion,
            DateTimeOffset.UtcNow);
}

// ─── IAetherTelemetry / NullAetherTelemetry ───────────────────────────────

public sealed class NullAetherTelemetryTests
{
    [Fact] public void Subscribe_ReturnsNonNullDisposable()
    {
        var handle = NullAetherTelemetry.Instance.Subscribe(new NoopObserver());
        Assert.NotNull(handle);
    }

    [Fact] public void Subscribe_DisposeTwice_DoesNotThrow()
    {
        var handle = NullAetherTelemetry.Instance.Subscribe(new NoopObserver());
        handle.Dispose();
        handle.Dispose(); // must be idempotent
    }

    [Fact] public void Subscribe_NullObserver_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            NullAetherTelemetry.Instance.Subscribe(null!));

    [Fact] public void Instance_IsSingleton() =>
        Assert.Same(NullAetherTelemetry.Instance, NullAetherTelemetry.Instance);

    private sealed class NoopObserver : IAetherTelemetryObserver
    {
        public void OnNodeEvent(AetherNodeEvent e) { }
        public void OnTransportEvent(AetherTransportEvent e) { }
        public void OnRouteEvent(AetherRouteEvent e) { }
        public void OnSecurityEvent(AetherSecurityEvent e) { }
        public void OnNetworkEvent(AetherNetworkEvent e) { }
    }
}

// ─── IAetherContext enums ─────────────────────────────────────────────────

public sealed class AetherInstallLevelTests
{
    [Fact] public void None_IsLowest_Level() =>
        Assert.True((int)AetherInstallLevel.None < (int)AetherInstallLevel.App);

    [Fact] public void OS_IsHighest_Level() =>
        Assert.True((int)AetherInstallLevel.OS > (int)AetherInstallLevel.App);

    [Fact] public void AllThreeLevels_AreDefined() =>
        Assert.Equal(3, Enum.GetValues<AetherInstallLevel>().Length);
}

// ─── IAetherIntelligence output records ───────────────────────────────────

public sealed class NetworkHealthReportTests
{
    [Fact] public void IsValid_ZeroToOne_True() =>
        Assert.True(new NetworkHealthReport(0.9, 10, 1, "ok", DateTimeOffset.UtcNow).IsValid);

    [Fact] public void IsValid_AboveOne_False() =>
        Assert.False(new NetworkHealthReport(1.1, 10, 1, "ok", DateTimeOffset.UtcNow).IsValid);

    [Fact] public void IsValid_Negative_False() =>
        Assert.False(new NetworkHealthReport(-0.1, 10, 1, "ok", DateTimeOffset.UtcNow).IsValid);
}

public sealed class ThreatAssessmentTests
{
    [Fact] public void IsValid_ZeroToOne_True() =>
        Assert.True(new ThreatAssessment("n", 0.5, AetherThreatLevel.Medium,
            [], DateTimeOffset.UtcNow).IsValid);

    [Fact] public void IsValid_OutOfRange_False() =>
        Assert.False(new ThreatAssessment("n", 1.5, AetherThreatLevel.High,
            [], DateTimeOffset.UtcNow).IsValid);
}

public sealed class TrustScoreUpdateTests
{
    [Fact] public void HasChanged_DifferentScores_True() =>
        Assert.True(new TrustScoreUpdate("n", 0.9, 0.5, "anomaly", DateTimeOffset.UtcNow).HasChanged);

    [Fact] public void HasChanged_SameScore_False() =>
        Assert.False(new TrustScoreUpdate("n", 0.9, 0.9, "none", DateTimeOffset.UtcNow).HasChanged);

    [Fact] public void IsDegraded_ScoreDecreased_True() =>
        Assert.True(new TrustScoreUpdate("n", 0.8, 0.4, "dropped", DateTimeOffset.UtcNow).IsDegraded);

    [Fact] public void IsDegraded_ScoreIncreased_False() =>
        Assert.False(new TrustScoreUpdate("n", 0.4, 0.8, "recovered", DateTimeOffset.UtcNow).IsDegraded);
}

// ─── SecurityDirective ────────────────────────────────────────────────────

public sealed class SecurityDirectiveTests
{
    [Fact] public void HasTarget_WithNodeId_True() =>
        Assert.True(Directive("node-42").HasTarget);

    [Fact] public void HasTarget_NullNodeId_False() =>
        Assert.False(Directive(null).HasTarget);

    [Fact] public void HasTarget_WhitespaceNodeId_False() =>
        Assert.False(Directive("   ").HasTarget);

    [Fact] public void IsPermanent_NullDuration_True() =>
        Assert.True(Directive("n", null).IsPermanent);

    [Fact] public void IsPermanent_WithDuration_False() =>
        Assert.False(Directive("n", TimeSpan.FromHours(1)).IsPermanent);

    private static SecurityDirective Directive(string? nodeId, TimeSpan? duration = null) =>
        new(SecurityDirectiveKind.QuarantineNode, nodeId, null,
            AetherThreatLevel.High, "test", duration, DateTimeOffset.UtcNow);
}

// ─── AuthChallengeResult ─────────────────────────────────────────────────

public sealed class AuthChallengeResultTests
{
    [Fact] public void Success_Factory_SetsSucceededTrue() =>
        Assert.True(AuthChallengeResult.Success(AuthMethod.BiometricAndDeviceAdmin).Succeeded);

    [Fact] public void Success_Factory_NoFailureReason() =>
        Assert.Null(AuthChallengeResult.Success(AuthMethod.Biometric).FailureReason);

    [Fact] public void Failure_Factory_SetsSucceededFalse() =>
        Assert.False(AuthChallengeResult.Failure(AuthMethod.DeviceAdmin, "cancelled").Succeeded);

    [Fact] public void Failure_Factory_SetsReason() =>
        Assert.Equal("cancelled",
            AuthChallengeResult.Failure(AuthMethod.DeviceAdmin, "cancelled").FailureReason);

    [Fact] public void BiometricAndDeviceAdmin_IsStrongerThanBiometricAlone() =>
        Assert.True((int)AuthMethod.BiometricAndDeviceAdmin > (int)AuthMethod.Biometric);

    [Fact] public void Custom_IsStrongestBuiltInMethod() =>
        Assert.True((int)AuthMethod.Custom > (int)AuthMethod.BiometricAndDeviceAdmin);
}
