namespace AgenticLab.AiService.Application.Workspace;

/// <summary>
/// One declared tool token from a workspace agent file and the backend tool it resolved to, so the UI can
/// show that (and how) a VS Code-style token was mapped. <paramref name="Mapped"/> is <c>null</c> when the
/// token had no matching backend tool and was dropped.
/// </summary>
/// <param name="Declared">The tool token exactly as written in the agent file (e.g. <c>search/fileSearch</c>).</param>
/// <param name="Mapped">The backend tool name it mapped to (e.g. <c>ListFiles</c>), or <c>null</c> when dropped.</param>
public sealed record ToolMapping(string Declared, string? Mapped);

/// <summary>
/// A user-authored agent discovered in the active workspace's <c>agents/</c> folder (one
/// <c>&lt;name&gt;.agent.yaml</c> file per agent). Unlike the built-in <see cref="IAgentDefinition"/>s
/// these are not registered at start-up; they are loaded per request from the workspace and turned into
/// a runnable agent on demand by the <see cref="WorkspaceAgentResolver"/>.
/// </summary>
/// <param name="Name">The unique name used to select the agent (from the YAML <c>name</c>).</param>
/// <param name="Description">A short description of what the agent is good at.</param>
/// <param name="Persona">The agent's persona/system instructions, layered on top of the shared harness prompt.</param>
/// <param name="ToolNames">The names of the existing backend tool functions the agent may call.</param>
/// <param name="RiskLevel">How much real-world impact the agent can have.</param>
/// <param name="Guardrails">The safety mechanisms enforced for the agent, surfaced so the user understands the risk.</param>
/// <param name="SupportsSkills">Whether the agent participates in workspace skills (its catalogue is injected per run).</param>
/// <param name="ModelId">The Azure OpenAI deployment the agent should run on (from the YAML <c>model</c>), or <c>null</c> for the default.</param>
/// <param name="RelativePath">The workspace-relative path of the YAML file the agent was loaded from.</param>
/// <param name="ToolMappings">Each declared tool token and the backend tool it mapped to, so the UI can show the mapping.</param>
/// <param name="SuggestedPrompts">Example messages (from the YAML <c>prompts</c>) a client may offer to pre-fill its composer.</param>
public sealed record WorkspaceAgentDefinition(
    string Name,
    string Description,
    string Persona,
    IReadOnlyList<string> ToolNames,
    AgentRiskLevel RiskLevel,
    IReadOnlyList<string> Guardrails,
    bool SupportsSkills,
    string? ModelId,
    string RelativePath,
    IReadOnlyList<ToolMapping> ToolMappings,
    IReadOnlyList<string>? SuggestedPrompts = null);
