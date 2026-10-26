using AgenticLab.AiService.Demo.Agents;

namespace AgenticLab.AiService.Demo.Vendors;

/// <summary>
/// The non-brand <c>Default</c> vendor. Unlike the brand vendors it supplies <strong>no</strong> harness
/// override (<see cref="Harness"/> is empty), so an agent run under it keeps its own harness. This type
/// exists only to hold the Default vendor's metadata (its display name and the agent modes it offers) in
/// the same place as the brand vendors, so the web client can load it uniformly via <c>GET /vendors</c>
/// instead of hard-coding it.
/// </summary>
public sealed class DefaultHarness : IVendorHarness
{
    /// <inheritdoc />
    public string Key => "default";

    /// <summary>
    /// Empty: the Default vendor does not replace the agent's harness. <see cref="VendorHarnessCatalog"/>
    /// treats an empty harness as "no override", so <c>Resolve("default")</c> returns <c>null</c> and the
    /// agent keeps its own harness.
    /// </summary>
    public string Harness => string.Empty;

    /// <inheritdoc />
    public string DisplayName => "Default";

    /// <summary>Empty so clients show the configured provider and model rather than a simulated label.</summary>
    public string ModelLabel => string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<VendorMode> Modes { get; } = new[]
    {
        new VendorMode(ChatAgent.AgentName, "chat"),
        new VendorMode(WikiAssistantAgent.AgentName, "wiki"),
        new VendorMode(TimeKeeperAgent.AgentName, "time"),
        new VendorMode(OrchestratorAgent.AgentName, "orchestrator"),
        // Workspace modes; previously offered only by the removed coding-assistant example hosts.
        new VendorMode(SharedAgentNames.Ask, "ask"),
        new VendorMode(SharedAgentNames.Plan, "plan"),
        new VendorMode(SharedAgentNames.Coder, "coder"),
    };
}
