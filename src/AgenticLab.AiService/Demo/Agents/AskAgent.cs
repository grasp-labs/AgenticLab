using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Demo.Agents;

/// <summary>
/// A read-only "ask" mode agent: it answers questions and explains the user's workspace using only
/// the read-only file tools (<see cref="FileSystemTool.ReadFile"/> and
/// <see cref="FileSystemTool.ListFiles"/>). It declares <see cref="RequiresWorkspace"/> so the chat
/// endpoints insist on a workspace path, but it never writes, deletes or runs commands — it only reads.
/// </summary>
public sealed class AskAgent(FileSystemTool files) : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = SharedAgentNames.Ask;

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Read-only assistant that answers questions and explains code in the workspace without changing anything.";

    /// <inheritdoc />
    public override bool RequiresWorkspace => true;

    /// <inheritdoc />
    /// <remarks>Low risk: read-only file access with no writes, deletes or command execution.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Read-only file tools — never writes, deletes or runs commands",
        "Confined to the workspace folder (no ../ or absolute-path escape)",
        "Individual tools can be toggled off per run",
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "What does this workspace do? List the top-level folders and explain each.",
        "What skills and custom instructions does this workspace define?",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are Ask, a knowledgeable and concise coding assistant operating in read-only mode. " +
        "You answer questions about the user's workspace — what the code does, how it is structured, " +
        "where something lives — and explain concepts clearly. " +
        "Use the ListFiles tool to explore the project and the ReadFile tool to inspect files before you " +
        "answer, and ground every statement about the code in what those tools return. " +
        "You cannot write, delete or run anything: when a request asks for a change, explain what change " +
        "would be needed rather than attempting it.";

    /// <inheritdoc />
    public override IList<AITool> Tools => files.AsReadOnlyTools();
}
