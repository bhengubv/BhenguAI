using System;
using System.Collections.Generic;

namespace Bhengu.AI.Core
{
    /// <summary>
    /// Top-level facade for the BhenguAI on-device stack. Holds the
    /// <see cref="IModelLoader"/> and a small registry of attached modules
    /// (embeddings, search, chat generators, tool bridges) wired in from
    /// downstream assemblies via extension methods.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bhengu.AI.Core deliberately knows nothing about Bhengu.AI.Inference,
    /// Bhengu.AI.Embeddings, Bhengu.AI.Search, or Bhengu.AI.Tools. Those
    /// assemblies attach their own services through the
    /// <see cref="RegisterModule{T}"/> / <see cref="GetModule{T}"/> pair, or
    /// through dedicated settable properties such as
    /// <see cref="EmbeddingService"/>.
    /// </para>
    /// <para>
    /// The shape is intentionally lean: a constructor that takes an
    /// <see cref="IModelLoader"/>, a public <see cref="ModelLoader"/>, and a
    /// type-keyed module bag. Concerns like chat generation
    /// (<c>Bhengu.AI.Inference.IChatGenerator</c>) and tool invocation
    /// (<c>Bhengu.AI.Tools.IToolBridge</c>) are not referenced from Core;
    /// callers register them with <see cref="RegisterModule{T}"/> and pull
    /// them back out with <see cref="GetModule{T}"/>.
    /// </para>
    /// </remarks>
    public sealed class BhenguEngine
    {
        private readonly Dictionary<Type, object> _modules = new();

        /// <summary>
        /// The model loader used to acquire and cache model files from
        /// ModelScope (primary) or HuggingFace (fallback).
        /// </summary>
        public IModelLoader ModelLoader { get; }

        /// <summary>
        /// Optional embedding service. Wired in by
        /// <c>Bhengu.AI.Embeddings.BhenguEngineExtensions.WithEmbeddingService</c>.
        /// Kept as a settable <see cref="object"/> so that Core does not need
        /// to reference downstream embedding implementations.
        /// </summary>
        public object? EmbeddingService { get; set; }

        public BhenguEngine(IModelLoader modelLoader)
        {
            ModelLoader = modelLoader ?? throw new ArgumentNullException(nameof(modelLoader));
        }

        /// <summary>
        /// Register a module instance keyed by its concrete or interface type.
        /// </summary>
        public BhenguEngine RegisterModule<T>(T module) where T : notnull
        {
            if (module is null) throw new ArgumentNullException(nameof(module));
            _modules[typeof(T)] = module;
            return this;
        }

        /// <summary>
        /// Retrieve a previously registered module, or <c>null</c> if none was
        /// registered for that type.
        /// </summary>
        public T? GetModule<T>() where T : class
        {
            return _modules.TryGetValue(typeof(T), out var value) ? value as T : null;
        }

        /// <summary>
        /// Returns true if a module of the given type has been registered.
        /// </summary>
        public bool HasModule<T>() where T : notnull
        {
            return _modules.ContainsKey(typeof(T));
        }
    }
}
