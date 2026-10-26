namespace AgenticLab.Web.Flow;

/// <summary>
/// Everything the diagram and controls derive from the <em>selected agent's</em> <see cref="AgentInfo"/>:
/// its capabilities (workspace / skills / MCP / A2A / instructions), risk level and guardrails, environment
/// badge, tool list with risk tiers and alias mappings, and description.
/// </summary>
internal sealed class SelectedAgentView(FlowViewState owner)
{
    /// <summary>The selected agent's info, from the registered or workspace roster; null before load or when unknown.</summary>
    public AgentInfo? Info => owner.Roster.Find(owner.SelectedAgent);

    public bool RequiresWorkspace => Info?.RequiresWorkspace ?? false;
    public bool SupportsSkills => Info?.SupportsSkills ?? false;
    public bool SupportsMcp => Info?.SupportsMcp ?? false;
    public bool SupportsA2A => Info?.SupportsA2A ?? false;

    /// <summary>Custom instructions apply to any agent that runs with a workspace.</summary>
    public bool SupportsInstructions => Info?.RequiresWorkspace ?? false;

    /// <summary>The agent's persona/description, shown in the harness anatomy view.</summary>
    public string Description => Info?.Description ?? "—";

    /// <summary>Example messages the agent advertises; the composer offers them as pre-fill buttons and never sends them.</summary>
    public IReadOnlyList<string> SuggestedPrompts => Info?.SuggestedPrompts ?? Array.Empty<string>();

    // --- Risk & environment ------------------------------------------------

    /// <summary>The risk level (None/Low/Medium/High) surfaced in the environment &amp; risk view.</summary>
    public string RiskLevel => Info?.RiskLevel ?? "None";

    /// <summary>The guardrails enforced for the agent, shown as chips.</summary>
    public IReadOnlyList<string> Guardrails => Info?.Guardrails ?? Array.Empty<string>();

    /// <summary>The CSS modifier for the risk meter (risk-none/-low/-medium/-high).</summary>
    public string RiskLevelClass => $"risk-{RiskLevel.ToLowerInvariant()}";

    /// <summary>How many of the three risk-meter segments are filled.</summary>
    public int RiskFilledSegments => VendorCatalog.RiskFilledSegments(RiskLevel);

    /// <summary>The environment badge: workspace agents run on your machine with file + shell access; the rest as a server process.</summary>
    public string EnvLabel => RequiresWorkspace
        ? "💻 Your machine — file + shell access"
        : "🖧 Server process — no local access";

    public string EnvTitle => RequiresWorkspace
        ? "This agent runs on your behalf on this machine, with access to the workspace files and the shell."
        : "This agent runs as a service process; it has no access to your local files or shell.";

    /// <summary>The badge colour class: amber for an agent with local machine reach, neutral otherwise.</summary>
    public string EnvBadgeClass => RequiresWorkspace ? "env-machine" : "env-server";

    // --- Tools -------------------------------------------------------------

    /// <summary>The full tool list, used to render the per-tool on/off checkboxes and chips.</summary>
    public IReadOnlyList<string> ToolNames => Info?.Tools ?? Array.Empty<string>();

    /// <summary>A compact count of enabled tools for the harness Tools box, e.g. "6 tools" or "4 of 6 tools"; "—" when none.</summary>
    public string ToolsSummary
    {
        get
        {
            var tools = Info?.Tools;
            if (tools is not { Count: > 0 })
            {
                return "—";
            }

            var total = tools.Count;
            var enabled = tools.Count(owner.Options.IsToolEnabled);
            var label = total == 1 ? "tool" : "tools";
            return enabled == total ? $"{total} {label}" : $"{enabled} of {total} {label}";
        }
    }

    /// <summary>The CSS risk class (risk-low/-medium/-high) for a tool, colour-coding it by how much it can do.</summary>
    public string ToolRiskClass(string tool) => owner.Roster.RiskFor(tool)?.Level switch
    {
        "High" => "risk-high",
        "Medium" => "risk-medium",
        "Low" => "risk-low",
        _ => ToolRiskCatalog.CssClass(tool),
    };

    /// <summary>A tooltip explaining a tool's risk tier and why.</summary>
    public string ToolRiskTitle(string tool) => $"{tool} — {owner.Roster.RiskFor(tool)?.Description ?? ToolRiskCatalog.Reason(tool)}";

    /// <summary>
    /// The declared token(s) from a workspace agent file that mapped to the given backend tool, or null
    /// when the tool was not produced by an alias mapping.
    /// </summary>
    public string? ToolMappedFrom(string tool)
    {
        var declared = Info?.ToolMappings?
            .Where(m => string.Equals(m.Mapped, tool, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(m.Declared, tool, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Declared)
            .ToList();
        return declared is { Count: > 0 } ? string.Join(", ", declared) : null;
    }

    /// <summary>
    /// The declared→mapped tool lines for a workspace agent, shown so the alias mapping is visible. Empty
    /// unless at least one token was aliased or dropped; dropped tokens are listed last.
    /// </summary>
    public IReadOnlyList<ToolMappingLine> ToolMappingLines
    {
        get
        {
            var mappings = Info?.ToolMappings;
            if (mappings is not { Count: > 0 })
            {
                return Array.Empty<ToolMappingLine>();
            }

            var interesting = mappings.Any(m =>
                m.Mapped is null || !string.Equals(m.Declared, m.Mapped, StringComparison.OrdinalIgnoreCase));
            if (!interesting)
            {
                return Array.Empty<ToolMappingLine>();
            }

            var resolved = mappings
                .Where(m => m.Mapped is not null)
                .GroupBy(m => m.Mapped!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ToolMappingLine(g.Select(m => m.Declared).ToList(), g.Key, Dropped: false));

            var dropped = mappings
                .Where(m => m.Mapped is null)
                .Select(m => new ToolMappingLine([m.Declared], null, Dropped: true));

            return resolved.Concat(dropped).ToList();
        }
    }
}
