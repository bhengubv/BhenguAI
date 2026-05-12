// ServiceCollectionExtensions.cs
//
// DI surface for Circle AI services. Provides AddCircleAI entry points
// that register AIOptions, AIService (as both singleton and IAIService),
// IChatGenerator, and optional subsystems (RAG) based on the caller's
// configuration.
//
// AIOptions uses init-only setters, so callers construct it via object
// initializers and pass the finished instance (or a factory that builds one).

using System;
using Circle.AI.Core;
using Circle.AI.Inference;
using Circle.AI.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Circle.AI.Hosting;

/// <summary>
/// Extension methods for registering Circle AI services into a
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Circle AI services into the DI container using a factory
    /// that produces the <see cref="AIOptions"/> configuration.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="optionsFactory">
    /// Factory that returns a fully configured <see cref="AIOptions"/> instance.
    /// Called once when the DI container first resolves <see cref="AIOptions"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddCircleAI(() =&gt; new AIOptions
    /// {
    ///     ModelPath = "/path/to/qwen3-0.6b.gguf",
    ///     SystemPrompt = "You are a helpful assistant.",
    ///     ContextSize = 8192,
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddCircleAI(
        this IServiceCollection services,
        Func<AIOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        // ---------------------------------------------------------------
        // AIOptions — singleton, built by the caller's factory
        // ---------------------------------------------------------------
        services.AddSingleton(_ =>
        {
            var options = optionsFactory();
            return options ?? throw new InvalidOperationException(
                "AIOptions factory returned null.");
        });

        RegisterCoreServices(services);
        return services;
    }

    /// <summary>
    /// Registers Circle AI services into the DI container using a
    /// pre-built <see cref="AIOptions"/> instance.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="options">
    /// A fully configured <see cref="AIOptions"/> instance. Registered as a
    /// singleton directly.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddCircleAI(new AIOptions
    /// {
    ///     ModelPath = "/path/to/qwen3-0.6b.gguf",
    ///     SystemPrompt = "You are a helpful assistant.",
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddCircleAI(
        this IServiceCollection services,
        AIOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        RegisterCoreServices(services);
        return services;
    }

    /// <summary>
    /// Registers Circle AI services with default <see cref="AIOptions"/>.
    /// The caller must ensure <see cref="AIOptions.ModelId"/> resolves via a
    /// registered <see cref="IModelLoader"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddCircleAI(this IServiceCollection services)
        => services.AddCircleAI(new AIOptions());

    /// <summary>
    /// Registers Circle AI services with a specific model file path.
    /// All other <see cref="AIOptions"/> properties use their defaults.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="modelPath">Absolute path to a GGUF model file.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddCircleAI(
        this IServiceCollection services,
        string modelPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath);
        return services.AddCircleAI(new AIOptions { ModelPath = modelPath });
    }

    // ------------------------------------------------------------------
    // Private — shared service registrations
    // ------------------------------------------------------------------

    /// <summary>
    /// Registers all core services that depend on <see cref="AIOptions"/>
    /// being already registered in the container.
    /// </summary>
    private static void RegisterCoreServices(IServiceCollection services)
    {
        // ---------------------------------------------------------------
        // IChatGenerator — QwenTextGenerator backed by llama.cpp
        // ---------------------------------------------------------------
        services.AddSingleton<IChatGenerator>(sp =>
        {
            var opts = sp.GetRequiredService<AIOptions>();
            var modelPath = ResolveModelPath(opts, sp);

            return new QwenTextGenerator(
                modelPath,
                contextSize: (uint)Math.Max(1, opts.ContextSize),
                threads: opts.ThreadCount);
        });

        // ---------------------------------------------------------------
        // AIService — singleton, also exposed as IAIService
        // ---------------------------------------------------------------
        services.AddSingleton<AIService>(sp =>
        {
            var opts = sp.GetRequiredService<AIOptions>();
            var modelLoader = sp.GetService<IModelLoader>();
            var logger = sp.GetService<ILogger<AIService>>();

            // Provide a generator factory so AIService can lazy-load via DI.
            IChatGenerator GeneratorFactory(string path) =>
                new QwenTextGenerator(
                    path,
                    contextSize: (uint)Math.Max(1, opts.ContextSize),
                    threads: opts.ThreadCount);

            return new AIService(opts, modelLoader, GeneratorFactory, logger);
        });
        services.AddSingleton<IAIService>(sp => sp.GetRequiredService<AIService>());

        // ---------------------------------------------------------------
        // RagContextBuilder — always resolvable; uses episodic memory when
        // configured, falls back to an empty in-memory store otherwise.
        // ---------------------------------------------------------------
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<AIOptions>();

            // Caller-supplied builder takes precedence.
            if (opts.RagBuilder is not null)
                return opts.RagBuilder;

            // Build from episodic store if present.
            if (opts.EpisodicMemory is not null)
                return new RagContextBuilder(opts.EpisodicMemory, embedder: null, topK: opts.RagTopK);

            // Fallback: in-memory store so RagContextBuilder is always resolvable.
            return new RagContextBuilder(new InMemoryEpisodicStore());
        });
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolves the model path from <see cref="AIOptions.ModelPath"/> (explicit)
    /// or falls back to <see cref="IModelLoader.GetModelPath"/> using
    /// <see cref="AIOptions.ModelId"/>. Returns a non-null path or throws.
    /// </summary>
    private static string ResolveModelPath(AIOptions opts, IServiceProvider sp)
    {
        // Explicit path takes precedence.
        if (!string.IsNullOrWhiteSpace(opts.ModelPath))
            return opts.ModelPath!;

        // Fall back to the model loader (if registered).
        var loader = sp.GetService<IModelLoader>();
        if (loader is not null)
        {
            var path = loader.GetModelPath(opts.ModelId);
            if (!string.IsNullOrEmpty(path))
                return path;
        }

        throw new InvalidOperationException(
            $"Cannot resolve model path. Set AIOptions.ModelPath explicitly or " +
            $"register an IModelLoader that knows model '{opts.ModelId}'.");
    }
}
