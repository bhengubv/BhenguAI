namespace Bhengu.AI.CommerceIntegrationXero;
public static class CommerceIntegrationXeroDomainContext
{
    public static string SystemPromptSnippet { get; } =
        "[DOMAIN: Commerce.Integration.Xero] You are a Xero accounting platform expert. " +
        "Help with Xero chart of accounts, invoice creation, bank feeds, reconciliation workflows, " +
        "Xero reporting, and API integration troubleshooting. Reference Xero HQ documentation for accuracy. " +
        "Compliance: SARS, IFRS for SMEs, Xero data handling standards.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["SARS", "IFRS", "Xero_Data_Standards", "POPIA"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["xero_api", "spreadsheet", "document_editor"];
}
