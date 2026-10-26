using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Agents;

/// <summary>
/// Builds and resolves the set of selectable agents from their <see cref="IAgentDefinition"/>s, all sharing
/// one configured provider with cached per-model clients. Agents are stateless and built once at construction.
/// </summary>
public sealed class AgentCatalog
{
    private readonly Dictionary<string, AIAgent> _agents;
    private readonly Dictionary<string, AgentBuild> _builds;
    private readonly Dictionary<string, bool> _requiresWorkspace;
    private readonly Dictionary<string, bool> _supportsSkills;

    // The pieces needed to rebuild an agent with a different (vendor) harness for a single run:
    // the chat client it runs on and the definition that composes its instructions and tools.
    private sealed record AgentBuild(Microsoft.Extensions.AI.IChatClient Client, IAgentDefinition Definition);

    /// <summary>
    /// Composes one <see cref="ChatClientAgent"/> per definition, keyed by name (case-insensitive).
    /// The first definition is treated as the default. Each agent's <see cref="AgentInfo.ModelId"/> is its
    /// declared model (see <see cref="ChatClientProvider.ResolveDeployment"/>) for display, but it
    /// runs on the client for its <see cref="ChatClientProvider.ExecutionDeployment"/>. Force-default
    /// configuration makes the declared model display-only, retaining the legacy Azure setting as a fallback.
    /// </summary>
    /// <param name="clients">Provides the selected backend's chat client for each agent's model or deployment.</param>
    /// <param name="definitions">The agent definitions to expose.</param>
    /// <exception cref="ArgumentException">Thrown when no definitions are supplied.</exception>
    public AgentCatalog(ChatClientProvider clients, IEnumerable<IAgentDefinition> definitions)
    {
        var list = definitions.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("At least one agent definition is required.", nameof(definitions));
        }

        var deployments = list.ToDictionary(d => d.Name, clients.ResolveDeployment, StringComparer.OrdinalIgnoreCase);
        _agents = new Dictionary<string, AIAgent>(StringComparer.OrdinalIgnoreCase);
        _builds = new Dictionary<string, AgentBuild>(StringComparer.OrdinalIgnoreCase);
        _requiresWorkspace = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        _supportsSkills = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in list)
        {
            var client = clients.Get(clients.ExecutionDeployment(deployments[definition.Name]));
            _agents[definition.Name] = new ChatClientAgent(
                client,
                instructions: definition.Instructions,
                name: definition.Name,
                tools: definition.Tools);
            _builds[definition.Name] = new AgentBuild(client, definition);
            _requiresWorkspace[definition.Name] = definition.RequiresWorkspace;
            _supportsSkills[definition.Name] = definition.SupportsSkills;
        }

        DefaultName = list[0].Name;
        Agents = list.Select(d => new AgentInfo(
            d.Name,
            d.Description,
            d.Tools.OfType<AIFunction>().Select(f => f.Name).ToList(),
            d.RequiresWorkspace,
            d.SupportsSkills,
            d.SupportsMcp,
            d.SupportsA2A,
            d.RiskLevel.ToString(),
            d.Guardrails,
            deployments[d.Name],
            Array.Empty<ToolMapping>(),
            Provider: clients.ProviderName,
            SuggestedPrompts: d.SuggestedPrompts)).ToList();
    }

    /// <summary>The name of the agent used when a request does not specify one.</summary>
    public string DefaultName { get; }

    /// <summary>The available agents, in registration order.</summary>
    public IReadOnlyList<AgentInfo> Agents { get; private set; }

    /// <summary>
    /// Rebuilds the cached agents whose tools come from a discovery source (MCP or A2A) so a re-discovery
    /// takes effect on already-built agents. Each agent bakes its <see cref="IAgentDefinition.Tools"/> when
    /// it is constructed, and MCP/A2A agents expose the discovered tools by reference; after a re-discovery
    /// reassigns those tool lists, the agents must be rebuilt from their retained build (chat client +
    /// definition) so they call the freshly discovered tools. Agents that do not use discovery are left
    /// untouched.
    /// </summary>
    public void RefreshDiscoveryAgents()
    {
        foreach (var (name, build) in _builds)
        {
            if (!build.Definition.SupportsMcp && !build.Definition.SupportsA2A)
            {
                continue;
            }

            _agents[name] = new ChatClientAgent(
                build.Client,
                instructions: build.Definition.Instructions,
                name: build.Definition.Name,
                tools: build.Definition.Tools);
        }

        Agents = Agents.Select(info => _builds.TryGetValue(info.Name, out var build)
            && (build.Definition.SupportsMcp || build.Definition.SupportsA2A)
                ? info with { Tools = build.Definition.Tools.OfType<AIFunction>().Select(tool => tool.Name).ToArray() }
                : info).ToArray();
    }

    /// <summary>Resolves the agent by name (case-insensitive), or the default when <paramref name="name"/> is null/blank.</summary>
    /// <param name="name">The requested agent name, or null/blank for the default.</param>
    /// <param name="agent">The resolved agent when found.</param>
    /// <param name="resolvedName">The name of the resolved agent when found.</param>
    /// <returns><c>true</c> when an agent was resolved; otherwise <c>false</c>.</returns>
    public bool TryResolve(string? name, out AIAgent agent, out string resolvedName)
    {
        resolvedName = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        return _agents.TryGetValue(resolvedName, out agent!);
    }

    /// <summary>
    /// Resolves the agent by name, replacing its shared harness with <paramref name="harnessOverride"/>
    /// for this run. When the override is null or blank the cached agent (its own harness) is returned;
    /// otherwise a transient <see cref="ChatClientAgent"/> is built on the same chat client with the
    /// agent's persona layered on the overridden harness. The transient agent is stateless and safe to
    /// use for a single run; conversation sessions are interchangeable across agent instances.
    /// </summary>
    /// <param name="name">The requested agent name, or null/blank for the default.</param>
    /// <param name="harnessOverride">The replacement harness text, or null/blank to keep the agent's own.</param>
    /// <param name="agent">The resolved (possibly harness-overridden) agent when found.</param>
    /// <param name="resolvedName">The name of the resolved agent when found.</param>
    /// <returns><c>true</c> when an agent was resolved; otherwise <c>false</c>.</returns>
    public bool TryResolve(string? name, string? harnessOverride, out AIAgent agent, out string resolvedName)
    {
        resolvedName = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        if (string.IsNullOrWhiteSpace(harnessOverride))
        {
            return _agents.TryGetValue(resolvedName, out agent!);
        }

        if (!_builds.TryGetValue(resolvedName, out var build))
        {
            agent = null!;
            return false;
        }

        agent = new ChatClientAgent(
            build.Client,
            instructions: build.Definition.InstructionsWith(harnessOverride),
            name: build.Definition.Name,
            tools: build.Definition.Tools);
        return true;
    }

    /// <summary>
    /// The bare harness (system) prompt the named agent runs under, or <c>null</c> when the name is unknown.
    /// Resolves the default agent when <paramref name="name"/> is null/blank. Surfaced so a client can show
    /// the active system prompt before a run (a selected vendor harness replaces it).
    /// </summary>
    /// <param name="name">The requested agent name, or null/blank for the default.</param>
    /// <returns>The agent's harness prompt, or <c>null</c> when no such agent exists.</returns>
    public string? HarnessFor(string? name)
    {
        var resolved = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        return _builds.TryGetValue(resolved, out var build) ? build.Definition.HarnessPrompt : null;
    }

    /// <summary>Whether the named agent requires a workspace path. Unknown names return <c>false</c>.</summary>
    /// <param name="name">The resolved agent name.</param>
    /// <returns><c>true</c> when the agent requires a workspace; otherwise <c>false</c>.</returns>
    public bool RequiresWorkspace(string name) =>
        _requiresWorkspace.TryGetValue(name, out var requires) && requires;

    /// <summary>Whether the named agent uses workspace skills. Unknown names return <c>false</c>.</summary>
    /// <param name="name">The resolved agent name.</param>
    /// <returns><c>true</c> when the agent participates in workspace skills; otherwise <c>false</c>.</returns>
    public bool SupportsSkills(string name) =>
        _supportsSkills.TryGetValue(name, out var supports) && supports;
}
