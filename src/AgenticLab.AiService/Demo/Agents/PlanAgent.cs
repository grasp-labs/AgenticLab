using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Demo.Agents;

/// <summary>
/// A read-only "plan" mode agent: it investigates the user's workspace and produces a concrete
/// implementation plan without making any changes. It uses the read-only file tools
/// (<see cref="FileSystemTool.ReadFile"/> and <see cref="FileSystemTool.ListFiles"/>) plus
/// <see cref="AskQuestionTool.AskQuestion"/> to clarify a genuinely blocking ambiguity, and declares
/// <see cref="RequiresWorkspace"/>, but never writes, deletes or runs commands.
/// </summary>
public sealed class PlanAgent(FileSystemTool files, AskQuestionTool ask) : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = SharedAgentNames.Plan;

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Read-only planner that investigates the workspace and proposes an implementation plan without changing anything.";

    /// <inheritdoc />
    public override bool RequiresWorkspace => true;

    /// <inheritdoc />
    /// <remarks>Low risk: read-only investigation; it only proposes a plan and asks before assuming.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Read-only file tools — never writes, deletes or runs commands",
        "Confined to the workspace folder (no ../ or absolute-path escape)",
        "Asks the user before assuming on a blocking ambiguity",
        "Individual tools can be toggled off per run",
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "Plan how to add a new skill that reports the current week number, including which files to create.",
        "Propose a plan to add a custom instruction that makes every answer end with a one-line summary.",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are Plan, a careful software architect operating in read-only mode. " +
        "Your job is to turn a request into a clear, actionable implementation plan — not to make the change. " +
        "Use the ListFiles tool to map the project and the ReadFile tool to study the relevant files before " +
        "you plan, and ground every step in what those tools reveal. " +
        "Produce an ordered list of concrete steps that name the specific files to create or edit and what " +
        "each change should do, call out risks or open questions, and keep it focused on the request. " +
        "You cannot write, delete or run anything — describe the steps for someone (or another agent) to carry out. " +
        "If a genuinely blocking ambiguity prevents you from planning, call the AskQuestion tool to ask the user — " +
        "ask sparingly and only when you cannot resolve it from the workspace; otherwise proceed and state your assumptions.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. files.AsReadOnlyTools(), .. ask.AsTools()];
}
