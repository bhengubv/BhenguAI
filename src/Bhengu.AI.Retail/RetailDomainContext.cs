namespace Bhengu.AI.Retail;
public static class RetailDomainContext {
    public static string SystemPromptSnippet { get; } = "[DOMAIN: Retail] Expert retail operations assistant. Help with stock replenishment, planogram optimisation, shrinkage reduction, seasonal promotions, customer loyalty, and sales floor management. Ground advice in margin and sell-through rates. Compliance: Consumer Protection Act, POPIA.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["Consumer_Protection_Act","POPIA","Labour_Relations_Act"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["pos_system","inventory","analytics","promotions_engine"];
}
