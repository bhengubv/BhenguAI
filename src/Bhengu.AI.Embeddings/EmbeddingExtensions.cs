using Bhengu.AI.Core;
using System.Threading.Tasks;

namespace Bhengu.AI.Embeddings
{
    public static class EmbeddingExtensions
    {
        public static BhenguEngine AddMiniLMEmbeddings(this BhenguEngine engine)
        {
            engine.EmbeddingService = new MiniLMEmbeddingService(engine.ModelLoader);
            return engine;
        }

        public static async Task<MiniLMEmbeddingService> UseMiniLMEmbeddings(this BhenguEngine engine)
        {
            if (engine.EmbeddingService is not MiniLMEmbeddingService service)
            {
                service = new MiniLMEmbeddingService(engine.ModelLoader);
                await service.InitializeAsync();
                engine.EmbeddingService = service;
            }
            return service;
        }
    }
}