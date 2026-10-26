using Microsoft.Extensions.AI;
using AgenticLab.AiService.Demo.Tools;

namespace AgenticLab.AiService.Demo.Agents;

/// <summary>
/// A patient math tutor that solves and explains arithmetic problems using the calculator tool.
/// </summary>
public sealed class MathTutorAgent(CalculatorTool calculator) : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = "MathTutor";

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Patient tutor that solves and explains arithmetic step by step.";

    /// <inheritdoc />
    /// <remarks>Low risk: pure computation with no side effects on the user's environment.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Calculator only — no file-system, command or network access",
        "No changes to your environment",
        "Individual tools can be toggled off per run",
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "What is 17% of 2,450? Show the steps.",
        "Evaluate (3 + 5) * 12 / 4 and explain the order of operations.",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are MathTutor, a patient and encouraging math tutor. " +
        "When a question involves arithmetic, use the Calculate tool to evaluate the expression rather than " +
        "computing it in your head, then explain the steps clearly so the user understands the result. " +
        "Keep explanations short and approachable.";

    /// <inheritdoc />
    public override IList<AITool> Tools => calculator.AsTools();
}
