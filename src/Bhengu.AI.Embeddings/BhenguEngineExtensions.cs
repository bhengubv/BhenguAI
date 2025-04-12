using Bhengu.AI.Core;
using Bhengu.AI.Embeddings;
using Microsoft.Extensions.DependencyInjection;

public static class BhenguEngineExtensions
{
    public static IServiceCollection AddEmbeddingService(this IServiceCollection services)
    {
        services.AddSingleton<MiniLMEmbeddingService>();
        return services;
    }

    public static BhenguEngine WithEmbeddingService(this BhenguEngine engine)
    {
        engine.EmbeddingService = new MiniLMEmbeddingService(engine.ModelLoader);
        return engine;
    }
}