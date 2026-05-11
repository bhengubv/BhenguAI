#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Inference;

/// <summary>
/// Downloads and manages GGUF model files stored on disk.
/// </summary>
public interface IModelDownloadService
{
    /// <summary>
    /// Ensures a GGUF model is present and valid on disk.
    /// If the file already exists and its SHA-256 matches <paramref name="expectedSha256"/>,
    /// the existing path is returned immediately. Otherwise the file is (re-)downloaded,
    /// verified, and the absolute path is returned.
    /// </summary>
    /// <param name="modelId">Logical identifier for the model (used as filename stem).</param>
    /// <param name="downloadUri">Where to download the GGUF file from.</param>
    /// <param name="expectedSha256">
    /// Optional hex-encoded SHA-256 digest. When provided the file is verified after download;
    /// a mismatch throws <see cref="InvalidOperationException"/> and the partial file is deleted.
    /// </param>
    /// <param name="progress">Optional callback receiving 0–1 download progress.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute path to the cached GGUF file.</returns>
    Task<string> EnsureModelAsync(
        string modelId,
        Uri downloadUri,
        string? expectedSha256,
        IProgress<double>? progress,
        CancellationToken ct);

    /// <summary>
    /// Returns <see langword="true"/> if the model file exists on disk.
    /// Does <em>not</em> verify the SHA-256 digest.
    /// </summary>
    Task<bool> IsModelCachedAsync(string modelId, CancellationToken ct);

    /// <summary>
    /// Deletes the model file if it exists. No-op when the file is absent.
    /// </summary>
    Task DeleteModelAsync(string modelId, CancellationToken ct);

    /// <summary>
    /// Returns the number of free bytes available on the drive that hosts the
    /// storage directory.
    /// </summary>
    ValueTask<long> GetAvailableDiskSpaceBytesAsync(CancellationToken ct);
}
