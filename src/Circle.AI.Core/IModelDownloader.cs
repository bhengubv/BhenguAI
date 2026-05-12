using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Core
{
    /// <summary>
    /// Downloads a model file (or set of files) to local storage.
    /// Implementations are expected to walk a chain of <see cref="IModelSource"/> instances
    /// so that, e.g., ModelScope can be tried first and HuggingFace second.
    /// </summary>
    public interface IModelDownloader
    {
        /// <summary>
        /// Original entry point: download a model identified by <paramref name="modelId"/>
        /// to <paramref name="localPath"/>. Implementations resolve the URL set internally.
        /// </summary>
        Task DownloadModelAsync(string modelId, string localPath, CancellationToken ct = default);

        /// <summary>
        /// Download a single model file by trying each candidate URL in order. The first
        /// URL is treated as the primary; subsequent URLs are fallbacks. Returns the name
        /// of the source that succeeded.
        /// </summary>
        Task<string> DownloadFromCandidatesAsync(
            IReadOnlyList<string> candidateUrls,
            string localFilePath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken ct = default);
    }
}
