namespace Bhengu.AI.Aether;

// ──────────────────────────────────────────────────────────────────────────
// Contract 2 — Presence and Capability
//
// Answers: "Is Aether here, and at what level?"
// Apps query this at startup; the bootstrap acts on the result.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Indicates where Aether is installed and who manages it.
/// </summary>
public enum AetherInstallLevel
{
    /// <summary>Aether is not present on this device.</summary>
    None,

    /// <summary>
    /// Aether was installed at app level — either bundled with the app or
    /// downloaded at first launch. Updated independently by the app.
    /// </summary>
    App,

    /// <summary>
    /// Aether is a system service managed by the OS. Always present on TGN
    /// devices. Updated with OS updates. Requires biometric + device admin
    /// auth to toggle on or off.
    /// </summary>
    OS,
}

/// <summary>
/// Reports the presence, version, and capability of the Aether runtime on
/// this device. Inject via DI; the platform adapter (MAUI, server) provides
/// the concrete implementation.
/// </summary>
public interface IAetherContext
{
    /// <summary>Where Aether is installed, if at all.</summary>
    AetherInstallLevel InstallLevel { get; }

    /// <summary>True when Aether is installed and enabled.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The installed Aether runtime version, or null when Aether is absent.
    /// </summary>
    Version? RuntimeVersion { get; }

    /// <summary>
    /// The minimum Aether version declared as required by the consuming app.
    /// Set this via configuration; the bootstrap checks it on startup.
    /// </summary>
    Version? MinimumRequired { get; }

    /// <summary>
    /// True when <see cref="RuntimeVersion"/> satisfies
    /// <see cref="MinimumRequired"/>. Always true when MinimumRequired is
    /// null.
    /// </summary>
    bool IsSufficient { get; }

    /// <summary>
    /// True when the install level is <see cref="AetherInstallLevel.OS"/>.
    /// OS-managed instances require biometric + device admin auth before
    /// they can be toggled.
    /// </summary>
    bool RequiresAuth { get; }

    /// <summary>
    /// True when Aether is installed and currently enabled. An OS-managed
    /// instance that has been toggled off returns false here.
    /// </summary>
    bool IsEnabled { get; }
}
