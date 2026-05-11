#nullable enable

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Inference;

/// <summary>
/// Default implementation of <see cref="IModelDownloadService"/>.
/// Models are stored as <c>{storageDirectory}/{modelId}.gguf</c>.
/// </summary>
public sealed class ModelDownloadService : IModelDownloadService
{
    private const int ProgressChunkBytes = 1 * 1024 * 1024; // 1 MB

    private readonly string _storageDirectory;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a new instance using an internally managed <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="storageDirectory">
    /// Root directory where GGUF files are cached. Created if it does not exist.
    /// </param>
    public ModelDownloadService(string storageDirectory)
        : this(storageDirectory, new HttpClient(), ownsHttpClient: true) { }

    /// <summary>
    /// Creates a new instance using a caller-supplied <see cref="HttpClient"/>.
    /// The caller retains ownership and is responsible for disposing it.
    /// </summary>
    public ModelDownloadService(string storageDirectory, HttpClient httpClient)
        : this(storageDirectory, httpClient, ownsHttpClient: false) { }

    private ModelDownloadService(string storageDirectory, HttpClient httpClient, bool ownsHttpClient)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("Storage directory must not be empty.", nameof(storageDirectory));

        _storageDirectory = storageDirectory;
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;

        Directory.CreateDirectory(_storageDirectory);
    }

    /// <inheritdoc/>
    public async Task<string> EnsureModelAsync(
        string modelId,
        Uri downloadUri,
        string? expectedSha256,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        ValidateModelId(modelId);
        ArgumentNullException.ThrowIfNull(downloadUri);

        var filePath = GetFilePath(modelId);

        // Fast path: file exists and hash matches.
        if (File.Exists(filePath) && expectedSha256 is not null)
        {
            if (await VerifySha256Async(filePath, expectedSha256, ct).ConfigureAwait(false))
            {
                progress?.Report(1.0);
                return filePath;
            }
            // Hash mismatch — delete stale file and re-download.
            File.Delete(filePath);
        }
        else if (File.Exists(filePath) && expectedSha256 is null)
        {
            // No hash supplied — trust the existing file.
            progress?.Report(1.0);
            return filePath;
        }

        // Download the file.
        var tempPath = filePath + ".tmp";
        try
        {
            await DownloadToFileAsync(downloadUri, tempPath, progress, ct).ConfigureAwait(false);

            // Verify SHA-256 when a digest was supplied.
            if (expectedSha256 is not null)
            {
                bool valid = await VerifySha256Async(tempPath, expectedSha256, ct).ConfigureAwait(false);
                if (!valid)
                {
                    File.Delete(tempPath);
                    throw new InvalidOperationException(
                        $"SHA-256 mismatch for model '{modelId}'. " +
                        "The downloaded file has been deleted.");
                }
            }

            // Atomically promote temp file.
            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath);
        }
        catch
        {
            // Clean up partial downloads on any failure.
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }

        return filePath;
    }

    /// <inheritdoc/>
    public Task<bool> IsModelCachedAsync(string modelId, CancellationToken ct)
    {
        ValidateModelId(modelId);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetFilePath(modelId)));
    }

    /// <inheritdoc/>
    public Task DeleteModelAsync(string modelId, CancellationToken ct)
    {
        ValidateModelId(modelId);
        ct.ThrowIfCancellationRequested();

        var filePath = GetFilePath(modelId);
        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<long> GetAvailableDiskSpaceBytesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Resolve the absolute path so DriveInfo works correctly.
        var absoluteDir = Path.GetFullPath(_storageDirectory);
        var root = Path.GetPathRoot(absoluteDir)
            ?? throw new InvalidOperationException(
                $"Cannot determine drive root for '{absoluteDir}'.");

        var drive = new DriveInfo(root);
        return ValueTask.FromResult(drive.AvailableFreeSpace);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetFilePath(string modelId) =>
        Path.Combine(_storageDirectory, $"{modelId}.gguf");

    private static void ValidateModelId(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model ID must not be empty.", nameof(modelId));
    }

    private async Task DownloadToFileAsync(
        Uri uri,
        string destPath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        using var response = await _http
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;

        await using var contentStream = await response.Content
            .ReadAsStreamAsync(ct)
            .ConfigureAwait(false);

        await using var fileStream = new FileStream(
            destPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81_920, useAsync: true);

        var buffer = new byte[81_920];
        long bytesRead = 0L;
        long bytesUntilNextReport = ProgressChunkBytes;
        int read;

        while ((read = await contentStream
                   .ReadAsync(buffer, ct)
                   .ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            bytesRead += read;
            bytesUntilNextReport -= read;

            if (progress is not null && bytesUntilNextReport <= 0)
            {
                var ratio = totalBytes > 0 ? (double)bytesRead / totalBytes : 0.0;
                progress.Report(Math.Min(ratio, 0.999)); // Reserve 1.0 for completion.
                bytesUntilNextReport = ProgressChunkBytes;
            }
        }

        progress?.Report(1.0);
    }

    private static async Task<bool> VerifySha256Async(
        string filePath,
        string expectedHex,
        CancellationToken ct)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81_920, useAsync: true);

        var actualHash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        var actualHex = Convert.ToHexString(actualHash);

        return string.Equals(actualHex, expectedHex.ToUpperInvariant(),
            StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
