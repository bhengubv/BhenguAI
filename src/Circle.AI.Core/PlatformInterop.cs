// PlatformInterop.cs
//
// Thin shim that loads a native llama.cpp model via P/Invoke and returns it
// wrapped in a SafeModelHandle. All previous TFLite (Android) and CoreML
// (iOS) branches have been removed — llama.cpp covers every supported
// platform via a single DllImport with platform-aware library naming
// (llama.dll on Windows, libllama.so on Linux/Android, libllama.dylib on
// macOS/iOS).
//
// NOTE: For full inference (chat / generation), prefer the strongly-typed
// API in Circle.AI.Inference (QwenTextGenerator). This shim exists so older
// callers in Circle.AI.Embeddings can keep handing around SafeModelHandle
// values until the embedding path is rewritten on top of llama.cpp.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Circle.AI.Core;

/// <summary>
/// Loads native models via llama.cpp. Callers receive an opaque
/// <see cref="SafeModelHandle"/> they can pass on to inference code.
/// </summary>
public static class PlatformInterop
{
    private const string LibraryName = "llama";

    /// <summary>
    /// Loads a GGUF model from <paramref name="path"/> using llama.cpp.
    /// </summary>
    /// <exception cref="ArgumentException">Path is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Model file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Native load failed.</exception>
    public static SafeModelHandle LoadModel(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Model path is required.", nameof(path));

        if (!File.Exists(path))
            throw new FileNotFoundException("GGUF model file not found.", path);

        // Initialise backend once. llama_backend_init is idempotent in modern
        // builds so a per-call invocation is safe.
        llama_backend_init();

        var modelParams = llama_model_default_params();
        IntPtr nativeHandle = llama_model_load_from_file(path, ref modelParams);
        if (nativeHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"llama.cpp failed to load model at '{path}'. " +
                "Verify the file is a valid GGUF and that the native llama " +
                "library is on the search path.");

        return new SafeModelHandle(nativeHandle, FreeModel);
    }

    private static void FreeModel(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            llama_model_free(handle);
    }

    // -- minimal native bindings (mirrors of the entries in
    //    Circle.AI.Inference.LlamaCppInterop, kept here only because Core
    //    must not take a project reference on Inference). ------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct LlamaModelParamsCompat
    {
        public IntPtr devices;
        public IntPtr tensor_buft_overrides;
        public int    n_gpu_layers;
        public int    split_mode;
        public int    main_gpu;
        public IntPtr tensor_split;
        public IntPtr progress_callback;
        public IntPtr progress_callback_user_data;
        public IntPtr kv_overrides;
        [MarshalAs(UnmanagedType.I1)] public bool vocab_only;
        [MarshalAs(UnmanagedType.I1)] public bool use_mmap;
        [MarshalAs(UnmanagedType.I1)] public bool use_mlock;
        [MarshalAs(UnmanagedType.I1)] public bool check_tensors;
    }

    [DllImport(LibraryName, EntryPoint = "llama_backend_init")]
    private static extern void llama_backend_init();

    [DllImport(LibraryName, EntryPoint = "llama_model_default_params")]
    private static extern LlamaModelParamsCompat llama_model_default_params();

    [DllImport(LibraryName, EntryPoint = "llama_model_load_from_file", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr llama_model_load_from_file(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path_model,
        ref LlamaModelParamsCompat @params);

    [DllImport(LibraryName, EntryPoint = "llama_model_free")]
    private static extern void llama_model_free(IntPtr model);
}
