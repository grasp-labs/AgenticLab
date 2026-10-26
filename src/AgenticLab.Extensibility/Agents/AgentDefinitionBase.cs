using Microsoft.Extensions.AI;

namespace AgenticLab.Extensibility.Agents;

/// <summary>Layers the shared host instructions with an agent's persona and bounded capabilities.</summary>
public abstract class AgentDefinitionBase : IAgentDefinition
{
    /// <summary>The host's operating rules; examples may override them without changing tool authority.</summary>
    protected virtual string Harness =>
        "You are the model inside an automated agent harness: together you and the harness form the agent. " +
        "The harness assembles your context, exposes a bounded set of tools, runs the think→act→observe " +
        "loop on your behalf, executes the tool calls you request, and relays the results back to you and " +
        "your replies back to the user. Follow these rules on every turn: " +
        "ground your answers in what the tools return and never fabricate facts, figures, or sources; " +
        "prefer calling a tool over answering from memory when a tool can verify the answer; " +
        "be accurate and concise, and be transparent about which tool or source you used; " +
        "if the tools return nothing useful, say so plainly instead of guessing; " +
        "keep your formatting clean and easy to read.";

    /// <summary>The agent-specific instructions added after the host's rules.</summary>
    protected abstract string Persona { get; }
    /// <inheritdoc />
    public abstract string Name { get; }
    /// <inheritdoc />
    public abstract string Description { get; }
    /// <inheritdoc />
    public virtual string? ModelId => null;
    /// <inheritdoc />
    public virtual bool RequiresWorkspace => false;
    /// <inheritdoc />
    public virtual bool SupportsSkills => false;
    /// <inheritdoc />
    public virtual bool SupportsMcp => false;
    /// <inheritdoc />
    public virtual bool SupportsA2A => false;
    /// <inheritdoc />
    public virtual AgentRiskLevel RiskLevel => AgentRiskLevel.None;
    /// <inheritdoc />
    public virtual IReadOnlyList<string> Guardrails => Array.Empty<string>();
    /// <inheritdoc />
    public virtual IReadOnlyList<string> SuggestedPrompts => Array.Empty<string>();
    /// <inheritdoc />
    public abstract IList<AITool> Tools { get; }
    /// <inheritdoc />
    public string Instructions => InstructionsWith(null);

    /// <inheritdoc />
    public string InstructionsWith(string? harnessOverride)
    {
        var harness = string.IsNullOrWhiteSpace(harnessOverride) ? Harness : harnessOverride;
        return $"<harnessMode>\n{harness}\n</harnessMode>\n\n<agentMode>\n{Persona}\n</agentMode>";
    }

    /// <inheritdoc />
    public string HarnessPrompt => Harness;
}