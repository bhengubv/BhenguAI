using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.PersonalFinance;
public sealed class PersonalFinanceCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public PersonalFinanceCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{PersonalFinanceDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> BuildBudgetAsync(string income,string expenses,CancellationToken ct=default)=>_i.AgentAsync($"Build a monthly budget. Income: {income}. Expenses: {expenses}. Apply the 50/30/20 rule, identify savings opportunities, and flag over-spending categories.",ct);
    public Task<string> CreateDebtPlanAsync(string debts,CancellationToken ct=default)=>_i.AgentAsync($"Create a debt elimination plan using the avalanche method (highest interest first):\n{debts}\nShow monthly payment schedule, total interest saved, and debt-free date.",ct);}
