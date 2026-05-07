using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Core
{
    /// <summary>
    /// Abstraction for model file sources. Allows fallback chains for sanctions resilience
    /// (e.g. ModelScope primary, HuggingFace fallback).
    /// </summary>
    public interface IModelSource
    {
        /// <summary>
        /// Friendly name of the source (e.g. "ModelScope", "HuggingFace"). Used in logs.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Quick reachability check for this source. Implementations should perform a
        /// lightweight HEAD/GET probe and return false on any failure rather than throw.
        /// </summary>
        Task<bool> IsAvailableAsync(CancellationToken ct = default);

        /// <summary>
        /// Download a single file from the given URL to the local path. Implementations
        /// should support resume (Range requests) where possible and report progress.
        /// </summary>
        Task DownloadAsync(
            string url,
            string localPath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Snapshot of an in-flight download, suitable for UI/logging consumers.
    /// </summary>
    public sealed class DownloadProgress
    {
        public string FileName { get; init; } = "";
        public long BytesReceived { get; init; }
        public long TotalBytes { get; init; }
        public double BytesPerSecond { get; init; }
        public TimeSpan EstimatedTimeRemaining { get; init; }
    }
}
