namespace Bhengu.AI.Voice;

/// <summary>
/// Describes a PCM audio format expected or produced by voice components.
/// </summary>
/// <param name="SampleRate">Samples per second (e.g. 16000 for 16 kHz).</param>
/// <param name="Channels">Number of interleaved channels (1 = mono, 2 = stereo).</param>
/// <param name="BitsPerSample">Bit depth of each sample (e.g. 16 for signed 16-bit PCM).</param>
public sealed record AudioFormat(int SampleRate, int Channels, int BitsPerSample)
{
    /// <summary>
    /// Canonical input format expected by Butler / B! voice components:
    /// PCM signed 16-bit, mono, 16 kHz. Most open-source ASR engines
    /// (Sherpa-onnx, Vosk) accept this format directly.
    /// </summary>
    public static readonly AudioFormat Pcm16Mono16k = new(16_000, 1, 16);
}
