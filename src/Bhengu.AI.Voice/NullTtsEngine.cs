using System.Runtime.CompilerServices;

namespace Bhengu.AI.Voice;

/// <summary>
/// No-op <see cref="ITtsEngine"/> implementation. Returns empty audio and
/// yields nothing. Used as a safe default when no real TTS backend has been
/// wired (silent mode) or during testing.
/// </summary>
public sealed class NullTtsEngine : ITtsEngine
{
    /// <summary>
    /// The PCM format that would be used by a real engine: 24 kHz, mono, 16-bit.
    /// Consumers that inspect the metadata of an empty synthesis result can
    /// rely on these values being stable.
    /// </summary>
    public static readonly TtsSynthesisResult EmptyResult =
        new(ReadOnlyMemory<byte>.Empty, 24_000, 1, 16);

    /// <inheritdoc />
    /// <remarks>
    /// Always returns an empty audio buffer with sample rate 24000 Hz, mono,
    /// 16-bit — the canonical format for Kokoro / Piper output. No audio is
    /// synthesised.
    /// </remarks>
    public Task<TtsSynthesisResult> SynthesiseAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EmptyResult);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Yields nothing. The returned sequence completes immediately with no
    /// audio chunks.
    /// </remarks>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> StreamSynthesiseAsync(
        string text,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
