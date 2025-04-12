using Bhengu.AI.Core;
using Bhengu.AI.Embeddings;
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\nCancellation requested...");
        };

        try
        {
            const string modelId = "microsoft/Phi-3-mini-128k-instruct";

            using var downloader = new HuggingFaceModelDownloader();

            // Subscribe to progress updates
            downloader.ProgressChanged += progress => {
                // The downloader handles console output itself
                // We could add additional logging here if needed
            };

            using var modelManager = new LocalModelManager(
                new Uri("https://huggingface.co/"),
                modelsDirectory: Path.Combine(Environment.CurrentDirectory, "Models")
            );

            Console.WriteLine($"Downloading Phi-3-mini model...\n");

            try
            {
                var modelPath = await modelManager.GetModelPathAsync(modelId, ct: cts.Token);

                Console.WriteLine("\nModel download complete!");
                Console.WriteLine($"Model saved to: {modelPath}");

                using var embedder = new Phi3MiniTextEmbedder(modelPath);
                var embedding = await embedder.GenerateAsync("Hello world", cts.Token);
                Console.WriteLine($"\nGenerated embedding (first 5 dims): [{string.Join(", ", embedding.Take(5))}...]");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\nDownload cancelled by user");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nError: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Details: {ex.InnerException.Message}");
                }
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
        }
        finally
        {
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}