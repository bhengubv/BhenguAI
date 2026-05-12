using System.Runtime.CompilerServices;
using Bhengu.AI.Companion;
namespace Bhengu.AI.Family;
public sealed class FamilyCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public FamilyCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{FamilyDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> PlanFamilyActivityAsync(string ages,string budget,string interests,CancellationToken ct=default)=>_i.AgentAsync($"Plan a family activity for children aged {ages}. Budget: {budget}. Interests: {interests}. Include indoor and outdoor options with estimated cost and age-appropriateness.",ct);
    public Task<string> CreateFamilyBudgetAsync(string income,string expenses,string goals,CancellationToken ct=default)=>_i.AgentAsync($"Create a family budget. Combined income: {income}. Expenses: {expenses}. Goals: {goals}. Allocate to categories and identify savings opportunities.",ct);}
