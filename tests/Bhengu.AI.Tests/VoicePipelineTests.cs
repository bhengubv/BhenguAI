using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Voice;
using Xunit;

namespace Bhengu.AI.Tests;

public sealed class VoicePipelineTests
{
    // ------------------------------------------------------------------
    // NullAudioCapture
    // ------------------------------------------------------------------

    [Fact]
    public void NullAudioCapture_Format_IsPcm16Mono16k()
    {
        var cap = new NullAudioCapture();
        Assert.Equal(AudioFormat.Pcm16Mono16k, cap.Format);
    }

    [Fact]
    public async Task NullAudioCapture_CaptureAsync_YieldsNoChunks()
    {
        var cap = new NullAudioCapture();
        var count = 0;
        await foreach (var _ in cap.CaptureAsync(CancellationToken.None))
            count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task NullAudioCapture_CaptureAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cap = new NullAudioCapture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in cap.CaptureAsync(cts.Token)) { }
        });
    }

    // ------------------------------------------------------------------
    // VoicePipeline construction
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_NullWakeDetector_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new VoicePipeline(null!, new FakeVoiceTranscriber()));
    }

    [Fact]
    public void Constructor_NullTranscriber_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new VoicePipeline(new FakeWakeWordDetector(), null!));
    }

    [Fact]
    public void Constructor_NullCapture_UsesNullAudioCapture()
    {
        var pipeline = new VoicePipeline(
            new FakeWakeWordDetector(),
            new FakeVoiceTranscriber(),
            capture: null);

        Assert.IsType<NullAudioCapture>(pipeline.AudioCapture);
    }

    [Fact]
    public void Constructor_ExposesCollaborators()
    {
        var wake        = new FakeWakeWordDetector();
        var transcriber = new FakeVoiceTranscriber();
        var capture     = new NullAudioCapture();

        var pipeline = new VoicePipeline(wake, transcriber, capture);

        Assert.Same(wake,        pipeline.WakeDetector);
        Assert.Same(transcriber, pipeline.Transcriber);
        Assert.Same(capture,     pipeline.AudioCapture);
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_DelegatesToWakeDetector()
    {
        var wake = new FakeWakeWordDetector();
        await using var pipeline = new VoicePipeline(wake, new FakeVoiceTranscriber());

        await pipeline.StartAsync();
        Assert.Equal(1, wake.StartCallCount);
    }

    [Fact]
    public async Task StopAsync_DelegatesToWakeDetector()
    {
        var wake = new FakeWakeWordDetector();
        await using var pipeline = new VoicePipeline(wake, new FakeVoiceTranscriber());

        await pipeline.StartAsync();
        await pipeline.StopAsync();
        Assert.Equal(1, wake.StopCallCount);
    }

    // ------------------------------------------------------------------
    // Wake-word activation → transcription
    // ------------------------------------------------------------------

    [Fact]
    public async Task WakeWord_Fires_RaisesTranscribedEvent()
    {
        var wake        = new FakeWakeWordDetector();
        var transcriber = new FakeVoiceTranscriber("hello world");

        await using var pipeline = new VoicePipeline(wake, transcriber);

        TranscribedEventArgs? received = null;
        pipeline.Transcribed += (_, e) => received = e;

        await pipeline.StartAsync();
        wake.FireWakeWord();

        // Give the background Task time to complete
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (received is null && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.NotNull(received);
        Assert.Equal("hello world", received.Result.Text);
    }

    // ------------------------------------------------------------------
    // DisposeAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_UnwiresWakeWordEvent()
    {
        var wake = new FakeWakeWordDetector();
        var pipeline = new VoicePipeline(wake, new FakeVoiceTranscriber());

        await pipeline.StartAsync();
        await pipeline.DisposeAsync();

        var triggered = false;
        pipeline.Transcribed += (_, _) => triggered = true;

        // This should not trigger the pipeline (it's disposed)
        wake.FireWakeWord();
        await Task.Delay(100);

        Assert.False(triggered);
    }

    // ------------------------------------------------------------------
    // Dispose guards on StartAsync / StopAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_AfterDispose_Throws()
    {
        var pipeline = new VoicePipeline(
            new FakeWakeWordDetector(),
            new FakeVoiceTranscriber());

        await pipeline.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pipeline.StartAsync());
    }

    [Fact]
    public async Task StopAsync_AfterDispose_Throws()
    {
        var pipeline = new VoicePipeline(
            new FakeWakeWordDetector(),
            new FakeVoiceTranscriber());

        await pipeline.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pipeline.StopAsync());
    }

    [Fact]
    public async Task StopAsync_BeforeStart_IsNoOp()
    {
        // StopAsync before StartAsync is allowed; it just cancels any in-flight
        // activation (there is none) and delegates to the wake detector.
        await using var pipeline = new VoicePipeline(
            new FakeWakeWordDetector(),
            new FakeVoiceTranscriber());

        var ex = await Record.ExceptionAsync(() => pipeline.StopAsync());
        Assert.Null(ex);
    }

    // ------------------------------------------------------------------
    // ActivationFailed event
    // ------------------------------------------------------------------

    [Fact]
    public async Task ActivationFailed_Event_FiredWhenTranscriberThrows()
    {
        var wake        = new FakeWakeWordDetector();
        var transcriber = new FakeVoiceTranscriber(shouldThrow: true);

        await using var pipeline = new VoicePipeline(wake, transcriber);

        Exception? caught = null;
        pipeline.ActivationFailed += (_, ex) => caught = ex;

        await pipeline.StartAsync();
        wake.FireWakeWord();

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (caught is null && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.NotNull(caught);
        Assert.IsType<InvalidOperationException>(caught);
    }

    // ------------------------------------------------------------------
    // DisposeAsync idempotency
    // ------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var pipeline = new VoicePipeline(
            new FakeWakeWordDetector(),
            new FakeVoiceTranscriber());

        await pipeline.DisposeAsync();
        var ex = await Record.ExceptionAsync(pipeline.DisposeAsync().AsTask);
        Assert.Null(ex);
    }
}
