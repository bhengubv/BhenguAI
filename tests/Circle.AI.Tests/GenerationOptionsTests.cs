using System;
using System.Collections.Generic;
using Circle.AI.Inference;
using Xunit;

namespace Circle.AI.Tests;

/// <summary>
/// Tests for <see cref="GenerationOptions"/> default values / init-only semantics,
/// and for <see cref="ChatMessage"/> record semantics.
/// </summary>
public sealed class GenerationOptionsTests
{
    // ------------------------------------------------------------------
    // Defaults
    // ------------------------------------------------------------------

    [Fact]
    public void Defaults_MaxTokens_Is512()
    {
        var opts = new GenerationOptions();
        Assert.Equal(512, opts.MaxTokens);
    }

    [Fact]
    public void Defaults_Temperature_Is0Point7()
    {
        var opts = new GenerationOptions();
        Assert.Equal(0.7f, opts.Temperature, precision: 6);
    }

    [Fact]
    public void Defaults_TopP_Is0Point9()
    {
        var opts = new GenerationOptions();
        Assert.Equal(0.9f, opts.TopP, precision: 6);
    }

    [Fact]
    public void Defaults_TopK_Is40()
    {
        var opts = new GenerationOptions();
        Assert.Equal(40, opts.TopK);
    }

    [Fact]
    public void Defaults_Seed_IsNull()
    {
        var opts = new GenerationOptions();
        Assert.Null(opts.Seed);
    }

    [Fact]
    public void Defaults_StopSequences_IsNull()
    {
        var opts = new GenerationOptions();
        Assert.Null(opts.StopSequences);
    }

    // ------------------------------------------------------------------
    // Init-only overrides
    // ------------------------------------------------------------------

    [Fact]
    public void Override_MaxTokens_SetsValue()
    {
        var opts = new GenerationOptions { MaxTokens = 2048 };
        Assert.Equal(2048, opts.MaxTokens);
    }

    [Fact]
    public void Override_Temperature_Zero_IsGreedy()
    {
        var opts = new GenerationOptions { Temperature = 0f };
        Assert.Equal(0f, opts.Temperature, precision: 6);
    }

    [Fact]
    public void Override_SpecificFields_OtherFieldsKeepDefaults()
    {
        var opts = new GenerationOptions { MaxTokens = 1024, Temperature = 0.5f };

        Assert.Equal(1024, opts.MaxTokens);
        Assert.Equal(0.5f, opts.Temperature, precision: 6);
        // Non-overridden fields retain their defaults.
        Assert.Equal(0.9f, opts.TopP, precision: 6);
        Assert.Equal(40,   opts.TopK);
        Assert.Null(opts.Seed);
        Assert.Null(opts.StopSequences);
    }

    [Fact]
    public void Override_Seed_ReturnsSetValue()
    {
        var opts = new GenerationOptions { Seed = 42 };
        Assert.Equal(42, opts.Seed);
    }

    [Fact]
    public void Override_StopSequences_ReturnsSetValue()
    {
        var stops = new[] { "<|im_end|>", "<|im_start|>" };
        var opts = new GenerationOptions { StopSequences = stops };
        Assert.Equal(stops, opts.StopSequences);
    }

    [Fact]
    public void MultipleInstances_ShareNoState()
    {
        // GenerationOptions is a class with init properties; two separate
        // instances must not share reference fields.
        var a = new GenerationOptions { MaxTokens = 10 };
        var b = new GenerationOptions { MaxTokens = 20 };
        Assert.NotEqual(a.MaxTokens, b.MaxTokens);
    }
}

// ==========================================================================
// ChatMessage — record semantics
// ==========================================================================

public sealed class ChatMessageTests
{
    // ------------------------------------------------------------------
    // Equality
    // ------------------------------------------------------------------

    [Fact]
    public void Equality_SameRoleAndContent_AreEqual()
    {
        var a = new ChatMessage("user", "hello");
        var b = new ChatMessage("user", "hello");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentRole_NotEqual()
    {
        var a = new ChatMessage("user",      "hello");
        var b = new ChatMessage("assistant", "hello");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentContent_NotEqual()
    {
        var a = new ChatMessage("user", "hello");
        var b = new ChatMessage("user", "world");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_RoleIsCaseSensitive()
    {
        // Record equality uses default string equality which is case-sensitive.
        var a = new ChatMessage("system", "s");
        var b = new ChatMessage("SYSTEM", "s");
        Assert.NotEqual(a, b);
    }

    // ------------------------------------------------------------------
    // Deconstruction
    // ------------------------------------------------------------------

    [Fact]
    public void Deconstruct_ProducesRoleAndContent()
    {
        var msg = new ChatMessage("system", "You are B!");
        var (role, content) = msg;
        Assert.Equal("system",    role);
        Assert.Equal("You are B!", content);
    }

    // ------------------------------------------------------------------
    // With-expression (non-destructive copy)
    // ------------------------------------------------------------------

    [Fact]
    public void With_Content_CreatesModifiedCopy()
    {
        var original = new ChatMessage("user", "hello");
        var copy     = original with { Content = "world" };

        Assert.Equal("user",  copy.Role);
        Assert.Equal("world", copy.Content);
        // Original is unchanged.
        Assert.Equal("hello", original.Content);
    }

    [Fact]
    public void With_Role_CreatesModifiedCopy()
    {
        var original = new ChatMessage("user", "text");
        var copy     = original with { Role = "assistant" };

        Assert.Equal("assistant", copy.Role);
        Assert.Equal("text",      copy.Content);
        Assert.Equal("user",      original.Role);
    }

    // ------------------------------------------------------------------
    // Hash code
    // ------------------------------------------------------------------

    [Fact]
    public void HashCode_EqualMessages_SameHash()
    {
        var a = new ChatMessage("user", "ping");
        var b = new ChatMessage("user", "ping");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ------------------------------------------------------------------
    // Role / Content exposure
    // ------------------------------------------------------------------

    [Fact]
    public void Properties_ReturnConstructorValues()
    {
        var msg = new ChatMessage("assistant", "Sure, let me help.");
        Assert.Equal("assistant",         msg.Role);
        Assert.Equal("Sure, let me help.", msg.Content);
    }

    // ------------------------------------------------------------------
    // Usability in collections
    // ------------------------------------------------------------------

    [Fact]
    public void CanBeStoredAndRetrievedFromList()
    {
        var messages = new List<ChatMessage>
        {
            new("system",    "sys"),
            new("user",      "hi"),
            new("assistant", "hey"),
        };

        Assert.Equal("system",    messages[0].Role);
        Assert.Equal("assistant", messages[2].Role);
    }

    [Fact]
    public void CanBeUsedAsDictionaryKey()
    {
        var a = new ChatMessage("user", "x");
        var b = new ChatMessage("user", "x");
        var dict = new Dictionary<ChatMessage, int> { [a] = 1 };

        Assert.True(dict.ContainsKey(b));
        Assert.Equal(1, dict[b]);
    }
}
