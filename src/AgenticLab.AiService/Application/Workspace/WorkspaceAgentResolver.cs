using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Workspace;

/// <summary>
/// Resolves and builds the user-authored agents declared in the active workspace's <c>agents/</c>
/// folder into runnable <see cref="AIAgent"/>s. Unlike the built-in agents in <see cref="AgentCatalog"/>
/// (registered once at start-up), these are loaded per request from the workspace by the
/// <see cref="WorkspaceAgentLoader"/> and wired to the same shared chat client. A workspace agent may
/// only use the harness's bounded tool set (file, terminal, skills and ask-question tools); a tool name
/// it lists that is not part of that set is silently dropped, so it can never grant itself a tool the
/// platform does not already expose. Discovery and building both require an active
/// <see cref="WorkspaceScope"/>.
/// </summary>
public sealed class WorkspaceAgentResolver
{
    private readonly ChatClientProvider _clients;
    private readonly WorkspaceAgentLoader _loader;
    private readonly IReadOnlyDictionary<string, AIFunction> _registry;

    /// <summary>
    /// Builds the registry of tool functions a workspace agent may reference, keyed by function name
    /// (case-insensitive), from the harness's application tools.
    /// </summary>
    public WorkspaceAgentResolver(
        ChatClientProvider clients,
        WorkspaceAgentLoader loader,
        FileSystemTool files,
        TerminalTool terminal,
        SkillsTool skills,
        AskQuestionTool ask,
        WebFetchTool web)
    {
        _clients = clients;
        _loader = loader;
        _registry = new[] { files.AsTools(), terminal.AsTools(), skills.AsTools(), ask.AsTools(), web.AsTools() }
            .SelectMany(t => t)
            .OfType<AIFunction>()
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lists the agents declared in the active workspace as user-facing <see cref="AgentInfo"/> summaries
    /// (with only the resolvable tools shown). Returns an empty list when none are declared. Must be
    /// called while the workspace scope is active.
    /// </summary>
    /// <returns>The workspace agents, ordered by name.</returns>
    public IReadOnlyList<AgentInfo> ListAgents() =>
        _loader.Load()
            .Select(d =>
            {
                var tools = ResolveTools(d.ToolNames);
                var agent = new WorkspaceDefinedAgent(d, tools);
                return new AgentInfo(
                    d.Name,
                    d.Description,
                    tools.OfType<AIFunction>().Select(f => f.Name).ToList(),
                    RequiresWorkspace: true,
                    agent.SupportsSkills,
                    SupportsMcp: false,
                    SupportsA2A: false,
                    d.RiskLevel.ToString(),
                    d.Guardrails,
                    _clients.ResolveDeployment(agent),
                    d.ToolMappings,
                    Provider: _clients.ProviderName,
                    SuggestedPrompts: d.SuggestedPrompts);
            })
            .ToList();

    /// <summary>
    /// Resolves a workspace agent by name (case-insensitive) and builds it into a runnable agent on the
    /// shared chat client. Must be called while the workspace scope is active.
    /// </summary>
    /// <param name="name">The requested agent name.</param>
    /// <param name="agent">The built agent when found.</param>
    /// <param name="definition">The resolved definition when found (its skills/risk metadata).</param>
    /// <param name="harnessOverride">A replacement harness prompt for this run, or null/blank to keep the agent's own.</param>
    /// <returns><c>true</c> when a workspace agent with the name was found; otherwise <c>false</c>.</returns>
    public bool TryResolve(string? name, out AIAgent agent, out WorkspaceAgentDefinition definition, string? harnessOverride = null)
    {
        agent = null!;
        definition = null!;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var match = _loader.Load()
            .FirstOrDefault(d => string.Equals(d.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        definition = match;
        var tools = ResolveTools(match.ToolNames);
        var adapter = new WorkspaceDefinedAgent(match, tools);
        agent = new ChatClientAgent(
            _clients.Get(_clients.ExecutionDeployment(_clients.ResolveDeployment(adapter))),
            instructions: adapter.InstructionsWith(harnessOverride),
            name: match.Name,
            tools: tools);
        return true;
    }

    // Maps the agent's declared tool names to the registered tool functions, dropping any that are unknown.
    // Every workspace agent supports workspace skills, so the ReadSkill tool is always granted (even when the
    // agent file does not list it) — otherwise the model can only see the <skills> catalogue but has no way to
    // load a skill's body and resorts to guessing a file path with ReadFile.
    private IList<AITool> ResolveTools(IReadOnlyList<string> toolNames)
    {
        var tools = new List<AITool>();
        foreach (var toolName in toolNames)
        {
            if (_registry.TryGetValue(toolName, out var function))
            {
                tools.Add(function);
            }
        }

        if (_registry.TryGetValue(nameof(SkillsTool.ReadSkill), out var readSkill) &&
            !tools.Contains(readSkill))
        {
            tools.Add(readSkill);
        }

        return tools;
    }
}
