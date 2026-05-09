using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bhengu.AI.Inference;
using Xunit;

namespace Bhengu.AI.Tests;

/// <summary>
/// Unit tests for the pure-logic helpers in <see cref="QwenTextGenerator"/>.
/// None of these tests load a native library or require a GGUF model on disk.
/// </summary>
public sealed class QwenTextGeneratorTests
{
    // =========================================================================
    // Constructor argument guards (fire BEFORE native backend init)
    // =========================================================================

    [Fact]
    public void Constructor_NullPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new QwenTextGenerator(null!, contextSize: 512));
    }

    [Fact]
    public void Constructor_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new QwenTextGenerator("", contextSize: 512));
    }

    [Fact]
    public void Constructor_WhitespacePath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new QwenTextGenerator("   ", contextSize: 512));
    }

    [Fact]
    public void Constructor_MissingFile_ThrowsFileNotFoundException()
    {
        // Use a path that definitely does not exist.
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gguf");
        Assert.Throws<FileNotFoundException>(() =>
            new QwenTextGenerator(missing, contextSize: 512));
    }

    [Fact]
    public void Constructor_ZeroContextSize_ThrowsArgumentOutOfRangeException()
    {
        // Any existing file will do; the check fires before native load.
        var file = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QwenTextGenerator(file, contextSize: 0));
        }
        finally
        {
            try { File.Delete(file); } catch { /* best-effort */ }
        }
    }

    // =========================================================================
    // BuildQwenChatPrompt — ChatML formatting
    // =========================================================================

    [Fact]
    public void BuildQwenChatPrompt_EmptyMessages_ReturnsOpenAssistantTurn()
    {
        var result = QwenTextGenerator.BuildQwenChatPrompt(Array.Empty<ChatMessage>());
        // Should end with the open assistant turn ready for the model to complete.
        Assert.EndsWith("<|im_start|>assistant\n", result);
    }

    [Fact]
    public void BuildQwenChatPrompt_SystemThenUser_CorrectChatMLFormat()
    {
        var messages = new[]
        {
            new ChatMessage("system", "You are a helpful assistant."),
            new ChatMessage("user",   "What is 2 + 2?"),
        };

        var result = QwenTextGenerator.BuildQwenChatPrompt(messages);

        Assert.Contains("<|im_start|>system\nYou are a helpful assistant.\n<|im_end|>", result);
        Assert.Contains("<|im_start|>user\nWhat is 2 + 2?\n<|im_end|>", result);
        Assert.EndsWith("<|im_start|>assistant\n", result);
    }

    [Fact]
    public void BuildQwenChatPrompt_AssistantTurn_IncludedInOutput()
    {
        var messages = new[]
        {
            new ChatMessage("user",      "Hi"),
            new ChatMessage("assistant", "Hello!"),
            new ChatMessage("user",      "How are you?"),
        };

        var result = QwenTextGenerator.BuildQwenChatPrompt(messages);

        Assert.Contains("<|im_start|>assistant\nHello!\n<|im_end|>", result);
    }

    [Fact]
    public void BuildQwenChatPrompt_RoleIsNormalisedToLower()
    {
        var messages = new[] { new ChatMessage("USER", "hi") };
        var result = QwenTextGenerator.BuildQwenChatPrompt(messages);
        Assert.Contains("<|im_start|>user\n", result);
        Assert.DoesNotContain("<|im_start|>USER\n", result);
    }

    [Fact]
    public void BuildQwenChatPrompt_NullOrEmptyRole_DefaultsToUser()
    {
        var messages = new[] { new ChatMessage("", "content") };
        var result = QwenTextGenerator.BuildQwenChatPrompt(messages);
        Assert.Contains("<|im_start|>user\n", result);
    }

    [Fact]
    public void BuildQwenChatPrompt_NullContent_TreatedAsEmpty()
    {
        var messages = new[] { new ChatMessage("user", null!) };
        // Should not throw; null content is treated as empty string.
        var result = QwenTextGenerator.BuildQwenChatPrompt(messages);
        Assert.Contains("<|im_start|>user\n\n<|im_end|>", result);
    }

    [Fact]
    public void BuildQwenChatPrompt_MultiTurn_CountsCorrectly()
    {
        var messages = new[]
        {
            new ChatMessage("system",    "sys"),
            new ChatMessage("user",      "q1"),
            new ChatMessage("assistant", "a1"),
            new ChatMessage("user",      "q2"),
        };

        var result = QwenTextGenerator.BuildQwenChatPrompt(messages);

        // Each turn generates one im_start + one im_end block.
        Assert.Equal(4, CountOccurrences(result, "<|im_end|>"));
        // Plus the trailing open assistant turn.
        Assert.Equal(5, CountOccurrences(result, "<|im_start|>"));
    }

    [Fact]
    public void BuildQwenChatPrompt_WhitespaceRole_DefaultsToUser()
    {
        // Role that is only whitespace → normalised to "user" (same as empty).
        var messages = new[] { new ChatMessage("   ", "content") };
        var result = QwenTextGenerator.BuildQwenChatPrompt(messages);
        Assert.Contains("<|im_start|>user\n", result);
        Assert.DoesNotContain("<|im_start|>   \n", result);
    }

    [Fact]
    public void BuildQwenChatPrompt_RoleWithLeadingTrailingSpaces_IsTrimmed()
    {
        // " SYSTEM " → trim → "system" (lower)
        var messages = new[] { new ChatMessage(" SYSTEM ", "You are B!") };
        var result = QwenTextGenerator.BuildQwenChatPrompt(messages);
        Assert.Contains("<|im_start|>system\n", result);
        Assert.DoesNotContain("<|im_start|> SYSTEM \n", result);
    }

    [Fact]
    public void BuildQwenChatPrompt_LargeConversation_ProducesCorrectStructure()
    {
        // Verifies that alternating turns produce correctly interleaved blocks.
        var messages = new[]
        {
            new ChatMessage("system",    "sys"),
            new ChatMessage("user",      "q1"),
            new ChatMessage("assistant", "a1"),
            new ChatMessage("user",      "q2"),
            new ChatMessage("assistant", "a2"),
            new ChatMessage("user",      "q3"),
        };

        var result = QwenTextGenerator.BuildQwenChatPrompt(messages);

        // There must be exactly 6 closed turns + 1 open assistant at the end.
        Assert.Equal(6, CountOccurrences(result, "<|im_end|>"));
        Assert.Equal(7, CountOccurrences(result, "<|im_start|>"));
        Assert.EndsWith("<|im_start|>assistant\n", result);
    }

    // =========================================================================
    // TryDrainUtf8 — streaming UTF-8 decode
    // =========================================================================

    [Fact]
    public void TryDrainUtf8_EmptyList_ReturnsFalse()
    {
        var pending = new List<byte>();
        var result = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.False(result);
        Assert.Equal(string.Empty, decoded);
        Assert.Empty(pending);
    }

    [Fact]
    public void TryDrainUtf8_AsciiBytes_DecodesAndClearsBuffer()
    {
        var pending = new List<byte>(Encoding.ASCII.GetBytes("hello"));
        var result = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.True(result);
        Assert.Equal("hello", decoded);
        Assert.Empty(pending);
    }

    [Fact]
    public void TryDrainUtf8_Complete2ByteSequence_DecodesCorrectly()
    {
        // 'é' = U+00E9, UTF-8: 0xC3 0xA9
        var pending = new List<byte> { 0xC3, 0xA9 };
        var result = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.True(result);
        Assert.Equal("é", decoded);
        Assert.Empty(pending);
    }

    [Fact]
    public void TryDrainUtf8_Incomplete2ByteSequence_BuffersAndReturnsFalse()
    {
        // Only first byte of a 2-byte sequence — should stay buffered.
        var pending = new List<byte> { 0xC3 }; // 0xC3 alone is incomplete
        var result = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.False(result);
        // The incomplete byte stays in the pending list.
        Assert.Single(pending);
    }

    [Fact]
    public void TryDrainUtf8_Complete3ByteSequence_Decodes()
    {
        // '中' = U+4E2D, UTF-8: 0xE4 0xB8 0xAD
        var pending = new List<byte> { 0xE4, 0xB8, 0xAD };
        var result = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.True(result);
        Assert.Equal("中", decoded);
        Assert.Empty(pending);
    }

    [Fact]
    public void TryDrainUtf8_AsciiFollowedByIncomplete_DrainsAsciiOnly()
    {
        // "hi" (ASCII) + first byte of 2-byte sequence
        var pending = new List<byte> { 0x68, 0x69, 0xC3 }; // "hi" + incomplete
        var result = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.True(result);
        Assert.Equal("hi", decoded);
        // Incomplete byte remains
        Assert.Single(pending);
        Assert.Equal(0xC3, pending[0]);
    }

    [Fact]
    public void TryDrainUtf8_EmojiBytesComplete_Decodes()
    {
        // '😀' = U+1F600, UTF-8: 0xF0 0x9F 0x98 0x80 (4-byte sequence)
        var pending = new List<byte> { 0xF0, 0x9F, 0x98, 0x80 };
        var result = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.True(result);
        Assert.Equal("😀", decoded);
        Assert.Empty(pending);
    }

    [Fact]
    public void TryDrainUtf8_PartialEmoji_BuffersAll4Bytes()
    {
        // First 3 bytes of a 4-byte emoji sequence — incomplete
        var pending = new List<byte> { 0xF0, 0x9F, 0x98 };
        var result = QwenTextGenerator.TryDrainUtf8(pending, out _);
        Assert.False(result);
        Assert.Equal(3, pending.Count);
    }

    // =========================================================================
    // TryFindStopSequence
    // =========================================================================

    [Fact]
    public void TryFindStopSequence_EmptyStops_ReturnsFalse()
    {
        var sb = new StringBuilder("hello world");
        var found = QwenTextGenerator.TryFindStopSequence(sb, Array.Empty<string>(), out var index);
        Assert.False(found);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void TryFindStopSequence_StopNotPresent_ReturnsFalse()
    {
        var sb = new StringBuilder("hello world");
        var found = QwenTextGenerator.TryFindStopSequence(
            sb, new[] { "<|im_end|>" }, out var index);
        Assert.False(found);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void TryFindStopSequence_StopPresent_ReturnsTrueWithCorrectIndex()
    {
        var sb = new StringBuilder("Hello<|im_end|>more");
        var found = QwenTextGenerator.TryFindStopSequence(
            sb, new[] { "<|im_end|>" }, out var index);
        Assert.True(found);
        Assert.Equal(5, index); // "Hello" is 5 chars
    }

    [Fact]
    public void TryFindStopSequence_NullOrEmptyStopStrings_SkippedAndNotMatched()
    {
        var sb = new StringBuilder("some text");
        var found = QwenTextGenerator.TryFindStopSequence(
            sb, new string[] { null!, "", "nothere" }, out var index);
        Assert.False(found);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void TryFindStopSequence_MultipleStops_ReturnsFirstFound()
    {
        // Both stops appear; the first one in the stop list that is found wins.
        var sb = new StringBuilder("abc<|im_end|>def<|im_start|>ghi");
        var found = QwenTextGenerator.TryFindStopSequence(
            sb, new[] { "<|im_end|>", "<|im_start|>" }, out var index);
        Assert.True(found);
        Assert.Equal(3, index); // "<|im_end|>" starts at index 3
    }

    [Fact]
    public void TryFindStopSequence_StopAtStart_ReturnsIndexZero()
    {
        var sb = new StringBuilder("<|im_end|>rest");
        var found = QwenTextGenerator.TryFindStopSequence(
            sb, new[] { "<|im_end|>" }, out var index);
        Assert.True(found);
        Assert.Equal(0, index);
    }

    [Fact]
    public void TryFindStopSequence_SecondStopBeforeFirstInString_ReturnsFirstInArray()
    {
        // "<|im_start|>" is at position 3 in the string, but "<|im_end|>" is the
        // first entry in the stops array. The implementation iterates the stops
        // array in order, so it returns the position of "<|im_end|>" (pos 18)
        // rather than the position of "<|im_start|>" (pos 3).
        // This test DOCUMENTS the "first-in-array" rather than "earliest-in-string"
        // semantics — important for callers who rely on this ordering.
        var sb = new StringBuilder("abc<|im_start|>def<|im_end|>ghi");
        var found = QwenTextGenerator.TryFindStopSequence(
            sb, new[] { "<|im_end|>", "<|im_start|>" }, out var index);
        Assert.True(found);
        // "<|im_end|>" starts at index 18 (3 + len("<|im_start|>") + 3 = 3+12+3 = 18)
        Assert.Equal(18, index);
    }

    [Fact]
    public void TryFindStopSequence_StopAtEnd_ReturnsCorrectIndex()
    {
        var sb = new StringBuilder("hello world<|im_end|>");
        var found = QwenTextGenerator.TryFindStopSequence(
            sb, new[] { "<|im_end|>" }, out var index);
        Assert.True(found);
        Assert.Equal(11, index); // "hello world" is 11 chars
    }

    // =========================================================================
    // TryDrainUtf8 — additional edge cases
    // =========================================================================

    [Fact]
    public void TryDrainUtf8_MultipleCompleteSequences_DecodesAll()
    {
        // "é中" = U+00E9 (2 bytes) + U+4E2D (3 bytes) = 5 bytes total
        var pending = new List<byte> { 0xC3, 0xA9, 0xE4, 0xB8, 0xAD };
        var ok = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.True(ok);
        Assert.Equal("é中", decoded);
        Assert.Empty(pending);
    }

    [Fact]
    public void TryDrainUtf8_AsciiFollowedByComplete3Byte_DecodesAll()
    {
        // "hi中" = 0x68 0x69 + 3 bytes for 中
        var pending = new List<byte> { 0x68, 0x69, 0xE4, 0xB8, 0xAD };
        var ok = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.True(ok);
        Assert.Equal("hi中", decoded);
        Assert.Empty(pending);
    }

    [Fact]
    public void TryDrainUtf8_NullByte_TreatedAsValidAscii()
    {
        // 0x00 is a valid UTF-8 byte (NUL / ASCII 0). It decodes to "\0" (length 1).
        var pending = new List<byte> { 0x00 };
        var result = QwenTextGenerator.TryDrainUtf8(pending, out var decoded);
        Assert.True(result);
        Assert.Equal("\0", decoded);
        Assert.Empty(pending);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static int CountOccurrences(string source, string substring)
    {
        int count = 0;
        int idx = 0;
        while ((idx = source.IndexOf(substring, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += substring.Length;
        }
        return count;
    }
}
