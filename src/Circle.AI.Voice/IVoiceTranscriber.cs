namespace Circle.AI.Voice;

/// <summary>
/// Converts captured audio into text. Implementations are expected to consume
/// PCM 16-bit, 16 kHz mono input as defined by
/// <see cref="AudioFormat.Pcm16Mono16k"/> unless otherwise documented.
/// </summary>
public interface IVoiceTranscriber : IAsyncDisposable
{
    /// <summary>
    /// Transcribe a complete audio buffer (PCM 16-bit, 16 kHz mono).
    /// </summary>
    /// <param name="pcmAudio">
    /// The full audio buffer to transcribe. The buffer must be little-endian
    /// signed 16-bit PCM samples, mono, sampled at 16 kHz.
    /// </param>
    /// <param name="ct">Cancellation token used to abort transcription.</param>
    /// <returns>The recognised text, confidence, and detected language.</returns>
    Task<TranscriptionResult> TranscribeAsync(ReadOnlyMemory<byte> pcmAudio, CancellationToken ct = default);

    /// <summary>
    /// Stream audio chunks and receive partial transcriptions as the
    /// underlying engine produces them.
    /// </summary>
    /// <param name="audioChunks">
    /// Asynchronous sequence of PCM 16-bit, 16 kHz mono audio chunks. The
    /// sequence completes when the caller has no more audio to feed.
    /// </param>
    /// <param name="ct">Cancellation token used to abort streaming.</param>
    /// <returns>
    /// An asynchronous sequence of <see cref="PartialTranscription"/>
    /// instances. The final element will have <see cref="PartialTranscription.IsFinal"/>
    /// set to <c>true</c>.
    /// </returns>
    IAsyncEnumerable<PartialTranscription> StreamTranscribeAsync(IAsyncEnumerable<ReadOnlyMemory<byte>> audioChunks, CancellationToken ct = default);
}

/// <summary>
/// Final transcription result produced by <see cref="IVoiceTranscriber.TranscribeAsync"/>.
/// </summary>
/// <param name="Text">The recognised text. Empty string if nothing was recognised.</param>
/// <param name="Confidence">Engine-reported confidence in the range [0, 1].</param>
/// <param name="LanguageCode">
/// Detected language as a BCP-47 / ISO 639 code (e.g. "en", "zu", "und" for unknown).
/// </param>
public sealed record TranscriptionResult(string Text, float Confidence, string LanguageCode);

/// <summary>
/// Partial or final transcription produced during streaming recognition.
/// </summary>
/// <param name="Text">The recognised text so far.</param>
/// <param name="IsFinal">
/// <c>true</c> when this is the final transcription for the current utterance;
/// <c>false</c> for in-progress hypotheses that may still change.
/// </param>
/// <param name="Confidence">Engine-reported confidence in the range [0, 1].</param>
public sealed record PartialTranscription(string Text, bool IsFinal, float Confidence);
