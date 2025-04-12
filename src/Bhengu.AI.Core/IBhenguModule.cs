namespace Bhengu.AI.Core
{
    public interface IBhenguModule : IDisposable
    {
        string ModuleName { get; }
        Task InitAsync(BhenguEngine engine);
        bool IsModelLoaded { get; }
    }
}