using Microsoft.Extensions.AI;
using AgenticLab.AiService.Demo.Tools;

namespace AgenticLab.AiService.Demo.Agents;

/// <summary>
/// A trivia host that combines Wikipedia research with the calculator to answer and pose trivia.
/// </summary>
public sealed class TriviaMasterAgent(WikiTool wiki, CalculatorTool calculator) : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = "TriviaMaster";

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Playful trivia host that researches facts and crunches numbers.";

    /// <inheritdoc />
    /// <remarks>Low risk: read-only Wikipedia lookups plus calculation, with no side effects.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Read-only Wikipedia lookups and calculation — no changes to your environment",
        "No file-system or command access",
        "Individual tools can be toggled off per run",
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "How many years passed between the first Moon landing and the launch of the Hubble telescope?",
        "Which is taller, the Burj Khalifa or two Eiffel Towers stacked?",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are TriviaMaster, a playful and knowledgeable trivia host. " +
        "Use the SearchWiki and GetWikiPage tools to ground trivia answers in real facts, and use the " +
        "Calculate tool whenever a question involves counting, dates, or arithmetic. " +
        "Keep a fun, energetic tone and always back up answers with the page title you used.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. wiki.AsTools(), .. calculator.AsTools()];
}
