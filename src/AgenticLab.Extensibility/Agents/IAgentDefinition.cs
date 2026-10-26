using Microsoft.Extensions.AI;

namespace AgenticLab.Extensibility.Agents;

/// <summary>The side effects an agent's available tools can have, independent of its prompt.</summary>
public enum AgentRiskLevel
{
    /// <summary>No tools with side effects.</summary>
    None,
    /// <summary>Read-only or computational tools.</summary>
    Low,
    /// <summary>Confined state changes without arbitrary command execution.</summary>
    Medium,
    /// <summary>File mutation and command execution on the host.</summary>
    High,
}

/// <summary>A stateless agent definition composed by the host with its configured model client.</summary>
public interface IAgentDefinition
{
    /// <summary>The unique, case-insensitive agent name.</summary>
    string Name { get; }
    /// <summary>The agent's user-facing purpose.</summary>
    string Description { get; }
    /// <summary>The composed harness and persona instructions.</summary>
    string Instructions { get; }
    /// <summary>Composes instructions with an optional replacement harness, retaining the persona.</summary>
    string InstructionsWith(string? harnessOverride);
    /// <summary>The uncomposed harness instructions, available for inspection.</summary>
    string HarnessPrompt { get; }
    /// <summary>An optional deployment preference; host configuration takes precedence.</summary>
    string? ModelId { get; }
    /// <summary>Whether the host must open a validated workspace before running the agent.</summary>
    bool RequiresWorkspace { get; }
    /// <summary>Whether the host supplies the active workspace's skills catalogue.</summary>
    bool SupportsSkills { get; }
    /// <summary>Whether MCP rediscovery requires rebuilding the agent's tools.</summary>
    bool SupportsMcp { get; }
    /// <summary>Whether the agent uses remote A2A specialists.</summary>
    bool SupportsA2A { get; }
    /// <summary>The impact permitted by the tool implementations.</summary>
    AgentRiskLevel RiskLevel { get; }
    /// <summary>Safety mechanisms enforced by code, not merely requested in instructions.</summary>
    IReadOnlyList<string> Guardrails { get; }
    /// <summary>Example messages a client may offer to pre-fill its composer; never sent to the model as instructions.</summary>
    IReadOnlyList<string> SuggestedPrompts { get; }
    /// <summary>The bounded tool set offered to this agent.</summary>
    IList<AITool> Tools { get; }
}