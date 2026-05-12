public interface IModelManager : IDisposable
{
    Task<string> GetModelPathAsync(string modelId, CancellationToken ct = default);
    Task<bool> VerifyModelAsync(string modelPath, byte[] expectedChecksum, CancellationToken ct = default);
}