using System.Runtime.CompilerServices;

namespace Bhengu.AI.Voice;

/// <summary>
/// Energy-based <see cref="IVoiceActivityDetector"/> that uses RMS (Root Mean
/// Square) energy to distinguish speech from silence. Pure managed code with
/// no external dependencies.
/// </summary>
/// <remarks>
/// <para>
/// The detector processes incoming audio in fixed-size frames. When the RMS
/// energy of a frame exceeds <see cref="EnergyThreshold"/>, the frame is
/// considered speech. Speech frames are buffered until a configurable number
/// of consecutive below-threshold frames (<see cref="SilenceFrameCount"/>)
/// are observed, at which point the buffered speech segment is yielded.
/// </para>
/// <para>
/// Expected input format: PCM 16-bit, 16 kHz, mono (little-endian signed
/// shorts) as specified by <see cref="AudioFormat.Pcm16Mono16k"/>.
/// </para>
/// </remarks>
public sealed class EnergyVadDetector : IVoiceActivityDetector
{
    /// <summary>
    /// RMS energy threshold in the range [0, 1]. Frames with RMS above this
    /// value are classified as speech.
    /// </summary>
    public float EnergyThreshold { get; }

    /// <summary>
    /// Number of consecutive below-threshold frames required to declare
    /// end-of-speech and emit the buffered segment.
    /// </summary>
    public int SilenceFrameCount { get; }

    /// <summary>
    /// Size of each analysis frame in bytes. At 16 kHz / 16-bit / mono,
    /// 640 bytes = 20 ms.
    /// </summary>
    public int FrameSizeBytes { get; }

    /// <summary>
    /// Initialise a new energy-based voice activity detector.
    /// </summary>
    /// <param name="energyThreshold">
    /// RMS energy threshold in [0, 1]. Default <c>0.02f</c> works well for
    /// typical close-talking microphones.
    /// </param>
    /// <param name="silenceFrames">
    /// How many consecutive below-threshold frames constitute end-of-speech.
    /// Default <c>15</c> frames = 300 ms at 20 ms/frame.
    /// </param>
    /// <param name="frameSizeBytes">
    /// Size of each analysis frame in bytes. Default <c>640</c> bytes = 20 ms
    /// at 16 kHz mono 16-bit.
    /// </param>
    public EnergyVadDetector(
        float energyThreshold = 0.02f,
        int silenceFrames = 15,
        int frameSizeBytes = 640)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(silenceFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(energyThreshold);

        EnergyThreshold = energyThreshold;
        SilenceFrameCount = silenceFrames;
        FrameSizeBytes = frameSizeBytes;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Iterates the incoming audio stream frame-by-frame, computes RMS
    /// energy, and yields complete speech segments when end-of-speech silence
    /// is detected. A final partial segment is emitted when the stream ends
    /// mid-speech.
    /// </remarks>
    public async IAsyncEnumerable<VadSegment> DetectAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioStream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioStream);

        // Carry-over buffer for bytes that don't fill a complete frame.
        using var residual = new MemoryStream();
        // Accumulator for the current speech segment.
        using var speechBuffer = new MemoryStream();

        bool inSpeech = false;
        int consecutiveSilenceFrames = 0;

        await foreach (var chunk in audioStream
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            if (chunk.Length == 0) continue;

            // Append new data to the residual buffer.
            residual.Write(chunk.Span);

            // Process complete frames from the residual.
            var residualBuffer = residual.GetBuffer();
            long available = residual.Position; // total bytes written
            long offset = 0;

            while (available - offset >= FrameSizeBytes)
            {
                var frame = new ReadOnlySpan<byte>(residualBuffer, (int)offset, FrameSizeBytes);
                float rms = ComputeRmsEnergy(frame);
                bool isSpeechFrame = rms >= EnergyThreshold;

                if (isSpeechFrame)
                {
                    if (!inSpeech)
                    {
                        inSpeech = true;
                        consecutiveSilenceFrames = 0;
                        speechBuffer.SetLength(0);
                    }
                    else
                    {
                        consecutiveSilenceFrames = 0;
                    }

                    speechBuffer.Write(frame);
                }
                else if (inSpeech)
                {
                    // Still in speech region; buffer silence frames in case
                    // speech resumes (avoids cutting off mid-word).
                    speechBuffer.Write(frame);
                    consecutiveSilenceFrames++;

                    if (consecutiveSilenceFrames >= SilenceFrameCount)
                    {
                        // End of speech — emit the buffered segment.
                        inSpeech = false;
                        consecutiveSilenceFrames = 0;
                        var audio = speechBuffer.ToArray();
                        speechBuffer.SetLength(0);
                        yield return new VadSegment(audio, IsSpeech: true);
                    }
                }
                // else: silence while not in speech — discard.

                offset += FrameSizeBytes;
            }

            // Move unconsumed residual bytes to the start of the buffer.
            long remaining = available - offset;
            if (remaining > 0)
            {
                Buffer.BlockCopy(residualBuffer, (int)offset, residualBuffer, 0, (int)remaining);
            }
            residual.Position = remaining;
            residual.SetLength(remaining);
        }

        // Stream ended — if we were mid-speech, emit what we have.
        if (inSpeech && speechBuffer.Length > 0)
        {
            yield return new VadSegment(speechBuffer.ToArray(), IsSpeech: true);
        }
    }

    /// <summary>
    /// Compute the Root Mean Square energy of a PCM 16-bit frame, normalised
    /// to the range [0, 1].
    /// </summary>
    private static float ComputeRmsEnergy(ReadOnlySpan<byte> frameBytes)
    {
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(frameBytes);
        if (samples.Length == 0) return 0f;

        double sumSquares = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double normalised = samples[i] / 32768.0;
            sumSquares += normalised * normalised;
        }

        return (float)Math.Sqrt(sumSquares / samples.Length);
    }
}
