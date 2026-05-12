using System;
using Circle.AI.Voice;
using Xunit;

namespace Circle.AI.Tests;

// ============================================================================
// TranscriptionResult
// ============================================================================

public sealed class TranscriptionResultTests
{
    [Fact]
    public void Constructor_MapsAllParameters()
    {
        var r = new TranscriptionResult("hello world", 0.9f, "en");
        Assert.Equal("hello world", r.Text);
        Assert.Equal(0.9f, r.Confidence);
        Assert.Equal("en", r.LanguageCode);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var r1 = new TranscriptionResult("hi", 0.8f, "zu");
        var r2 = new TranscriptionResult("hi", 0.8f, "zu");
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void Equality_DifferentText_NotEqual()
    {
        var r1 = new TranscriptionResult("hello", 0.9f, "en");
        var r2 = new TranscriptionResult("world", 0.9f, "en");
        Assert.NotEqual(r1, r2);
    }

    [Fact]
    public void Equality_DifferentConfidence_NotEqual()
    {
        var r1 = new TranscriptionResult("hi", 0.9f, "en");
        var r2 = new TranscriptionResult("hi", 0.5f, "en");
        Assert.NotEqual(r1, r2);
    }

    [Fact]
    public void Equality_DifferentLanguageCode_NotEqual()
    {
        var r1 = new TranscriptionResult("hi", 0.9f, "en");
        var r2 = new TranscriptionResult("hi", 0.9f, "zu");
        Assert.NotEqual(r1, r2);
    }

    [Fact]
    public void WithExpression_OverridesText()
    {
        var original = new TranscriptionResult("hello", 0.9f, "en");
        var modified  = original with { Text = "world" };
        Assert.Equal("world", modified.Text);
        Assert.Equal(0.9f,    modified.Confidence);
        Assert.Equal("en",    modified.LanguageCode);
    }

    [Fact]
    public void WithExpression_OverridesLanguageCode()
    {
        var original = new TranscriptionResult("hi", 0.8f, "und");
        var modified  = original with { LanguageCode = "zu" };
        Assert.Equal("hi",  modified.Text);
        Assert.Equal("zu",  modified.LanguageCode);
    }

    [Fact]
    public void EmptyText_IsAllowed()
    {
        // NullVoiceTranscriber returns empty text — this must be a valid value.
        var r = new TranscriptionResult(string.Empty, 0f, "und");
        Assert.Equal(string.Empty, r.Text);
        Assert.Equal(0f,           r.Confidence);
        Assert.Equal("und",        r.LanguageCode);
    }

    [Fact]
    public void HashCode_SameValues_AreEqual()
    {
        var r1 = new TranscriptionResult("test", 0.7f, "en");
        var r2 = new TranscriptionResult("test", 0.7f, "en");
        Assert.Equal(r1.GetHashCode(), r2.GetHashCode());
    }

    [Fact]
    public void ToString_ContainsText()
    {
        // Record's generated ToString should mention the positional parameters.
        var r = new TranscriptionResult("howzit", 0.95f, "zu");
        Assert.Contains("howzit", r.ToString());
    }
}

// ============================================================================
// PartialTranscription
// ============================================================================

public sealed class PartialTranscriptionTests
{
    [Fact]
    public void Constructor_MapsAllParameters()
    {
        var p = new PartialTranscription("hello", true, 0.95f);
        Assert.Equal("hello", p.Text);
        Assert.True(p.IsFinal);
        Assert.Equal(0.95f, p.Confidence);
    }

    [Fact]
    public void IsFinal_False_ReflectsIntermediate()
    {
        var p = new PartialTranscription("hel...", false, 0.4f);
        Assert.False(p.IsFinal);
        Assert.Equal("hel...", p.Text);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var p1 = new PartialTranscription("hi", true, 0.9f);
        var p2 = new PartialTranscription("hi", true, 0.9f);
        Assert.Equal(p1, p2);
    }

    [Fact]
    public void Equality_DifferentIsFinal_NotEqual()
    {
        var p1 = new PartialTranscription("hi", true,  0.9f);
        var p2 = new PartialTranscription("hi", false, 0.9f);
        Assert.NotEqual(p1, p2);
    }

    [Fact]
    public void Equality_DifferentText_NotEqual()
    {
        var p1 = new PartialTranscription("hello", true, 0.9f);
        var p2 = new PartialTranscription("world", true, 0.9f);
        Assert.NotEqual(p1, p2);
    }

    [Fact]
    public void WithExpression_OverridesIsFinal()
    {
        var partial = new PartialTranscription("text", false, 0.5f);
        var final   = partial with { IsFinal = true };
        Assert.True(final.IsFinal);
        Assert.Equal("text", final.Text);
        Assert.Equal(0.5f,   final.Confidence);
    }

    [Fact]
    public void WithExpression_OverridesText()
    {
        var p1 = new PartialTranscription("hel", false, 0.4f);
        var p2 = p1 with { Text = "hello world", IsFinal = true };
        Assert.Equal("hello world", p2.Text);
        Assert.True(p2.IsFinal);
        Assert.Equal(0.4f, p2.Confidence);
    }

    [Fact]
    public void HashCode_SameValues_AreEqual()
    {
        var p1 = new PartialTranscription("test", false, 0.6f);
        var p2 = new PartialTranscription("test", false, 0.6f);
        Assert.Equal(p1.GetHashCode(), p2.GetHashCode());
    }

    [Fact]
    public void EmptyText_IsAllowed()
    {
        var p = new PartialTranscription(string.Empty, false, 0f);
        Assert.Equal(string.Empty, p.Text);
    }
}

// ============================================================================
// AudioFormat (record extras — Pcm16Mono16k sanity is in NullVoiceTests)
// ============================================================================

public sealed class AudioFormatRecordTests
{
    [Fact]
    public void CustomConstructor_SetsAllProperties()
    {
        var fmt = new AudioFormat(44100, 2, 32);
        Assert.Equal(44100, fmt.SampleRate);
        Assert.Equal(2,     fmt.Channels);
        Assert.Equal(32,    fmt.BitsPerSample);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new AudioFormat(16_000, 1, 16);
        var b = new AudioFormat(16_000, 1, 16);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentSampleRate_NotEqual()
    {
        var a = new AudioFormat(16_000, 1, 16);
        var b = new AudioFormat(44_100, 1, 16);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentChannels_NotEqual()
    {
        var a = new AudioFormat(16_000, 1, 16);
        var b = new AudioFormat(16_000, 2, 16);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentBitsPerSample_NotEqual()
    {
        var a = new AudioFormat(16_000, 1, 16);
        var b = new AudioFormat(16_000, 1, 32);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Pcm16Mono16k_EqualsManuallyConstructed()
    {
        var manual = new AudioFormat(16_000, 1, 16);
        Assert.Equal(AudioFormat.Pcm16Mono16k, manual);
    }

    [Fact]
    public void WithExpression_ChangesSingleProperty()
    {
        var original = AudioFormat.Pcm16Mono16k;
        var stereo   = original with { Channels = 2 };

        Assert.Equal(2,       stereo.Channels);
        Assert.Equal(16_000,  stereo.SampleRate);  // unchanged
        Assert.Equal(16,      stereo.BitsPerSample); // unchanged
    }

    [Fact]
    public void HashCode_SameValues_AreEqual()
    {
        var a = new AudioFormat(22_050, 1, 16);
        var b = new AudioFormat(22_050, 1, 16);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}

// ============================================================================
// WakeWordDetectedEventArgs
// ============================================================================

public sealed class WakeWordDetectedEventArgsTests
{
    [Fact]
    public void RequiredWakeWord_SetViaInit()
    {
        var args = new WakeWordDetectedEventArgs { WakeWord = "Hey B", Confidence = 0.98f };
        Assert.Equal("Hey B", args.WakeWord);
    }

    [Fact]
    public void Confidence_SetViaInit()
    {
        var args = new WakeWordDetectedEventArgs { WakeWord = "hey butler", Confidence = 0.75f };
        Assert.Equal(0.75f, args.Confidence);
    }

    [Fact]
    public void DefaultConfidence_IsZero()
    {
        // When only the required property is supplied, Confidence defaults to 0.
        var args = new WakeWordDetectedEventArgs { WakeWord = "Hey B" };
        Assert.Equal(0f, args.Confidence);
    }

    [Fact]
    public void DetectedAt_DefaultsToRecentUtcTime()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var args   = new WakeWordDetectedEventArgs { WakeWord = "Hey B" };
        var after  = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.InRange(args.DetectedAt, before, after);
    }

    [Fact]
    public void DetectedAt_CanBeOverridden()
    {
        var specific = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var args = new WakeWordDetectedEventArgs
        {
            WakeWord   = "Hey B",
            DetectedAt = specific,
        };
        Assert.Equal(specific, args.DetectedAt);
    }

    [Fact]
    public void ConfidenceAtMaximum_IsAccepted()
    {
        var args = new WakeWordDetectedEventArgs { WakeWord = "Hey B", Confidence = 1.0f };
        Assert.Equal(1.0f, args.Confidence);
    }

    [Fact]
    public void IsEventArgs_InheritanceHolds()
    {
        // Verify the type hierarchy so subscribers can safely cast from EventArgs.
        EventArgs ea = new WakeWordDetectedEventArgs { WakeWord = "Hey B" };
        Assert.IsType<WakeWordDetectedEventArgs>(ea);
    }
}
