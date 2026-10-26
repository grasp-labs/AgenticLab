using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Workspace;

/// <summary>
/// Adapts a <see cref="WorkspaceAgentDefinition"/> (loaded from the workspace's <c>agents/</c> folder)
/// to an <see cref="IAgentDefinition"/> so it reuses the same layered system prompt as the built-in
/// agents: the shared <see cref="AgentDefinitionBase.Harness"/> followed by the YAML-supplied
/// <see cref="Persona"/>. Workspace agents always require a workspace, since their tools operate inside
/// it. Built on demand per request by the <see cref="WorkspaceAgentResolver"/>; never registered at
/// start-up.
/// </summary>
internal sealed class WorkspaceDefinedAgent(WorkspaceAgentDefinition definition, IList<AITool> tools) : AgentDefinitionBase
{
    /// <inheritdoc />
    public override string Name => definition.Name;

    /// <inheritdoc />
    public override string Description => definition.Description;

    /// <inheritdoc />
    public override bool RequiresWorkspace => true;

    /// <inheritdoc />
    // Every workspace-defined agent supports workspace skills: it already runs confined to the workspace,
    // so the skill catalogue that lives there is always relevant. (The agent file's `skills` field is
    // retained on the definition for reference but no longer gates this.)
    public override bool SupportsSkills => true;

    /// <inheritdoc />
    public override AgentRiskLevel RiskLevel => definition.RiskLevel;

    /// <inheritdoc />
    public override string? ModelId => definition.ModelId;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails => definition.Guardrails;

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts => definition.SuggestedPrompts ?? Array.Empty<string>();

    /// <inheritdoc />
    protected override string Persona => definition.Persona;

    /// <inheritdoc />
    public override IList<AITool> Tools => tools;
}
