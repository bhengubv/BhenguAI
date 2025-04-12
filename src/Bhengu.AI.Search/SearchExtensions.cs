using Bhengu.AI.Core;

namespace Bhengu.AI.Search
{
    public static class SearchExtensions
    {
        public static BhenguEngine AddSemanticSearch(this BhenguEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            engine.RegisterModule<SemanticSearchModule>(new SemanticSearchModule());
            return engine;
        }
    }
}