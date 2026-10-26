using Microsoft.Extensions.AI;
using AgenticLab.AiService.Demo.Tools;

namespace AgenticLab.AiService.Demo.Agents;

/// <summary>
/// The default research agent: answers factual questions grounded in Wikipedia.
/// </summary>
public sealed class WikiAssistantAgent(WikiTool wiki) : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = "WikiAssistant";

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Concise research helper that answers factual questions using Wikipedia.";

    /// <inheritdoc />
    /// <remarks>Low risk: read-only Wikipedia lookups with no side effects on the user's environment.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Read-only Wikipedia lookups — no changes to your environment",
        "No file-system or command access",
        "Individual tools can be toggled off per run",
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "When was the Eiffel Tower completed? Check Wikipedia.",
        "Who was Ada Lovelace and what is she known for?",
        "Summarize the Wikipedia page on the Model Context Protocol.",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are WikiAssistant, a concise and friendly research helper. " +
        "When a question asks about facts, people, places, or concepts, use the SearchWiki tool to find " +
        "relevant pages and the GetWikiPage tool to read a page summary before answering. " +
        "Always base factual answers on what the tools return and mention the page title you used.";

    /// <inheritdoc />
    public override IList<AITool> Tools => wiki.AsTools();
}
