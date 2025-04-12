using System;
using System.Threading.Tasks;

namespace Bhengu.AI.Core
{
    public class BhenguEngine
    {
        public EmbeddingService Embeddings { get; }
        public SearchService Search { get; }

        public BhenguEngine(IModelLoader modelLoader)
        {
            Embeddings = new EmbeddingService(modelLoader);
            Search = new SearchService(modelLoader);
        }
    }
}