using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Embeddings
{
    public interface ITextEmbedder
    {
        Task<float[]> GenerateAsync(string text, CancellationToken ct = default);
    }
}