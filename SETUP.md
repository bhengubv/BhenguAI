# BhenguAI Setup

This library needs native llama.cpp binaries at runtime. They aren't bundled (size + licensing).

## Acquire native binaries

Easiest: download prebuilt llama.cpp binaries from https://github.com/ggerganov/llama.cpp/releases

Or build from source: `git clone https://github.com/ggerganov/llama.cpp && cd llama.cpp && cmake -B build && cmake --build build --config Release`.

## Drop-in locations per platform

| Platform | Filename | Path |
|---|---|---|
| Windows x64 | `llama.dll` | next to your `.exe` |
| Linux x64 | `libllama.so` | next to your binary or in `LD_LIBRARY_PATH` |
| macOS arm64 | `libllama.dylib` | next to your binary |
| Android arm64 | `libllama.so` | inside the APK at `lib/arm64-v8a/` |
| iOS arm64 | `libllama.dylib` | embedded in the app bundle |

## Acquire the model

The downloader pulls Qwen 3 14B Q4_K_M from ModelScope (primary) or HuggingFace (fallback). About 8 GB.

```csharp
var loader = new LocalModelLoader();
var modelPath = await loader.DownloadModelAsync("Qwen3-14B-Q4");
```

## Verify
Run `samples/ConsoleTest`. Expected output: a chat completion from Qwen.
