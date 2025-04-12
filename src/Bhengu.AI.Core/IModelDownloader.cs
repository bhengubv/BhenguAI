using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Core
{
    public interface IModelDownloader
    {
        Task DownloadModelAsync(string modelId, string localPath, CancellationToken ct = default);
    }
}