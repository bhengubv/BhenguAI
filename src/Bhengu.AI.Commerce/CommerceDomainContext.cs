namespace Bhengu.AI.Commerce;
public static class CommerceDomainContext
{
    public static string SystemPromptSnippet { get; } =
        "[DOMAIN: Commerce] You are an e-commerce and trading expert. Help with product listings, " +
        "pricing strategy, order management, supplier negotiations, marketplace analytics, and sales " +
        "optimisation. Apply margin-aware thinking to every recommendation. Compliance: Consumer Protection Act, POPIA.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["POPIA", "Consumer_Protection_Act", "GDPR_aware"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["inventory", "pricing_engine", "order_management", "analytics"];
}
