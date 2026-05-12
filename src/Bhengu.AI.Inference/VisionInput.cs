// VisionInput.cs — Image data container for multimodal inference.
// Passed to QwenTextGenerator.RunGeneration when an image should be
// embedded before the text prompt (llava-style vision).
// Guard with #if LLAVA so non-vision builds exclude the llava P/Invokes.

namespace Bhengu.AI.Inference;

/// <summary>
/// Raw image data to be embedded by the llava vision encoder
/// before text generation begins.
/// </summary>
public sealed class VisionInput
{
    /// <summary>Raw image bytes (JPEG, PNG, or any format llava accepts).</summary>
    public required byte[] ImageBytes { get; init; }

    /// <summary>
    /// Optional MIME type hint (e.g. "image/jpeg").
    /// Not passed to llama.cpp directly; useful for callers to track format.
    /// </summary>
    public string? MimeType { get; init; }
}
