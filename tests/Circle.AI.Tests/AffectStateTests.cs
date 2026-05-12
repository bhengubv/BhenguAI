// AffectStateTests.cs
//
// Unit tests for AffectState — the HER affect layer (Track 4).

using System;
using Circle.AI.Memory;
using Xunit;

namespace Circle.AI.Tests;

public sealed class AffectStateTests
{
    // ------------------------------------------------------------------
    // ToSystemPromptHint
    // ------------------------------------------------------------------

    [Fact]
    public void ToSystemPromptHint_AllValuesNeutral_ReturnsEmptyString()
    {
        // Arrange: defaults sit at neutral (Curiosity=0.5, Engagement=0.5,
        // Uncertainty=0.2, Rapport=0.0, Energy=0.5). None of these cross
        // the thresholds that produce a hint.
        var state = new AffectState
        {
            Curiosity   = 0.5f,
            Engagement  = 0.5f,
            Uncertainty = 0.2f,
            Rapport     = 0.0f,
            Energy      = 0.5f,
        };

        // Act
        var hint = state.ToSystemPromptHint();

        // Assert
        Assert.Equal(string.Empty, hint);
    }

    [Fact]
    public void ToSystemPromptHint_HighCuriosity_ContainsCuriousHint()
    {
        var state = new AffectState { Curiosity = 0.8f };

        var hint = state.ToSystemPromptHint();

        Assert.Contains("deeply curious", hint, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("[Affect state]", hint);
    }

    [Fact]
    public void ToSystemPromptHint_HighEngagement_ContainsEngagementHint()
    {
        var state = new AffectState { Engagement = 0.9f };

        var hint = state.ToSystemPromptHint();

        Assert.Contains("fully engaged", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSystemPromptHint_LowEngagement_ContainsBriefHint()
    {
        var state = new AffectState { Engagement = 0.2f };

        var hint = state.ToSystemPromptHint();

        Assert.Contains("brief", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSystemPromptHint_HighUncertainty_ContainsClarifyingHint()
    {
        var state = new AffectState { Uncertainty = 0.7f };

        var hint = state.ToSystemPromptHint();

        Assert.Contains("uncertain", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSystemPromptHint_HighRapport_ContainsWarmHint()
    {
        var state = new AffectState { Rapport = 0.8f };

        var hint = state.ToSystemPromptHint();

        Assert.Contains("warm", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSystemPromptHint_LowEnergy_ContainsCalmHint()
    {
        var state = new AffectState { Energy = 0.2f };

        var hint = state.ToSystemPromptHint();

        Assert.Contains("calm", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSystemPromptHint_HighEnergy_ContainsEnergeticHint()
    {
        var state = new AffectState { Energy = 0.9f };

        var hint = state.ToSystemPromptHint();

        Assert.Contains("energetic", hint, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // ApplyPositiveSignal
    // ------------------------------------------------------------------

    [Fact]
    public void ApplyPositiveSignal_IncreasesEngagement()
    {
        var state = new AffectState { Engagement = 0.5f };
        var before = state.Engagement;

        state.ApplyPositiveSignal();

        Assert.True(state.Engagement > before);
    }

    [Fact]
    public void ApplyPositiveSignal_IncreasesRapport()
    {
        var state = new AffectState { Rapport = 0.3f };
        var before = state.Rapport;

        state.ApplyPositiveSignal();

        Assert.True(state.Rapport > before);
    }

    [Fact]
    public void ApplyPositiveSignal_DecreasesUncertainty()
    {
        var state = new AffectState { Uncertainty = 0.5f };
        var before = state.Uncertainty;

        state.ApplyPositiveSignal();

        Assert.True(state.Uncertainty < before);
    }

    [Fact]
    public void ApplyPositiveSignal_DoesNotExceedOne()
    {
        var state = new AffectState { Engagement = 0.99f, Rapport = 0.99f };

        state.ApplyPositiveSignal();

        Assert.True(state.Engagement <= 1f);
        Assert.True(state.Rapport <= 1f);
    }

    // ------------------------------------------------------------------
    // ApplyNegativeSignal
    // ------------------------------------------------------------------

    [Fact]
    public void ApplyNegativeSignal_DecreasesEngagement()
    {
        var state = new AffectState { Engagement = 0.5f };
        var before = state.Engagement;

        state.ApplyNegativeSignal();

        Assert.True(state.Engagement < before);
    }

    [Fact]
    public void ApplyNegativeSignal_IncreasesUncertainty()
    {
        var state = new AffectState { Uncertainty = 0.2f };
        var before = state.Uncertainty;

        state.ApplyNegativeSignal();

        Assert.True(state.Uncertainty > before);
    }

    [Fact]
    public void ApplyNegativeSignal_DoesNotGoBelowZero()
    {
        var state = new AffectState { Engagement = 0.01f };

        state.ApplyNegativeSignal();

        Assert.True(state.Engagement >= 0f);
    }

    // ------------------------------------------------------------------
    // ApplyIdleDecay
    // ------------------------------------------------------------------

    [Fact]
    public void ApplyIdleDecay_8Hours_MovesEngagementTowardNeutral_WhenAbove()
    {
        // Start above neutral; 8 hours idle should drift it toward 0.5.
        var state = new AffectState { Engagement = 0.9f };
        var before = state.Engagement;

        state.ApplyIdleDecay(TimeSpan.FromHours(8));

        Assert.True(state.Engagement < before, "Engagement should decrease toward neutral.");
        Assert.True(state.Engagement >= 0.5f,  "Engagement should not overshoot neutral.");
    }

    [Fact]
    public void ApplyIdleDecay_8Hours_MovesEngagementTowardNeutral_WhenBelow()
    {
        // Start below neutral; 8 hours idle should drift it toward 0.5.
        var state = new AffectState { Engagement = 0.1f };
        var before = state.Engagement;

        state.ApplyIdleDecay(TimeSpan.FromHours(8));

        Assert.True(state.Engagement > before, "Engagement should increase toward neutral.");
        Assert.True(state.Engagement <= 0.5f,  "Engagement should not overshoot neutral.");
    }

    [Fact]
    public void ApplyIdleDecay_8Hours_MovesEnergyTowardNeutral()
    {
        var state = new AffectState { Energy = 0.9f };

        state.ApplyIdleDecay(TimeSpan.FromHours(8));

        Assert.True(state.Energy < 0.9f);
        Assert.True(state.Energy >= 0.5f);
    }

    [Fact]
    public void ApplyIdleDecay_UpdatesLastUpdatedUtc()
    {
        var state = new AffectState();
        var before = state.LastUpdatedUtc;

        // Small sleep not needed — DateTimeOffset.UtcNow resolution is sufficient.
        state.ApplyIdleDecay(TimeSpan.FromHours(1));

        Assert.True(state.LastUpdatedUtc >= before);
    }
}
