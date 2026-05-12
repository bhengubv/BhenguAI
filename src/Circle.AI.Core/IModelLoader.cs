using System;
using System.Threading.Tasks;

namespace Circle.AI.Core
{
    public interface IModelLoader : IDisposable
    {
        Task<string> DownloadModelAsync(string modelName, IProgress<float>? progress = null);
        string GetModelPath(string modelName);
        bool ModelExists(string modelName);
        Task<bool> CheckForCriticalUpdateAsync();
    }
}