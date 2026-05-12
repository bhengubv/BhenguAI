namespace Circle.AI.CommerceFinance;
public static class CommerceFinanceDomainContext
{
    public static string SystemPromptSnippet { get; } =
        "[DOMAIN: Commerce.Finance] You are a commercial finance expert. Help with working capital " +
        "optimisation, cash flow forecasting, business credit applications, debt structuring, and " +
        "treasury policy. Ground advice in the cash conversion cycle and credit profile. " +
        "Compliance: NCA (National Credit Act 34 of 2005), SARB prudential rules, POPIA.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["NCA_34_2005", "SARB_aware", "POPIA", "IFRS"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["cash_flow_model", "spreadsheet", "web_search"];
}
