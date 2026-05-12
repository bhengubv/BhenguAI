// VoiceHerTests.cs — Tests for HER-gap voice additions:
//   NullTtsEngine, NullVoiceActivityDetector, VoicePipeline VAD/TTS wiring,
//   VoiceOptions defaults.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Hosting;
using Bhengu.AI.Voice;
using Xunit;

namespace Bhengu.AI.Tests;

// ============================================================================
// NullTtsEngine
// ============================================================================

public sealed class NullTtsEngineTests
{
    [Fact]
    public async Task SynthesiseAsync_ReturnsEmptyAudioData()
    {
        var tts = new NullTtsEngine();
        var result = await tts.SynthesiseAsync("Hello world");
        Assert.True(result.AudioData.IsEmpty);
    }

    [Fact]
    public async Task SynthesiseAsync_ReturnsSampleRate24000()
    {
        var tts = new NullTtsEngine();
        var result = await tts.SynthesiseAsync("Hello");
        Assert.Equal(24_000, result.SampleRate);
    }

    [Fact]
    public async Task SynthesiseAsync_ReturnsMono16Bit()
    {
        var tts = new NullTtsEngine();
        var result = await tts.SynthesiseAsync("Hello");
        Assert.Equal(1, result.Channels);
        Assert.Equal(16, result.BitsPerSample);
    }

    [Fact]
    public async Task SynthesiseAsync_EmptyText_StillReturnsEmptyResult()
    {
        var tts = new NullTtsEngine();
        var result = await tts.SynthesiseAsync(string.Empty);
        Assert.True(result.AudioData.IsEmpty);
    }

    [Fact]
    public async Task SynthesiseAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tts = new NullTtsEngine();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            tts.SynthesiseAsync("Hello", cts.Token));
    }

    [Fact]
    public async Task StreamSynthesiseAsync_YieldsNoChunks()
    {
        var tts = new NullTtsEngine();
        var count = 0;
        await foreach (var _ in tts.StreamSynthesiseAsync("Hello"))
            count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task StreamSynthesiseAsync_CancelledToken_CompletesWithoutException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tts = new NullTtsEngine();
        // Cancelled token should not throw when stream produces nothing
        var count = 0;
        try
        {
            await foreach (var _ in tts.StreamSynthesiseAsync("Hello", cts.Token))
                count++;
        }
        catch (OperationCanceledException) { /* acceptable */ }
        Assert.Equal(0, count);
    }

    [Fact]
    public void EmptyResult_StaticField_HasCorrectMetadata()
    {
        var r = NullTtsEngine.EmptyResult;
        Assert.True(r.AudioData.IsEmpty);
        Assert.Equal(24_000, r.SampleRate);
        Assert.Equal(1, r.Channels);
        Assert.Equal(16, r.BitsPerSample);
    }
}

// ============================================================================
// NullVoiceActivityDetector
// ============================================================================

public sealed class NullVoiceActivityDetectorTests
{
    [Fact]
    public async Task DetectAsync_PassesAllChunksThrough()
    {
        var vad = new NullVoiceActivityDetector();
        var chunks = new[] { new byte[] { 1, 2 }, new byte[] { 3, 4 }, new byte[] { 5 } };
        var results = new List<VadSegment>();

        await foreach (var seg in vad.DetectAsync(ToAsyncEnumerable(chunks)))
            results.Add(seg);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task DetectAsync_AllSegmentsMarkedAsSpeech()
    {
        var vad = new NullVoiceActivityDetector();
        var chunks = new[] { new byte[] { 0 }, new byte[] { 1 } };

        await foreach (var seg in vad.DetectAsync(ToAsyncEnumerable(chunks)))
            Assert.True(seg.IsSpeech);
    }

    [Fact]
    public async Task DetectAsync_PreservesChunkContent()
    {
        var vad = new NullVoiceActivityDetector();
        var input = new byte[] { 7, 8, 9 };
        var results = new List<VadSegment>();

        await foreach (var seg in vad.DetectAsync(ToAsyncEnumerable(new[] { input })))
            results.Add(seg);

        Assert.Single(results);
        Assert.Equal(input, results[0].Audio.ToArray());
    }

    [Fact]
    public async Task DetectAsync_EmptyStream_YieldsNothing()
    {
        var vad = new NullVoiceActivityDetector();
        var count = 0;
        await foreach (var _ in vad.DetectAsync(EmptyAudioStream()))
            count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DetectAsync_NullStream_Throws()
    {
        var vad = new NullVoiceActivityDetector();
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in vad.DetectAsync(null!)) { }
        });
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ToAsyncEnumerable(
        byte[][] chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> EmptyAudioStream()
    {
        await Task.CompletedTask;
        yield break;
    }
}

// ============================================================================
// VoicePipeline — TTS and VAD wiring (HER additions)
// ============================================================================

public sealed class VoicePipelineHerTests
{
    [Fact]
    public void Constructor_WithTts_ExposesToTtsEngineProperty()
    {
        var tts = new NullTtsEngine();
        var pipeline = new VoicePipeline(
            new NullWakeWordDetector(),
            new NullVoiceTranscriber(),
            tts: tts);
        Assert.Same(tts, pipeline.TtsEngine);
    }

    [Fact]
    public void Constructor_WithoutTts_TtsEngineIsNull()
    {
        var pipeline = new VoicePipeline(
            new NullWakeWordDetector(),
            new NullVoiceTranscriber());
        Assert.Null(pipeline.TtsEngine);
    }

    [Fact]
    public void Constructor_WithVad_ExposesVoiceActivityDetectorProperty()
    {
        var vad = new NullVoiceActivityDetector();
        var pipeline = new VoicePipeline(
            new NullWakeWordDetector(),
            new NullVoiceTranscriber(),
            vad: vad);
        Assert.Same(vad, pipeline.VoiceActivityDetector);
    }

    [Fact]
    public void Constructor_WithoutVad_VoiceActivityDetectorIsNull()
    {
        var pipeline = new VoicePipeline(
            new NullWakeWordDetector(),
            new NullVoiceTranscriber());
        Assert.Null(pipeline.VoiceActivityDetector);
    }

    [Fact]
    public async Task DisposeAsync_WithTtsAndVad_DisposesWithoutException()
    {
        var pipeline = new VoicePipeline(
            new NullWakeWordDetector(),
            new NullVoiceTranscriber(),
            vad: new NullVoiceActivityDetector(),
            tts: new NullTtsEngine());
        var ex = await Record.ExceptionAsync(() => pipeline.DisposeAsync().AsTask());
        Assert.Null(ex);
    }
}

// ============================================================================
// VoiceOptions defaults
// ============================================================================

public sealed class VoiceOptionsTests
{
    [Fact]
    public void DefaultWakeWord_IsHeyB()
    {
        var opts = new VoiceOptions();
        Assert.Equal("hey b", opts.WakeWord);
    }

    [Fact]
    public void DefaultSampleRate_Is16000()
    {
        var opts = new VoiceOptions();
        Assert.Equal(16_000, opts.SampleRateHz);
    }

    [Fact]
    public void DefaultAutoStart_IsFalse()
    {
        var opts = new VoiceOptions();
        Assert.False(opts.AutoStart);
    }

    [Fact]
    public void DefaultTtsBackend_IsNull()
    {
        var opts = new VoiceOptions();
        Assert.Equal("null", opts.TtsBackend);
    }

    [Fact]
    public void DefaultEndOfSpeechSilenceMs_Is800()
    {
        var opts = new VoiceOptions();
        Assert.Equal(800, opts.EndOfSpeechSilenceMs);
    }

    [Fact]
    public void AIOptions_Voice_IsNullByDefault()
    {
        var opts = new AIOptions();
        Assert.Null(opts.Voice);
    }

    [Fact]
    public void AIOptions_WithVoice_RetainsVoiceOptions()
    {
        var voice = new VoiceOptions { WakeWord = "yo b" };
        var opts = new AIOptions { Voice = voice };
        Assert.Equal("yo b", opts.Voice!.WakeWord);
    }
}
