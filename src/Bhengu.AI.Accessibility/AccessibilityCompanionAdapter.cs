using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Accessibility;
public sealed class AccessibilityCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public AccessibilityCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{AccessibilityDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> AuditWcagAsync(string htmlOrDescription,CancellationToken ct=default)=>_i.AgentAsync($"Audit this interface for WCAG 2.2 AA compliance. Identify violations, their impact on disabled users, and remediation steps:\n{htmlOrDescription}",ct);
    public Task<string> WriteAltTextAsync(string imageDescription,string context,CancellationToken ct=default)=>_i.AgentAsync($"Write descriptive alt text for an image. Image: {imageDescription}. Context: {context}. Follow WCAG 2.2 alt text best practices.",ct);}
