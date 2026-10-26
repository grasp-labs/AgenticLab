namespace AgenticLab.Web.Flow;

/// <summary>
/// How the Agent host node and the Model node present themselves
/// for the current vendor and display options — their labels and the active system (harness) prompt fetched
/// from the service, shown in the anatomy's System Prompt box as a preview or the full text.
/// </summary>
internal sealed class HarnessView(FlowViewState owner, Action notify)
{
    private string _promptText = string.Empty;
    private bool _showFullPrompt;
    private int _promptVersion = -1;
    private bool _loading;

    /// <summary>The host node's title: the vendor name when selected, otherwise Agent host.</summary>
    public string Label => owner.VendorKey == "default"
        ? "Agent host"
        : owner.Roster.VendorName;

    /// <summary>Technical labels expose the implementation and selected agent.</summary>
    public string Subtitle => owner.Diagram.ShowTechnicalLabels
        ? $"Agent host · AiService · {owner.SelectedAgent ?? "Agent"}"
        : "Agent service";

    /// <summary>Technical labels expose the actual deployment; branded overview labels are explicitly simulated.</summary>
    public string LlmModelLabel
    {
        get
        {
            var simulated = owner.Roster.CurrentVendorInfo?.ModelLabel ?? string.Empty;
            if (owner.VendorKey != "default" && owner.Roster.CurrentVendorInfo?.ExampleId is null
                && !owner.Diagram.ShowTechnicalLabels && simulated.Length > 0)
            {
                return $"{simulated} (simulated)";
            }

            return ConfiguredModelLabel;
        }
    }

    /// <summary>
    /// The configured provider and the selected agent's declared model, as reported by the service. With
    /// <c>Models:ForceDefaultModel</c> the provider's default model executes instead; see docs/agents.md.
    /// </summary>
    public string ConfiguredModelLabel
    {
        get
        {
            var deployment = owner.Agent.Info?.ModelId;
            return string.IsNullOrWhiteSpace(deployment) ? ProviderLabel : $"{ProviderLabel} · {deployment}";
        }
    }

    private string ProviderLabel => owner.Agent.Info?.Provider is { Length: > 0 } name ? name : "Model";

    /// <summary>
    /// A host's own illustrative label; otherwise the selected agent's declared model for the current host, and
    /// only the provider for other hosts, whose agents may run on different models.
    /// </summary>
    public string HostModelLabel(VendorInfo host) =>
        !string.IsNullOrWhiteSpace(host.ModelLabel) ? host.ModelLabel
        : string.Equals(host.Key, owner.VendorKey, StringComparison.OrdinalIgnoreCase) ? ConfiguredModelLabel
        : ProviderLabel;

    // --- System prompt ------------------------------------------------------

    /// <summary>The descriptive fallback for the System Prompt box before the real text has been fetched.</summary>
    public string PromptBody => owner.VendorKey == "default"
        ? "Agent host instructions: operating guidance and the tool loop"
        : $"{owner.Roster.VendorName} system prompt: replaces the shared host instructions for this run (persona kept)";

    /// <summary>The actual harness (system) prompt text for the selected vendor and agent, fetched from the service.</summary>
    public string PromptText => _promptVersion == owner.ConfigurationVersion ? _promptText : string.Empty;

    public bool HasPromptText => !string.IsNullOrWhiteSpace(PromptText);

    /// <summary>Distinguishes loading and unavailable text from a real fetched prompt.</summary>
    public string PromptAvailability => _promptVersion != owner.ConfigurationVersion
        ? "Prompt not loaded for this selection."
        : _loading ? "Loading host instructions..." : "Host instructions unavailable.";

    /// <summary>Clears old text before fetching the current selection's prompt.</summary>
    public void BeginPromptLoad()
    {
        _promptVersion = owner.ConfigurationVersion;
        _promptText = string.Empty;
        _loading = true;
        notify();
    }

    /// <summary>Whether the anatomy shows the full system prompt text (vs. a short preview).</summary>
    public bool ShowFullPrompt
    {
        get => _showFullPrompt;
        set { _showFullPrompt = value; notify(); }
    }

    /// <summary>The fetched text as a short preview (or the full text when expanded), else the descriptive fallback.</summary>
    public string PromptDisplay
    {
        get
        {
            if (!HasPromptText)
            {
                return PromptBody;
            }

            var text = _promptText.Trim();
            if (_showFullPrompt || text.Length <= 160)
            {
                return text;
            }

            return text[..160].TrimEnd() + "…";
        }
    }

    /// <summary>Replaces the fetched prompt (after the vendor or agent changes), collapsing any expanded full view.</summary>
    public void SetPrompt(string? prompt)
    {
        _promptVersion = owner.ConfigurationVersion;
        _loading = false;
        _promptText = prompt ?? string.Empty;
        _showFullPrompt = false;
        notify();
    }
}
