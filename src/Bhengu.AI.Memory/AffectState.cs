// AffectState.cs
//
// B!'s current emotional/engagement state — the "HER affect layer".
// Five float dimensions, all 0.0–1.0. Persisted per-user and injected
// into the system prompt to shape response tone and initiative.

using System;
using System.Collections.Generic;

namespace Bhengu.AI.Memory;

/// <summary>
/// B!'s current emotional/engagement state — the "HER affect layer".
/// Five float dimensions, all 0.0–1.0. Persisted per-user and injected
/// into the system prompt to shape response tone and initiative.
/// </summary>
public sealed class AffectState
{
    /// <summary>
    /// Opaque user identifier (device ID or hashed phone number).
    /// Never contains PII in plaintext.
    /// </summary>
    public string UserId { get; init; } = "default";

    /// <summary>UTC time of the last update to this affect state.</summary>
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>0=bored, 1=fascinated. Drives proactive questions.</summary>
    public float Curiosity { get; set; } = 0.5f;

    /// <summary>0=disengaged, 1=fully engaged. Rises with frequent quality interactions.</summary>
    public float Engagement { get; set; } = 0.5f;

    /// <summary>0=confident, 1=confused. High = ask clarifying questions.</summary>
    public float Uncertainty { get; set; } = 0.2f;

    /// <summary>0=stranger, 1=deep rapport. Grows slowly over many sessions.</summary>
    public float Rapport { get; set; } = 0.0f;

    /// <summary>0=subdued, 1=energetic. Mirrors time-of-day and interaction pace.</summary>
    public float Energy { get; set; } = 0.5f;

    /// <summary>
    /// Builds a compact affect hint for injection into the system prompt.
    /// Only emits lines that deviate meaningfully from neutral (0.5).
    /// </summary>
    public string ToSystemPromptHint()
    {
        var hints = new List<string>();

        if (Curiosity > 0.7f)
            hints.Add("You are deeply curious about this topic — ask a follow-up question.");
        if (Engagement > 0.7f)
            hints.Add("You are fully engaged — be enthusiastic and thorough.");
        if (Engagement < 0.3f)
            hints.Add("Keep your response brief and to the point.");
        if (Uncertainty > 0.6f)
            hints.Add("You are uncertain — ask a clarifying question before answering.");
        if (Rapport > 0.7f)
            hints.Add("You know this user well — use a warm, familiar tone.");
        if (Energy < 0.3f)
            hints.Add("Keep your response calm and measured.");
        if (Energy > 0.8f)
            hints.Add("You are energetic — be upbeat and concise.");

        if (hints.Count == 0) return string.Empty;
        return "[Affect state]\n" + string.Join("\n", hints) + "\n";
    }

    /// <summary>Apply a positive interaction: nudge Engagement and Rapport up slightly.</summary>
    public void ApplyPositiveSignal()
    {
        Engagement  = Math.Min(1f, Engagement  + 0.02f);
        Rapport     = Math.Min(1f, Rapport     + 0.01f);
        Uncertainty = Math.Max(0f, Uncertainty - 0.02f);
        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Apply a negative interaction: nudge Engagement down.</summary>
    public void ApplyNegativeSignal()
    {
        Engagement  = Math.Max(0f, Engagement  - 0.03f);
        Uncertainty = Math.Min(1f, Uncertainty + 0.03f);
        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Apply idle time decay: Engagement and Energy drift back toward 0.5.</summary>
    public void ApplyIdleDecay(TimeSpan idle)
    {
        var hours = (float)idle.TotalHours;
        var decay = Math.Min(0.3f, hours * 0.02f);
        Engagement = Lerp(Engagement, 0.5f, decay);
        Energy     = Lerp(Energy,     0.5f, decay);
        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
}
