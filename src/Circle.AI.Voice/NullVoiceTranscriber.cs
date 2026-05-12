using System.Runtime.CompilerServices;

namespace Circle.AI.Voice;

/// <summary>
/// No-op <see cref="IVoiceTranscriber"/> implementation. Returns empty
/// results without consuming audio. Used as a safe default when no real
/// transcriber has been wired.
/// </summary>
public sealed class NullVoiceTranscriber : IVoiceTranscriber
{
    private bool _disposed;

    /// <inheritdoc />
    public Task<TranscriptionResult> TranscribeAsync(ReadOnlyMemory<byte> pcmAudio, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new TranscriptionResult(string.Empty, 0f, "und"));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PartialTranscription> StreamTranscribeAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioChunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(audioChunks);

        // Drain the input so callers' producers are not blocked, but emit nothing.
        await foreach (var _ in audioChunks.WithCancellation(ct).ConfigureAwait(false))
        {
            // discard
        }

        yield break;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
