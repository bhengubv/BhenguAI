namespace Circle.AI.Hosting;

/// <summary>
/// Configuration for the B! voice pipeline, composed via
/// <see cref="AIOptions.Voice"/>. All properties have safe defaults
/// that produce a voice-disabled, silent-TTS pipeline when left unchanged.
/// </summary>
public sealed class VoiceOptions
{
    /// <summary>
    /// Wake word (phrase) that triggers the voice pipeline.
    /// Default <c>"hey b"</c> — the canonical B! wake word.
    /// </summary>
    public string WakeWord { get; set; } = "hey b";

    /// <summary>
    /// Target sample rate for microphone capture, in Hz.
    /// Default <c>16000</c> (16 kHz) — the format required by most open-source
    /// ASR engines (Sherpa-onnx, Vosk) and by the canonical
    /// <see cref="Circle.AI.Voice.AudioFormat.Pcm16Mono16k"/> format.
    /// </summary>
    public int SampleRateHz { get; set; } = 16_000;

    /// <summary>
    /// When <c>true</c>, the voice pipeline starts automatically alongside the
    /// butler service. When <c>false</c> (default), callers must start the
    /// pipeline manually via <c>VoicePipeline.StartAsync</c>.
    /// </summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// Selects the TTS engine backend to use for spoken responses.
    /// <list type="bullet">
    ///   <item><term><c>"null"</c></term><description>Silent mode — no audio is synthesised (default).</description></item>
    ///   <item><term><c>"kokoro"</c></term><description>Kokoro on-device neural TTS engine.</description></item>
    ///   <item><term><c>"piper"</c></term><description>Piper on-device TTS engine.</description></item>
    /// </list>
    /// </summary>
    public string TtsBackend { get; set; } = "null";

    /// <summary>
    /// Duration of trailing silence (in milliseconds) that marks the end of an
    /// utterance for voice activity detection purposes. Default <c>800</c> ms.
    /// Lower values make the detector more responsive; higher values reduce
    /// false end-of-utterance triggers in noisy environments.
    /// </summary>
    public int EndOfSpeechSilenceMs { get; set; } = 800;
}
