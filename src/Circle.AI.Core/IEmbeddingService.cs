namespace Circle.AI.Core
{
    public interface IEmbeddingService : IBhenguModule
    {
        float[] GenerateEmbedding(string text);
        int EmbeddingSize { get; }
    }
}