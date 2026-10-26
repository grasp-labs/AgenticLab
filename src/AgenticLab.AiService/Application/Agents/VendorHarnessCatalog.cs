namespace AgenticLab.AiService.Application.Agents;

/// <summary>
/// Resolves a vendor/brand key (sent by the web flow page when a brand theme is selected) to a
/// vendor-flavoured <em>harness</em> system prompt that replaces the shared
/// <see cref="AgentDefinitionBase.Harness"/> for a single run. This lets the demo show how the same
/// agent persona behaves under a different vendor's framing — the harness layer is swapped while the
/// agent's own persona (<c>&lt;agentMode&gt;</c>) is kept.
/// </summary>
/// <remarks>
/// The catalog holds no prompt content itself: the per-vendor prompts are supplied by the injected
/// <see cref="IVendorHarness"/> implementations, whose <strong>original, representative</strong> content
/// lives in opt-in example modules (so the Application layer stays free of example dependencies).
/// Those prompts are written in each vendor's spirit — not the vendors' real,
/// proprietary system prompts — and each keeps the essential tool-grounding operating rules so the agents
/// keep functioning. Keys are matched case-insensitively; an unknown or blank key resolves to <c>null</c>,
/// as does a vendor that supplies an <strong>empty</strong> harness (the non-brand <c>Default</c> vendor,
/// which is listed for its metadata but contributes no override), leaving the agent's own harness in place.
/// </remarks>
public sealed class VendorHarnessCatalog
{
    private readonly IReadOnlyDictionary<string, string> _harnesses;
    private readonly IReadOnlyList<IVendorHarness> _vendors;

    /// <summary>
    /// Builds the <c>vendor key → harness prompt</c> map from the registered vendor harnesses. A later
    /// registration with a duplicate key replaces an earlier one. Entries with a blank key are ignored, and
    /// a vendor that supplies an empty harness (the Default vendor) is listed in <see cref="Vendors"/> for
    /// its metadata but contributes no override.
    /// </summary>
    /// <param name="vendors">The per-vendor harness providers to expose.</param>
    public VendorHarnessCatalog(IEnumerable<IVendorHarness> vendors)
    {
        var keyed = vendors.Where(v => !string.IsNullOrWhiteSpace(v.Key)).ToList();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vendor in keyed)
        {
            if (!string.IsNullOrWhiteSpace(vendor.Harness))
            {
                map[vendor.Key.Trim()] = vendor.Harness;
            }
        }

        _harnesses = map;
        _vendors = keyed;
    }

    /// <summary>
    /// The registered vendors (those with a non-blank key), in registration order. Carries each vendor's
    /// full metadata (key, display name, simulated model label and the modes it offers) so a client can
    /// build the vendor picker without hard-coding the data. Includes the non-brand <c>Default</c> vendor.
    /// </summary>
    public IReadOnlyList<IVendorHarness> Vendors => _vendors;


    /// <summary>
    /// Resolves the harness prompt for a vendor key, or <c>null</c> when the key is null, blank or
    /// unknown (in which case the agent keeps its own harness).
    /// </summary>
    /// <param name="vendor">The vendor/brand key, e.g. <c>microsoft365</c>.</param>
    /// <returns>The replacement harness text, or <c>null</c> to leave the agent's harness unchanged.</returns>
    public string? Resolve(string? vendor) =>
        !string.IsNullOrWhiteSpace(vendor) && _harnesses.TryGetValue(vendor.Trim(), out var harness)
            ? harness
            : null;
}
