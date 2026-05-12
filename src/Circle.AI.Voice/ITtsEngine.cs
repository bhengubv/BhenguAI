namespace Circle.AI.Voice;

/// <summary>
/// Text-to-speech engine that converts generated text into PCM audio.
/// Implementations synthesise audio using an on-device or cloud TTS backend.
/// </summary>
public interface ITtsEngine
{
    /// <summary>
    /// Synthesise <paramref name="text"/> to a single PCM audio buffer.
    /// </summary>
    /// <param name="text">The text to convert to speech.</param>
    /// <param name="cancellationToken">Token used to cancel the synthesis.</param>
    /// <returns>
    /// A <see cref="TtsSynthesisResult"/> containing the full audio buffer
    /// and its PCM format metadata.
    /// </returns>
    Task<TtsSynthesisResult> SynthesiseAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream PCM audio chunks as they are synthesised, enabling low-latency
    /// playback that begins before the full sentence is complete.
    /// </summary>
    /// <param name="text">The text to convert to speech.</param>
    /// <param name="cancellationToken">Token used to cancel streaming.</param>
    /// <returns>
    /// An asynchronous sequence of raw PCM chunks. Each chunk shares the same
    /// sample rate, channel count, and bit depth as reported by the engine.
    /// </returns>
    IAsyncEnumerable<ReadOnlyMemory<byte>> StreamSynthesiseAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a single-shot TTS synthesis operation.
/// </summary>
/// <param name="AudioData">
/// The complete PCM audio buffer. Empty when the engine produced no audio
/// (e.g. empty input text or null implementation).
/// </param>
/// <param name="SampleRate">
/// Samples per second (e.g. 24000 for 24 kHz).
/// </param>
/// <param name="Channels">
/// Number of interleaved audio channels (1 = mono, 2 = stereo).
/// </param>
/// <param name="BitsPerSample">
/// Bit depth of each sample (e.g. 16 for signed 16-bit PCM).
/// </param>
public sealed record TtsSynthesisResult(
    ReadOnlyMemory<byte> AudioData,
    int SampleRate,
    int Channels,
    int BitsPerSample);
