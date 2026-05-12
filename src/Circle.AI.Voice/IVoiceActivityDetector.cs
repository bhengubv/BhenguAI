namespace Circle.AI.Voice;

/// <summary>
/// Detects speech vs silence in a raw PCM audio stream (Voice Activity Detection).
/// Returns only the segments that contain speech, trimming leading and trailing
/// silence from the input stream.
/// </summary>
/// <remarks>
/// Implementations are expected to process 16-bit, 16 kHz mono PCM input as
/// defined by <see cref="AudioFormat.Pcm16Mono16k"/>. A VAD implementation
/// typically buffers audio, applies an energy or neural model threshold, and
/// yields complete utterances once end-of-speech silence is detected.
/// </remarks>
public interface IVoiceActivityDetector
{
    /// <summary>
    /// Processes an incoming audio stream and yields only the segments that
    /// contain speech. Each yielded <see cref="VadSegment"/> with
    /// <see cref="VadSegment.IsSpeech"/> set to <c>true</c> represents a
    /// complete utterance from speech onset to end-of-speech silence.
    /// </summary>
    /// <param name="audioStream">
    /// Raw PCM audio stream as produced by <see cref="IAudioCapture.CaptureAsync"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel detection. When cancelled the sequence completes
    /// without raising an exception.
    /// </param>
    /// <returns>
    /// An asynchronous sequence of <see cref="VadSegment"/> values. Only
    /// speech-containing chunks are yielded by production implementations;
    /// silence segments may optionally be yielded as markers with
    /// <see cref="VadSegment.IsSpeech"/> set to <c>false</c>.
    /// </returns>
    IAsyncEnumerable<VadSegment> DetectAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioStream,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single segment identified by a <see cref="IVoiceActivityDetector"/>.
/// </summary>
/// <param name="Audio">
/// The raw PCM audio bytes for this segment. Non-empty for speech segments;
/// may be empty for silence markers.
/// </param>
/// <param name="IsSpeech">
/// <c>true</c> when this segment contains detected speech and should be forwarded
/// to the transcriber. <c>false</c> for silence or noise markers — these are
/// informational only and callers should not send them to a transcriber.
/// </param>
public sealed record VadSegment(ReadOnlyMemory<byte> Audio, bool IsSpeech);
