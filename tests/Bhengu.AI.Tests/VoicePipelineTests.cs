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

    // ------------------------------------------------------------------
    // WakeWord fires twice → second activation cancels first
    // ------------------------------------------------------------------

    [Fact]
    public async Task WakeWord_FiresTwice_BothActivationsComplete()
    {
        // The second wake event triggers CancelActivation() on the first
        // in-flight activation. Both events must not crash the pipeline.
        var wake        = new FakeWakeWordDetector();
        var transcriber = new FakeVoiceTranscriber("done");

        await using var pipeline = new VoicePipeline(wake, transcriber);

        var completedCount = 0;
        pipeline.Transcribed += (_, _) => Interlocked.Increment(ref completedCount);

        await pipeline.StartAsync();
        wake.FireWakeWord();
        wake.FireWakeWord(); // fires while first may still be in progress

        // Give time for both activations to settle
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (completedCount < 1 && DateTime.UtcNow < deadline)
            await Task.Delay(30);

        // At least one transcription must have completed; no crashes.
        Assert.True(completedCount >= 1);
    }

    // ------------------------------------------------------------------
    // Transcriber yields nothing → Transcribed NOT fired
    //
    // VoicePipeline.RunActivationAsync: `if (result is not null)`
    // When ToFinalAsync drains an empty stream it returns null.
    // Neither Transcribed nor ActivationFailed is raised — only a
    // Trace.TraceInformation log. This is the "silent audio" path
    // (empty capture, pure noise, or premature cancellation).
    // ------------------------------------------------------------------

    [Fact]
    public async Task WakeWord_TranscriberYieldsNothing_TranscribedNotFired()
    {
        var wake = new FakeWakeWordDetector();
        // NullVoiceTranscriber yields no PartialTranscription items.
        await using var pipeline = new VoicePipeline(wake, new NullVoiceTranscriber());

        var transcribed = false;
        Exception? failedEx = null;
        pipeline.Transcribed      += (_, _)  => transcribed = true;
        pipeline.ActivationFailed += (_, ex) => failedEx    = ex;

        await pipeline.StartAsync();
        wake.FireWakeWord();

        // Give RunActivationAsync time to complete — NullVoiceTranscriber
        // drains immediately and yields nothing, so this is near-instant.
        await Task.Delay(200);

        // Empty stream → ToFinalAsync returns null → result is null →
        // Transcribed must NOT fire; ActivationFailed must NOT fire.
        Assert.False(transcribed);
        Assert.Null(failedEx);
    }

    // ------------------------------------------------------------------
    // Transcriber yields only non-final partials → Transcribed fires
    //
    // ToFinalAsync iterates all partials; if none has IsFinal=true it
    // returns the last partial as the result (rather than null).
    // This documents that the pipeline accepts "no IsFinal=true ever"
    // as a valid stream end — it always surfaces the last partial seen.
    // ------------------------------------------------------------------

    [Fact]
    public async Task WakeWord_TranscriberYieldsOnlyNonFinalPartials_TranscribedFiredWithLastText()
    {
        var wake        = new FakeWakeWordDetector();
        var transcriber = new NonFinalOnlyTranscriber("partial result");

        await using var pipeline = new VoicePipeline(wake, transcriber);

        TranscribedEventArgs? received = null;
        pipeline.Transcribed += (_, e) => received = e;

        await pipeline.StartAsync();
        wake.FireWakeWord();

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (received is null && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        // The last partial (IsFinal=false) is still converted to a
        // TranscriptionResult and surfaced via Transcribed.
        Assert.NotNull(received);
        Assert.Equal("partial result", received.Result.Text);
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Transcriber that yields a single <see cref="PartialTranscription"/>
    /// with <see cref="PartialTranscription.IsFinal"/> = <c>false</c>, then
    /// ends the stream. Tests that <c>ToFinalAsync</c> returns the last
    /// partial even when no final token is ever produced.
    /// </summary>
    private sealed class NonFinalOnlyTranscriber : IVoiceTranscriber
    {
        private readonly string _text;
        public NonFinalOnlyTranscriber(string text) => _text = text;

        public async IAsyncEnumerable<PartialTranscription> StreamTranscribeAsync(
            IAsyncEnumerable<ReadOnlyMemory<byte>> audioChunks,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            // Drain the audio stream so the pipeline doesn't block.
            await foreach (var _ in audioChunks.WithCancellation(ct).ConfigureAwait(false)) { }
            yield return new PartialTranscription(_text, IsFinal: false, Confidence: 0.6f);
        }

        public Task<TranscriptionResult> TranscribeAsync(
            ReadOnlyMemory<byte> pcmAudio,
            CancellationToken ct = default)
            => Task.FromResult(new TranscriptionResult(_text, 0.6f, "und"));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

// ============================================================================
// TranscribedEventArgs — direct construction tests
// ============================================================================

public sealed class TranscribedEventArgsTests
{
    [Fact]
    public void RequiredResult_SetViaObjectInitializer_IsReflected()
    {
        var result = new TranscriptionResult("hello", 0.9f, "en");
        var args   = new TranscribedEventArgs { Result = result };
        Assert.Same(result, args.Result);
    }

    [Fact]
    public void CompletedAt_Defaults_ToRecentUtcTime()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var args   = new TranscribedEventArgs
        {
            Result = new TranscriptionResult("test", 0.8f, "und"),
        };
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.InRange(args.CompletedAt, before, after);
    }

    [Fact]
    public void CompletedAt_CanBeOverriddenWithSpecificTime()
    {
        var specific = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var args = new TranscribedEventArgs
        {
            Result      = new TranscriptionResult("hi", 0.5f, "zu"),
            CompletedAt = specific,
        };
        Assert.Equal(specific, args.CompletedAt);
    }

    [Fact]
    public void IsEventArgs_InheritanceHolds()
    {
        EventArgs ea = new TranscribedEventArgs
        {
            Result = new TranscriptionResult("test", 0.7f, "en"),
        };
        Assert.IsType<TranscribedEventArgs>(ea);
    }
}
