using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Retail;
public sealed class RetailCompanionAdapter : ICompanionSession {
    private readonly ICompanionSession _i;
    public RetailCompanionAdapter(ICompanionSession i) => _i = i ?? throw new ArgumentNullException(nameof(i));
    public string SessionId  => _i.SessionId;
    public string IdentityId => _i.IdentityId;
    public InterfaceKind Interface => _i.Interface;
    public IReadOnlyList<CompanionTurn> History => _i.History;
    public CompanionContext GetContext() => _i.GetContext();
    public Task RefreshContextAsync(CancellationToken ct=default) => _i.RefreshContextAsync(ct);
    public Task SignalFeedbackAsync(bool p,string? n=null,CancellationToken ct=default) => _i.SignalFeedbackAsync(p,n,ct);
    public ValueTask DisposeAsync() => _i.DisposeAsync();
    public event EventHandler<CompanionProactiveEvent>? ProactiveMessageReady
    { add=>_i.ProactiveMessageReady+=value; remove=>_i.ProactiveMessageReady-=value; }
    public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
    public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
    public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
    private static string E(string m)=>$"{RetailDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> AnalyseStockHealthAsync(string sku, int onHand, int weeklySales, CancellationToken ct=default)
        =>_i.AgentAsync($"Analyse stock health for SKU {sku}: {onHand} units on hand, {weeklySales} weekly sales. Recommend reorder point, safety stock, and EOQ.",ct);
    public Task<string> PlanPromotionAsync(string objective, string constraints, CancellationToken ct=default)
        =>_i.AgentAsync($"Plan a retail promotion. Objective: {objective}. Constraints: {constraints}. Include mechanics, discount level, marketing channels, and success metrics.",ct);}
