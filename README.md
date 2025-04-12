# Bhengu.AI.Core

![NuGet Version](https://img.shields.io/nuget/v/Bhengu.AI.Core?color=blue) ![License](https://img.shields.io/github/license/yourusername/Bhengu.AI)

Professional AI model management for .NET applications, with first-class support for Hugging Face models like Microsoft's Phi-3-mini.

## Features

✅ **Smart Model Downloading**
- Resume interrupted downloads
- Checksum verification
- Update detection

🚀 **Performance**
- Multi-threaded downloads
- Sharded model support
- Progress reporting

🔒 **Security**
- Authentication support
- File validation
- Secure HTTPS transfers

## Installation

```bash
dotnet add package Bhengu.AI.Core
```

## Quick Start

```csharp
using Bhengu.AI.Core;
using Bhengu.AI.Embeddings;

// 1. Initialize downloader (add your HF token if needed)
using var downloader = new HuggingFaceModelDownloader();

// 2. Download Phi-3-mini (automatically caches)
await downloader.DownloadModelAsync(
    "microsoft/Phi-3-mini-128k-instruct",
    "./ai_models"
);

// 3. Use the model
using var embedder = new Phi3MiniTextEmbedder("./ai_models/microsoft_Phi-3-mini-128k-instruct");
var embedding = await embedder.GenerateAsync("Hello world");
```

## Configuration

Set these optional environment variables:

| Variable       | Purpose                          |
|----------------|----------------------------------|
| `HF_TOKEN`     | Hugging Face API token           |
| `HF_CACHE_DIR` | Custom model cache location      |
| `HF_TIMEOUT`   | Download timeout in minutes      |

## Documentation

Full API reference available at:  
[https://github.com/bhengubv/Bhengu.AI/docs](https://github.com/bhengubv/Bhengu.AI/docs)

## License

MIT License - Free for commercial and personal use.