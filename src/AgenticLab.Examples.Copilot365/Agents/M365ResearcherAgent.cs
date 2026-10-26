using Microsoft.Extensions.AI;
using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Runtime;
using AgenticLab.Examples.Copilot365.Tools;

namespace AgenticLab.Examples.Copilot365.Agents;

/// <summary>
/// The Microsoft 365 Copilot "Researcher" agent: a deep, multi-source researcher that combines the
/// user's work content (the fake Microsoft 365 / Graph search tools) with public web knowledge
/// (Wikipedia) to produce a thorough, grounded answer.
/// </summary>
public sealed class M365ResearcherAgent(Microsoft365Tool m365, IHostToolSource hostTools) : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = "M365Researcher";

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Deep researcher that combines your Microsoft 365 work content with public web knowledge.";

    /// <inheritdoc />
    /// <remarks>Low risk: read-only research over work content and Wikipedia, with no side effects.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Read-only search and lookups — never writes or sends anything",
        "Sample, in-memory work data — no real Microsoft Graph or network calls",
        "No file-system or command access on your machine",
        "Individual tools can be toggled off per run",
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "Research the SSO blocker: what do our emails and chats say, and what does Wikipedia say about SAML?",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are Researcher, a Microsoft 365 Copilot agent for in-depth research. " +
        "You tackle open-ended questions by gathering evidence from multiple sources before answering: " +
        "use SearchFiles, SearchEmail, SearchChats and FindPeople to ground the answer in the user's work " +
        "content, SummarizeDocument to dig into a specific file, and SearchWiki/GetWikiPage for public web " +
        "background. Synthesize what you find into a clear, well-structured answer, distinguish internal " +
        "sources from public ones, and note where the evidence is thin rather than guessing.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. m365.AsResearchTools(), .. hostTools.GetTools(["SearchWiki", "GetWikiPage"])];
}