using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Voice;
using Xunit;

namespace Circle.AI.Tests;

// ============================================================================
// NullWakeWordDetector
// ============================================================================

public sealed class NullWakeWordDetectorTests
{
    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DefaultConstructor_WakeWordIsHeyB()
    {
        await using var detector = new NullWakeWordDetector();
        Assert.Equal("Hey B", detector.WakeWord);
    }

    [Fact]
    public async Task CustomConstructor_SetsWakeWord()
    {
        await using var detector = new NullWakeWordDetector("wake up");
        Assert.Equal("wake up", detector.WakeWord);
    }

    [Fact]
    public void Constructor_NullWakeWord_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NullWakeWordDetector(null!));
    }

    [Fact]
    public void Constructor_EmptyWakeWord_Throws()
    {
        Assert.Throws<ArgumentException>(() => new NullWakeWordDetector(""));
    }

    [Fact]
    public void Constructor_WhitespaceWakeWord_Throws()
    {
        Assert.Throws<ArgumentException>(() => new NullWakeWordDetector("   "));
    }

    // -----------------------------------------------------------------------
    // IsListening transitions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IsListening_InitiallyFalse()
    {
        await using var detector = new NullWakeWordDetector();
        Assert.False(detector.IsListening);
    }

    [Fact]
    public async Task StartAsync_SetsIsListeningTrue()
    {
        await using var detector = new NullWakeWordDetector();
        await detector.StartAsync();
        Assert.True(detector.IsListening);
    }

    [Fact]
    public async Task StopAsync_SetsIsListeningFalse()
    {
        await using var detector = new NullWakeWordDetector();
        await detector.StartAsync();
        await detector.StopAsync();
        Assert.False(detector.IsListening);
    }

    // -----------------------------------------------------------------------
    // Dispose guards
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var detector = new NullWakeWordDetector();
        await detector.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => detector.StartAsync());
    }

    [Fact]
    public async Task StopAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var detector = new NullWakeWordDetector();
        await detector.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => detector.StopAsync());
    }

    [Fact]
    public async Task DisposeAsync_SetsIsListeningFalse()
    {
        var detector = new NullWakeWordDetector();
        await detector.StartAsync();
        await detector.DisposeAsync();
        Assert.False(detector.IsListening);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var detector = new NullWakeWordDetector();
        await detector.DisposeAsync();
        var ex = await Record.ExceptionAsync(() => detector.DisposeAsync().AsTask());
        Assert.Null(ex);
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await using var detector = new NullWakeWordDetector();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            detector.StartAsync(cts.Token));
    }

    // -----------------------------------------------------------------------
    // WakeWordDetected never fires
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WakeWordDetected_NeverRaised_ByNullDetector()
    {
        await using var detector = new NullWakeWordDetector();
        var raised = false;
        detector.WakeWordDetected += (_, _) => raised = true;

        await detector.StartAsync();
        await Task.Delay(50);
        await detector.StopAsync();

        Assert.False(raised);
    }
}

// ============================================================================
// NullVoiceTranscriber
// ============================================================================

public sealed class NullVoiceTranscriberTests
{
    // -----------------------------------------------------------------------
    // TranscribeAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_ReturnsEmptyResult()
    {
        await using var t = new NullVoiceTranscriber();
        var result = await t.TranscribeAsync(new byte[] { 0, 1, 2, 3 });
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0f, result.Confidence);
    }

    [Fact]
    public async Task TranscribeAsync_AfterDispose_Throws()
    {
        var t = new NullVoiceTranscriber();
        await t.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            t.TranscribeAsync(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public async Task TranscribeAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await using var t = new NullVoiceTranscriber();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            t.TranscribeAsync(ReadOnlyMemory<byte>.Empty, cts.Token));
    }

    // -----------------------------------------------------------------------
    // StreamTranscribeAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StreamTranscribeAsync_YieldsNoPartials()
    {
        await using var t = new NullVoiceTranscriber();
        var count = 0;

        var audioStream = GenerateAudioChunksAsync(5);
        await foreach (var _ in t.StreamTranscribeAsync(audioStream))
            count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task StreamTranscribeAsync_NullStream_Throws()
    {
        await using var t = new NullVoiceTranscriber();
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in t.StreamTranscribeAsync(null!)) { }
        });
    }

    [Fact]
    public async Task StreamTranscribeAsync_AfterDispose_Throws()
    {
        var t = new NullVoiceTranscriber();
        await t.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in t.StreamTranscribeAsync(EmptyAudioStream())) { }
        });
    }

    // -----------------------------------------------------------------------
    // DisposeAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var t = new NullVoiceTranscriber();
        await t.DisposeAsync();
        var ex = await Record.ExceptionAsync(() => t.DisposeAsync().AsTask());
        Assert.Null(ex);
    }

    // -----------------------------------------------------------------------
    // AudioFormat constant (sanity)
    // -----------------------------------------------------------------------

    [Fact]
    public void AudioFormat_Pcm16Mono16k_HasCorrectValues()
    {
        var fmt = AudioFormat.Pcm16Mono16k;
        Assert.Equal(16_000, fmt.SampleRate);
        Assert.Equal(1, fmt.Channels);
        Assert.Equal(16, fmt.BitsPerSample);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> GenerateAudioChunksAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return new byte[] { (byte)i };
        }
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> EmptyAudioStream()
    {
        await Task.CompletedTask;
        yield break;
    }
}
