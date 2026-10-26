using Microsoft.Extensions.AI;
using AgenticLab.Extensibility.Agents;
using AgenticLab.Examples.Copilot365.Tools;

namespace AgenticLab.Examples.Copilot365.Agents;

/// <summary>
/// The Microsoft 365 Copilot "Copilot Chat" agent: a workplace assistant grounded in the user's
/// work content via the (fake) Microsoft 365 / Graph tools — email, files, Teams chats, calendar
/// and the people directory.
/// </summary>
public sealed class Microsoft365Agent(Microsoft365Tool m365) : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = "M365Copilot";

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Workplace assistant grounded in your Microsoft 365 content: email, files, Teams chats, calendar and people — and can send email on your behalf.";

    /// <inheritdoc />
    /// <remarks>
    /// Medium risk: most tools are read-only grounding, but <see cref="Microsoft365Tool.SendMail"/> models
    /// a write action on the user's behalf. Real delivery would cross a trust boundary and could leak
    /// data or impersonate the user; this sample only validates inputs and returns a simulated receipt.
    /// </remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Medium;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Sending email is the only write action — all other tools are read-only search",
        "Instructed to confirm the recipient, subject and body with you before sending",
        "Validates the recipient address and refuses an empty message before sending",
        "Sample, in-memory data — no real Microsoft Graph or network calls",
        "No file-system or command access on your machine",
        "The SendMail tool can be toggled off per run to make the agent read-only",
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "What is the status of the Q3 launch? Check my email and calendar.",
        "Summarize the FY26 budget workbook and who sent me the latest forecast.",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are Microsoft 365 Copilot, a helpful assistant for work. " +
        "You answer questions about the user's working day by grounding your replies in their organization's " +
        "content: use SearchEmail for Outlook mail, SearchFiles for OneDrive/SharePoint documents, " +
        "SearchChats for Microsoft Teams messages, GetCalendar for meetings, FindPeople for the directory, " +
        "and SummarizeDocument to summarize a specific file. " +
        "You can also send email on the user's behalf with SendMail — this is a real, hard-to-reverse action, so " +
        "treat it with care: only call SendMail after the user has clearly asked you to send a message, and first " +
        "confirm the exact recipient, subject and body with them; never send to recipients the user did not name, " +
        "never include content the user has not approved, and never auto-send as a side effect of answering. " +
        "Prefer calling these tools over answering from memory, cite the email subject, file name, chat or " +
        "meeting you used, and respect that this is sensitive workplace data — be accurate and never invent it.";

    /// <inheritdoc />
    public override IList<AITool> Tools => m365.AsTools();
}