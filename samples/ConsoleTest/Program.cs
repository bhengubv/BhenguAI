// BhenguAI ConsoleTest
//
// Demonstrates the post-pivot flow:
//   1. Build a BhenguEngine wired to LocalModelLoader.
//   2. Download Qwen 3 14B Q4_K_M (skipping if already cached).
//   3. Construct a QwenTextGenerator over the cached GGUF.
//   4. Run a single chat completion and print the answer.
//
// This sample is not meant to be runnable until the native llama.cpp binary
// (llama.dll / libllama.so / libllama.dylib) is dropped next to the produced
// binary. See SETUP.md for instructions.

using Circle.AI.Core;
using Circle.AI.Inference;

const string ModelId = "Qwen3-14B-Q4";

try
{
    Console.WriteLine("BhenguAI ConsoleTest");
    Console.WriteLine("====================");

    // 1. Engine + model loader.
    using var loader = new LocalModelLoader();
    var engine = new BhenguEngine(loader);

    // 2. Acquire the model. The loader caches and verifies, so this is a
    // no-op once we've downloaded it before.
    string modelPath;
    if (engine.ModelLoader.ModelExists(ModelId))
    {
        modelPath = engine.ModelLoader.GetModelPath(ModelId);
        Console.WriteLine($"Using cached model: {modelPath}");
    }
    else
    {
        Console.WriteLine($"Downloading {ModelId} (~8 GB) — this can take a while...");
        var progress = new Progress<float>(p =>
            Console.Write($"\r  {p * 100:0.0}%   "));
        modelPath = await engine.ModelLoader.DownloadModelAsync(ModelId, progress);
        Console.WriteLine();
        Console.WriteLine($"Downloaded to: {modelPath}");
    }

    // 3. Build the chat generator. This is where the native llama.cpp
    // library is loaded; missing-binary failures surface here.
    using IChatGenerator chat = new QwenTextGenerator(modelPath);

    // 4. One round of chat.
    var messages = new List<ChatMessage>
    {
        new("system", "You are B!, the on-device assistant for the TheGeekNetwork ecosystem. Answer in two or three sentences."),
        new("user",   "What is the SDPKT wallet?")
    };

    Console.WriteLine();
    Console.WriteLine("Question: What is the SDPKT wallet?");
    Console.WriteLine("Answer:");

    var reply = await chat.GenerateAsync(messages);
    Console.WriteLine(reply);
}
catch (DllNotFoundException ex)
{
    PrintNativeMissing(ex.Message);
}
catch (BadImageFormatException ex)
{
    PrintNativeMissing(ex.Message);
}
catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException)
{
    PrintNativeMissing(ex.InnerException!.Message);
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine();
    Console.WriteLine($"Error: {ex.Message}");
    if (ex.InnerException is not null)
        Console.WriteLine($"Cause: {ex.InnerException.Message}");
    Console.ResetColor();
}

static void PrintNativeMissing(string detail)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine();
    Console.WriteLine("Native llama.cpp library not found.");
    Console.WriteLine("  Drop llama.dll (Windows), libllama.so (Linux/Android),");
    Console.WriteLine("  or libllama.dylib (macOS/iOS) next to this binary.");
    Console.WriteLine("  See SETUP.md for how to obtain or build it.");
    Console.WriteLine();
    Console.WriteLine($"  Loader said: {detail}");
    Console.ResetColor();
}
