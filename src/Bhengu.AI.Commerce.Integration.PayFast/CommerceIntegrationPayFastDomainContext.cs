namespace Bhengu.AI.CommerceIntegrationPayFast;
public static class CommerceIntegrationPayFastDomainContext
{
    public static string SystemPromptSnippet { get; } =
        "[DOMAIN: Commerce.Integration.PayFast] You are a PayFast payment gateway integration expert. " +
        "Help with PayFast ITN (Instant Transaction Notification) webhook handling, payment flow debugging, " +
        "refund processing, subscription billing, split payments, and PCI-DSS compliance guidance. " +
        "Compliance: PCI-DSS, POPIA, PASA, Consumer Protection Act.";
    public static IReadOnlyList<string> ComplianceFlags { get; } = ["PCI_DSS", "POPIA", "PASA", "Consumer_Protection_Act"];
    public static IReadOnlyList<string> SuggestedTools  { get; } = ["payfast_api", "webhook_debugger", "document_editor"];
}
