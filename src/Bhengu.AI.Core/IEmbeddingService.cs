namespace Bhengu.AI.Core
{
    public interface IEmbeddingService : IBhenguModule
    {
        float[] GenerateEmbedding(string text);
        int EmbeddingSize { get; }
    }
}