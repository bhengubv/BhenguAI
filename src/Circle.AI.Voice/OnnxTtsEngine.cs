using System.Runtime.CompilerServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Circle.AI.Voice;

/// <summary>
/// <see cref="ITtsEngine"/> implementation that uses an ONNX Runtime session to
/// run a VITS/Kokoro-style text-to-speech model. The model is expected to accept
/// text token IDs as input and produce a float waveform as output.
/// </summary>
/// <remarks>
/// <para>
/// The ONNX session is lazy-loaded on first synthesis and reused across calls.
/// Thread safety is provided via <see cref="Lock"/> — ONNX Runtime sessions
/// support concurrent <c>Run</c> calls, but we serialise to avoid contention
/// on the tokenisation and output-conversion code.
/// </para>
/// <para>
/// Tokenisation uses a character-level fallback: each character in the input
/// text is mapped to its Unicode code point. Real production models may require
/// a phonemizer or a model-specific vocabulary; subclass or replace the
/// <see cref="TokeniseText"/> method as needed.
/// </para>
/// <para>
/// The output waveform (float[] in [-1, 1]) is converted to 16-bit signed PCM
/// at the configured sample rate.
/// </para>
/// </remarks>
public sealed class OnnxTtsEngine : ITtsEngine, IDisposable
{
    private readonly string _modelPath;
    private readonly int _sampleRate;
    private readonly Lock _gate = new();
    private InferenceSession? _session;
    private bool _disposed;

    /// <summary>
    /// The name of the ONNX model input that receives token IDs.
    /// Standard for VITS-style models.
    /// </summary>
    private const string InputName = "input";

    /// <summary>
    /// The name of the ONNX model input that receives input lengths.
    /// Required by VITS-style models for batched inference.
    /// </summary>
    private const string InputLengthsName = "input_lengths";

    /// <summary>
    /// The name of the ONNX model input that receives scales
    /// (noise_scale, length_scale, noise_scale_w).
    /// </summary>
    private const string ScalesName = "scales";

    /// <summary>
    /// The name of the ONNX model output that contains the waveform.
    /// </summary>
    private const string OutputName = "output";

    /// <summary>
    /// Initialise a new ONNX-based TTS engine.
    /// </summary>
    /// <param name="modelPath">
    /// Absolute path to the ONNX model file.
    /// </param>
    /// <param name="sampleRate">
    /// Output sample rate in Hz. Must match the model's training sample rate.
    /// Default is 24000 (common for Kokoro/VITS models).
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="modelPath"/> is null or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="sampleRate"/> is not positive.
    /// </exception>
    public OnnxTtsEngine(string modelPath, int sampleRate = 24_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        _modelPath = modelPath;
        _sampleRate = sampleRate;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tokenises the input text, runs ONNX inference, converts the output
    /// waveform to 16-bit PCM, and returns the full audio buffer.
    /// </remarks>
    public Task<TtsSynthesisResult> SynthesiseAsync(
        string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new TtsSynthesisResult(
                ReadOnlyMemory<byte>.Empty, _sampleRate, 1, 16));
        }

        return Task.Run(() => SynthesiseCore(text, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Splits the input text on sentence boundaries and synthesises each
    /// sentence independently, yielding PCM chunks as they become available.
    /// This enables low-latency playback that begins before the full text
    /// has been processed.
    /// </remarks>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> StreamSynthesiseAsync(
        string text,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var sentences = SplitSentences(text);

        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sentence))
                continue;

            var result = await Task.Run(
                () => SynthesiseCore(sentence.Trim(), cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (result.AudioData.Length > 0)
            {
                yield return result.AudioData;
            }
        }
    }

    /// <summary>
    /// Release the ONNX session and associated native resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Ensure the ONNX inference session is loaded. Thread-safe via
    /// <see cref="_gate"/>.
    /// </summary>
    private InferenceSession EnsureSession()
    {
        if (_session is not null) return _session;

        lock (_gate)
        {
            if (_session is not null) return _session;

            if (!File.Exists(_modelPath))
            {
                throw new InvalidOperationException(
                    $"ONNX TTS model not found at '{_modelPath}'. " +
                    "Provide a valid VITS/Kokoro ONNX model file.");
            }

            var opts = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2)
            };

            _session = new InferenceSession(_modelPath, opts);
            return _session;
        }
    }

    /// <summary>
    /// Core synthesis: tokenise -> infer -> convert waveform to PCM16.
    /// </summary>
    private TtsSynthesisResult SynthesiseCore(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var session = EnsureSession();
        var tokens = TokeniseText(text);

        if (tokens.Length == 0)
        {
            return new TtsSynthesisResult(ReadOnlyMemory<byte>.Empty, _sampleRate, 1, 16);
        }

        // Build input tensors.
        // Input: token IDs as int64 [1, sequence_length]
        var inputTensor = new DenseTensor<long>(new[] { 1, tokens.Length });
        for (int i = 0; i < tokens.Length; i++)
        {
            inputTensor[0, i] = tokens[i];
        }

        // Input lengths: [1] containing the sequence length
        var inputLengths = new DenseTensor<long>(new[] { 1 });
        inputLengths[0] = tokens.Length;

        // Scales: [3] — noise_scale, length_scale, noise_scale_w
        var scales = new DenseTensor<float>(new[] { 3 });
        scales[0] = 0.667f;  // noise_scale — controls expressiveness
        scales[1] = 1.0f;    // length_scale — controls speed
        scales[2] = 0.8f;    // noise_scale_w — controls phoneme duration variation

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputName, inputTensor),
            NamedOnnxValue.CreateFromTensor(InputLengthsName, inputLengths),
            NamedOnnxValue.CreateFromTensor(ScalesName, scales)
        };

        // Run inference.
        float[] waveform;
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();

            using var results = session.Run(inputs);
            var outputTensor = results.First();
            var outputData = outputTensor.AsTensor<float>();

            waveform = new float[outputData.Length];
            int idx = 0;
            foreach (var sample in outputData)
            {
                waveform[idx++] = sample;
            }
        }

        // Convert float waveform [-1, 1] to 16-bit signed PCM.
        var pcmBytes = FloatWaveformToPcm16(waveform);

        return new TtsSynthesisResult(pcmBytes, _sampleRate, 1, 16);
    }

    /// <summary>
    /// Character-level tokenisation fallback. Maps each character to its
    /// Unicode code point. Real production models typically require a
    /// phonemizer or model-specific vocabulary lookup.
    /// </summary>
    /// <remarks>
    /// The first token is a BOS (beginning of sequence) marker (0) and the
    /// last is an EOS (end of sequence) marker (0). Blank tokens (0) are
    /// inserted between each character for VITS-style models that expect
    /// interleaved blanks.
    /// </remarks>
    internal static long[] TokeniseText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        // VITS models expect: [BOS, blank, char, blank, char, ..., blank, EOS]
        // where blank = 0, BOS = 0, EOS = 0, and char tokens start at some offset.
        // We use a simple mapping: char code point + 1 (to avoid collision with blank).
        var result = new List<long>(text.Length * 2 + 2) { 0 }; // BOS / blank

        foreach (char c in text)
        {
            result.Add(c + 1); // character token
            result.Add(0);      // inter-character blank
        }

        return result.ToArray();
    }

    /// <summary>
    /// Convert a float waveform (values in [-1, 1]) to 16-bit signed PCM
    /// as a byte array (little-endian).
    /// </summary>
    private static byte[] FloatWaveformToPcm16(ReadOnlySpan<float> waveform)
    {
        var pcm = new byte[waveform.Length * 2];
        for (int i = 0; i < waveform.Length; i++)
        {
            // Clamp to [-1, 1] then scale to short range.
            float sample = Math.Clamp(waveform[i], -1f, 1f);
            short value = (short)(sample * 32767f);
            // Little-endian: low byte first.
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return pcm;
    }

    /// <summary>
    /// Split text into sentences on common sentence-ending punctuation.
    /// Preserves the delimiter at the end of each chunk.
    /// </summary>
    private static List<string> SplitSentences(string text)
    {
        var sentences = new List<string>();
        int start = 0;
        char[] delimiters = ['.', '!', '?', ';'];

        for (int i = 0; i < text.Length; i++)
        {
            if (Array.IndexOf(delimiters, text[i]) >= 0)
            {
                // Include the delimiter in the sentence.
                var sentence = text[start..(i + 1)];
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    sentences.Add(sentence);
                }
                start = i + 1;
            }
        }

        // Remainder after the last delimiter.
        if (start < text.Length)
        {
            var remainder = text[start..];
            if (!string.IsNullOrWhiteSpace(remainder))
            {
                sentences.Add(remainder);
            }
        }

        return sentences;
    }
}
