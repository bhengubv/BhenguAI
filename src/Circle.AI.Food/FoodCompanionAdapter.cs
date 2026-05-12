using System.Runtime.CompilerServices;
using Circle.AI.Companion;
namespace Circle.AI.Food;
public sealed class FoodCompanionAdapter:ICompanionSession{private readonly ICompanionSession _i;
public FoodCompanionAdapter(ICompanionSession i)=>_i=i??throw new ArgumentNullException(nameof(i));
public string SessionId=>_i.SessionId;public string IdentityId=>_i.IdentityId;public InterfaceKind Interface=>_i.Interface;
public IReadOnlyList<CompanionTurn> History=>_i.History;public CompanionContext GetContext()=>_i.GetContext();
public Task RefreshContextAsync(CancellationToken ct=default)=>_i.RefreshContextAsync(ct);
public Task SignalFeedbackAsync(bool p,string?n=null,CancellationToken ct=default)=>_i.SignalFeedbackAsync(p,n,ct);
public ValueTask DisposeAsync()=>_i.DisposeAsync();
public event EventHandler<CompanionProactiveEvent>?ProactiveMessageReady{add=>_i.ProactiveMessageReady+=value;remove=>_i.ProactiveMessageReady-=value;}
public Task<string> SendAsync(string m,CancellationToken ct=default)=>_i.SendAsync(E(m),ct);
public IAsyncEnumerable<string> StreamAsync(string m,[EnumeratorCancellation]CancellationToken ct=default)=>_i.StreamAsync(E(m),ct);
public Task<string> AgentAsync(string m,CancellationToken ct=default)=>_i.AgentAsync(E(m),ct);
private static string E(string m)=>$"{FoodDomainContext.SystemPromptSnippet}\n\n{m}";
    public Task<string> CreateRecipeAsync(string ingredients,string dietary,string difficulty,CancellationToken ct=default)=>_i.AgentAsync($"Create a recipe using: {ingredients}. Dietary requirements: {dietary}. Difficulty: {difficulty}. Include prep time, cook time, step-by-step method, and nutritional estimate.",ct);
    public Task<string> PlanMealsAsync(string days,string people,string dietary,string budget,CancellationToken ct=default)=>_i.AgentAsync($"Plan {days} days of meals for {people} people. Dietary: {dietary}. Budget: {budget}. Include breakfast, lunch, dinner, and a shopping list.",ct);}
