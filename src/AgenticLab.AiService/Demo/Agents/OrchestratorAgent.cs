using Microsoft.Extensions.AI;
using AgenticLab.AiService.Demo.Tools;

namespace AgenticLab.AiService.Demo.Agents;

/// <summary>
/// An orchestrator that answers arithmetic itself with its calculator tool, but delegates general-knowledge
/// and research questions to a separate specialist agent over the Agent2Agent (A2A) protocol. It
/// demonstrates agent-to-agent communication: one agent calling another agent (rather than a tool) across a
/// standard protocol, contrasting a locally-answered turn with a delegated one.
/// </summary>
public sealed class OrchestratorAgent(CalculatorTool calculator, A2AAgentProvider a2a) : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = "Orchestrator";

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Solves arithmetic itself and delegates research questions to a specialist agent over A2A.";

    /// <inheritdoc />
    public override bool SupportsA2A => true;

    /// <inheritdoc />
    /// <remarks>Low risk: computes locally and delegates read-only questions over A2A with no side effects.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Delegation only — the remote agents are read-only and return text, with no file, command or write access",
        "Calculator is a pure arithmetic evaluator with no side effects",
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "Ask the research agent who invented the World Wide Web and in which year, then calculate how many years before 2000 that was.",
        "Have the poet write two lines about the number 42, then calculate 42 squared.",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are Orchestrator, a coordinating assistant. You have two ways to answer: " +
        "for arithmetic, call the Calculate tool and report the result; " +
        "for anything better handled by a specialist (general knowledge, research, creative writing, etc.), " +
        "call the DelegateToAgent tool with the name of the appropriate specialist agent and the question — " +
        "the available specialists are listed in that tool's description — then relay the specialist's answer, " +
        "making clear which specialist produced it. " +
        "Do not answer a specialist's kind of question from your own memory when you can delegate it. Keep replies concise.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. calculator.AsTools(), .. a2a.GetTools()];
}
