namespace Circle.AI.Aether;

// ──────────────────────────────────────────────────────────────────────────
// Contract 5 — Auth Challenge
//
// Bidirectional trust gate.
//   → User auth enables the security layer at OS level.
//   ← Security layer demands re-auth when threat thresholds are crossed.
//
// Minimum: Biometric + DeviceAdmin for OS-level operations.
// Developers can raise the bar; they cannot lower it below the minimum.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>Why an auth challenge is being issued.</summary>
public enum AuthChallengeReason
{
    /// <summary>The user is enabling or disabling the OS-level Aether service.</summary>
    OsLevelToggle,

    /// <summary>
    /// The AI Security Layer detected anomaly scores above the configured
    /// threshold and requires the user to confirm their identity.
    /// </summary>
    ThreatThresholdReached,

    /// <summary>The operation being attempted requires elevated auth.</summary>
    PrivilegedOperation,

    /// <summary>Scheduled trust renewal — periodic re-validation.</summary>
    PeriodicRevalidation,

    /// <summary>Explicitly triggered by the developer or admin.</summary>
    ManualRequest,
}

/// <summary>
/// The authentication method used or required.
/// Methods are ordered by strength; higher numeric values are stronger.
/// </summary>
public enum AuthMethod
{
    /// <summary>Fingerprint, face, or iris recognition.</summary>
    Biometric = 1,

    /// <summary>Device administrator credential (PIN, password, pattern).</summary>
    DeviceAdmin = 2,

    /// <summary>
    /// Biometric AND device admin — the minimum for any OS-level operation.
    /// </summary>
    BiometricAndDeviceAdmin = 3,

    /// <summary>
    /// Developer-defined method layered on top of BiometricAndDeviceAdmin.
    /// </summary>
    Custom = 4,
}

/// <summary>The outcome of an auth challenge.</summary>
public sealed record AuthChallengeResult(
    bool Succeeded,
    AuthMethod MethodUsed,
    string? FailureReason,
    DateTimeOffset CompletedAt)
{
    /// <summary>Convenience: a successful result with no failure reason.</summary>
    public static AuthChallengeResult Success(AuthMethod method) =>
        new(true, method, null, DateTimeOffset.UtcNow);

    /// <summary>Convenience: a failed result with an explanatory reason.</summary>
    public static AuthChallengeResult Failure(AuthMethod method, string reason) =>
        new(false, method, reason, DateTimeOffset.UtcNow);
}

/// <summary>
/// Issues and resolves authentication challenges for security-sensitive
/// operations. Platform adapters (MAUI, server) implement this using native
/// biometric and device admin APIs.
/// </summary>
public interface IAuthChallenge
{
    /// <summary>
    /// Presents an auth challenge to the user for the given reason.
    /// The platform adapter enforces the minimum method requirement.
    /// </summary>
    /// <param name="reason">Why auth is being requested.</param>
    /// <param name="minimumMethod">
    /// The weakest method acceptable. Defaults to
    /// <see cref="AuthMethod.BiometricAndDeviceAdmin"/> when null.
    /// </param>
    /// <param name="prompt">Human-readable message shown to the user.</param>
    Task<AuthChallengeResult> ChallengeAsync(
        AuthChallengeReason reason,
        AuthMethod? minimumMethod,
        string prompt,
        CancellationToken ct = default);

    /// <summary>
    /// Presents the OS-level toggle challenge. Always requires
    /// <see cref="AuthMethod.BiometricAndDeviceAdmin"/> at minimum.
    /// </summary>
    /// <param name="enable">True to enable the service, false to disable.</param>
    Task<AuthChallengeResult> RequestOsToggleAsync(
        bool enable,
        CancellationToken ct = default);
}
