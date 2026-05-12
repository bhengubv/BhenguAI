using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Commerce;
public sealed class CommerceCompanionAdapter : ICompanionSession
{
    private readonly ICompanionSession _inner;
    public CommerceCompanionAdapter(ICompanionSession inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    public string SessionId  => _inner.SessionId;
    public string IdentityId => _inner.IdentityId;
    public InterfaceKind Interface => _inner.Interface;
    public IReadOnlyList<CompanionTurn> History => _inner.History;
    public CompanionContext GetContext() => _inner.GetContext();
    public Task RefreshContextAsync(CancellationToken ct = default) => _inner.RefreshContextAsync(ct);
    public Task SignalFeedbackAsync(bool positive, string? note = null, CancellationToken ct = default) => _inner.SignalFeedbackAsync(positive, note, ct);
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
    public event EventHandler<CompanionProactiveEvent>? ProactiveMessageReady
    { add => _inner.ProactiveMessageReady += value; remove => _inner.ProactiveMessageReady -= value; }
    public Task<string> SendAsync(string message, CancellationToken ct = default) => _inner.SendAsync(Enrich(message), ct);
    public IAsyncEnumerable<string> StreamAsync(string message, [EnumeratorCancellation] CancellationToken ct = default) => _inner.StreamAsync(Enrich(message), ct);
    public Task<string> AgentAsync(string instruction, CancellationToken ct = default) => _inner.AgentAsync(Enrich(instruction), ct);
    private static string Enrich(string m) => $"{CommerceDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> OptimiseListingAsync(string productDetails, CancellationToken ct = default)
        => _inner.AgentAsync($"Optimise this product listing for search discovery and conversions:\n{productDetails}", ct);
    public Task<string> AnalysePricingAsync(string product, decimal currentPrice, CancellationToken ct = default)
        => _inner.AgentAsync($"Analyse pricing for: {product} at {currentPrice:C}. Recommend optimal pricing considering margins, competition, and demand.", ct);
    public Task<string> GenerateSupplierBriefAsync(string productRequirements, CancellationToken ct = default)
        => _inner.AgentAsync($"Write a supplier brief for: {productRequirements}. Include quantity, specs, quality standards, delivery terms, and pricing expectations.", ct);
}
