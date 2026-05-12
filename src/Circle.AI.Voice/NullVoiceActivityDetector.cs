using System.Runtime.CompilerServices;

namespace Circle.AI.Voice;

/// <summary>
/// No-op <see cref="IVoiceActivityDetector"/> implementation that passes all
/// audio chunks through as speech segments without any analysis. Used as a
/// safe default when no real VAD backend has been wired and in tests where
/// unconditional pass-through is required.
/// </summary>
/// <remarks>
/// Every chunk received from the upstream audio stream is re-emitted as a
/// <see cref="VadSegment"/> with <see cref="VadSegment.IsSpeech"/> set to
/// <c>true</c>. This means the transcriber will receive all audio, including
/// silence — which is the safest behaviour for a null implementation.
/// </remarks>
public sealed class NullVoiceActivityDetector : IVoiceActivityDetector
{
    /// <inheritdoc />
    /// <remarks>
    /// Passes every chunk through as <c>new VadSegment(chunk, true)</c>.
    /// No buffering, energy analysis, or neural model is applied.
    /// </remarks>
    public async IAsyncEnumerable<VadSegment> DetectAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioStream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioStream);

        await foreach (var chunk in audioStream
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return new VadSegment(chunk, true);
        }
    }
}
