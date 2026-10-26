using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using AgenticLab.Web.Flow;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Web.Components.Pages.FlowParts;

namespace AgenticLab.Web.Components.Pages;

/// <summary>
/// Code-behind for the live agent-flow page. Owns the two page-scoped state containers
/// (<see cref="FlowViewState"/> for view/preferences, <see cref="FlowRunController"/> for run state),
/// cascades them to the child components, and bridges their change events to Blazor re-renders.
/// State is instantiated here (not via DI) so it shares the component's circuit lifetime.
/// Panel preferences are saved after rendering to keep layout callbacks synchronous.
/// </summary>
public partial class Flow : IDisposable
{
    private const string VendorStorageKey = "agenticlab-vendor";
    private const string PanelStorageKey = "agenticlab-panels";
    private const string TokenUsageStorageKey = "agenticlab-token-usage";
    private const string WorkspaceBasesStorageKey = "agenticlab-workspace-bases";
    private const string RecentWorkspacesStorageKey = "agenticlab-workspace-recent";

    [Inject]
    private AiServiceClient Ai { get; set; } = default!;

    [Inject]
    private ConceptCatalog Concepts { get; set; } = default!;

    [Inject]
    private IOptions<ReplayRetentionOptions> Retention { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private FlowViewState _view = default!;
    private FlowRunController _run = default!;
    private bool _catalogsLoaded;
    private bool _savePanelsAfterRender;
    private bool _tokenUsagePreferenceChanged;
    private long _runRenderVersion;
    private ExamplePanelHost? _examplePanel;

    [Inject] private IEnumerable<IExampleModule> Examples { get; set; } = [];

    private IWebExample? SelectedExample => Examples.OfType<IWebExample>()
        .FirstOrDefault(example => example.Manifest.HostKeys.Contains(_view.HostKey));

    private ExamplePanelContext PanelContext => new(_run.ConversationId, _view.HostKey, _view.SelectedAgent,
        _run.Running, _run.StateVersion, text => _view.Message = text, ResetExampleConversationAsync);

    private async Task<string> ResetExampleConversationAsync()
    {
        if (_run.Running) throw new InvalidOperationException("Stop the run before starting a new conversation.");
        if (SelectedExample is not null && _examplePanel is not null) await _examplePanel.BeforeResetAsync();
        await _run.NewConversationAsync();
        return _run.ConversationId;
    }

    private async Task NewConversationAsync()
    {
        if (_run.Running) return;
        try
        {
            await ResetExampleConversationAsync();
        }
        catch (Exception ex)
        {
            _run.ReportError($"Could not reset the example: {ex.Message}");
        }
    }

    /// <summary>How much the Execution panel has captured, shown in its header.</summary>
    private string ExecutionMeta
    {
        get
        {
            var count = _run.Projections.Exchanges.Count;
            return count == 0 ? "nothing captured yet" : $"{count} exchange{(count == 1 ? "" : "s")}";
        }
    }

    private string MaximizeTitle =>
        _view.Layout.BottomPanelMaximized ? "Restore the Execution panel" : "Expand the Execution panel";

    private string HostSummary => _view.Roster.CurrentVendorInfo is { } info
        ? $"{info.DisplayName}: {_view.Harness.HostModelLabel(info)}; {info.Modes.Count} mode{(info.Modes.Count == 1 ? "" : "s")}" : _view.Roster.VendorName;

    protected override void OnInitialized()
    {
        _view = new FlowViewState(Concepts);
        _view.Roster.SetExamples(Examples.Where(example => !example.Manifest.RequiresUi || example is IWebExample)
            .Select(example => example.Manifest));
        _run = new FlowRunController(Ai, _view, Retention.Value);
        _view.Changed += OnViewChanged;
        _view.WorkspacePrefs.Changed += OnWorkspacePrefsChanged;
        _run.Changed += OnRunChangedAsync;
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var vendors = await Ai.GetVendorsAsync();
            if (vendors is not null)
            {
                _view.Roster.SetVendors(vendors.Vendors);
                _view.HostKey = _view.Roster.RestoreHost(null);
            }

            var response = await Ai.GetAgentsAsync();
            if (response is not null)
            {
                _view.Roster.SetAgents(response.Agents);
                _view.InitSelectedAgent(_view.Roster.VendorDefaultAgent ?? response.Default);
                _catalogsLoaded = true;
            }

            await _run.Catalogs.RefreshHarnessPromptAsync();
            await _run.Catalogs.RefreshKnownA2AAsync();
        }
        catch (Exception ex)
        {
            _run.ReportError($"Could not load the agent list: {ex.Message}");
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_savePanelsAfterRender)
        {
            _savePanelsAfterRender = false;
            await SavePanelsAsync();
        }

        if (!firstRender)
        {
            return;
        }

        try
        {
            var storedUsage = await JS.InvokeAsync<string?>("localStorage.getItem", TokenUsageStorageKey);
            if (!_tokenUsagePreferenceChanged)
            {
                _view.Presentation.ShowTokenUsageSummaries = bool.TryParse(storedUsage, out var visible) && visible;
            }
        }
        catch
        {
        }

        try
        {
            var stored = await JS.InvokeAsync<string?>("localStorage.getItem", VendorStorageKey);
            var restored = _view.Roster.RestoreHost(stored);
            if (restored != _view.HostKey)
            {
                _view.HostKey = restored;
                _view.SelectedAgent = _view.Roster.VendorDefaultAgent ?? _view.SelectedAgent;
                await _run.Catalogs.RefreshWorkspaceContextAsync();
                await _run.Catalogs.RefreshKnownA2AAsync();
                StateHasChanged();
            }

            var storedPanels = await JS.InvokeAsync<string?>("localStorage.getItem", PanelStorageKey);
            if (!string.IsNullOrEmpty(storedPanels) && PanelState.TryParse(storedPanels, out var panels))
            {
                _view.Layout.Init(panels.LeftCollapsed, panels.RightCollapsed, panels.BottomCollapsed,
                    panels.LeftWidth, panels.RightWidth, panels.BottomHeight, panels.AdaptiveConversationWidth);
                StateHasChanged();
            }

            var storedBases = await JS.InvokeAsync<string?>("localStorage.getItem", WorkspaceBasesStorageKey);
            var storedRecent = await JS.InvokeAsync<string?>("localStorage.getItem", RecentWorkspacesStorageKey);
            if (!string.IsNullOrEmpty(storedBases) || !string.IsNullOrEmpty(storedRecent))
            {
                var recent = string.IsNullOrEmpty(storedRecent)
                    ? Array.Empty<string>()
                    : storedRecent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                _view.WorkspacePrefs.Init(storedBases, recent);
                if (_view.WorkspacePrefs.BasePaths.Count > 0)
                {
                    await _run.Catalogs.RefreshWorkspaceSuggestionsAsync();
                }

                StateHasChanged();
            }
        }
        catch
        {
            // localStorage may be unavailable (prerender); ignore and keep the default vendor.
        }
    }

    private async Task SetTokenUsageVisibilityAsync(bool visible)
    {
        _tokenUsagePreferenceChanged = true;
        _view.Presentation.ShowTokenUsageSummaries = visible;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", TokenUsageStorageKey, visible ? "true" : "false");
        }
        catch
        {
        }
    }

    private async Task CloseDiscoveryAsync(bool refreshCatalogs)
    {
        _view.Layout.DiscoveryOpen = false;
        if (!refreshCatalogs) return;
        try
        {
            var response = await Ai.GetAgentsAsync();
            if (response is not null) _view.Roster.SetAgents(response.Agents);
        }
        catch (HttpRequestException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        await _run.Catalogs.RefreshKnownMcpAsync();
        await _run.Catalogs.RefreshKnownA2AAsync();
    }

    private Task OnHostChangedAsync(ChangeEventArgs args) =>
        args.Value?.ToString() is { } key && key != _view.HostKey && !_run.Running
            && _view.Roster.AvailableHosts.Any(host => host.Key == key)
            ? SetVendorAsync(key) : Task.CompletedTask;

    private async Task SetVendorAsync(string key)
    {
        if (key == _view.HostKey || _run.Running) return;

        _view.HostKey = key;
        _view.SelectedAgent = _view.Roster.VendorDefaultAgent ?? _view.SelectedAgent;
        await _run.NewConversationAsync();
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", VendorStorageKey, key);
        }
        catch
        {
            // Persisting the vendor is best-effort.
        }

        await _run.Catalogs.RefreshWorkspaceContextAsync();
        await _run.Catalogs.RefreshKnownA2AAsync();
    }

    private void ToggleLeftPanel()
    {
        _savePanelsAfterRender = true;
        _view.Layout.ToggleLeftPanel();
    }

    private void ToggleRightPanel()
    {
        _savePanelsAfterRender = true;
        _view.Layout.ToggleRightPanel();
    }

    private void SetLeftWidth(int width)
    {
        _savePanelsAfterRender = true;
        _view.Layout.LeftPanelWidth = width;
    }

    private void SetRightWidth(int width)
    {
        _savePanelsAfterRender = true;
        _view.Layout.RightPanelWidth = width;
    }

    private void ToggleBottomPanel()
    {
        _savePanelsAfterRender = true;
        _view.Layout.ToggleBottomPanel();
    }

    private void SetBottomHeight(int height)
    {
        _savePanelsAfterRender = true;
        _view.Layout.BottomPanelHeight = height;
    }

    private void ResetLayout()
    {
        _savePanelsAfterRender = true;
        _view.Layout.Reset();
    }

    private async Task SavePanelsAsync()
    {
        var state = new PanelState(
            _view.Layout.LeftPanelCollapsed,
            _view.Layout.RightPanelCollapsed,
            _view.Layout.BottomPanelCollapsed,
            _view.Layout.LeftPanelWidth,
            _view.Layout.RightPanelWidth,
            _view.Layout.BottomPanelHeight,
            _view.Layout.AdaptiveConversationWidth);
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", PanelStorageKey, state.Serialize());
        }
        catch
        {
            // Persisting the panel layout is best-effort.
        }
    }

    private void OnViewChanged() => _ = InvokeAsync(StateHasChanged);

    // Persists the workspace preferences (base folders + recent paths) whenever they change.
    private void OnWorkspacePrefsChanged() => _ = PersistWorkspacePrefsAsync();

    private async Task PersistWorkspacePrefsAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", WorkspaceBasesStorageKey, _view.WorkspacePrefs.Bases);
            await JS.InvokeVoidAsync("localStorage.setItem", RecentWorkspacesStorageKey, string.Join('\n', _view.WorkspacePrefs.Recent));
        }
        catch
        {
            // Persisting the workspace preferences is best-effort.
        }
    }

    private Task OnRunChangedAsync() => InvokeAsync(() =>
    {
        _runRenderVersion++;
        StateHasChanged();
    });

    public void Dispose()
    {
        _view.Changed -= OnViewChanged;
        _view.WorkspacePrefs.Changed -= OnWorkspacePrefsChanged;
        _run.Changed -= OnRunChangedAsync;
        _run.Dispose();
    }
}

/// <summary>The persisted panel layout (collapsed flags + sizes) stored in localStorage.</summary>
internal readonly record struct PanelState(
    bool LeftCollapsed,
    bool RightCollapsed,
    bool BottomCollapsed,
    int LeftWidth,
    int RightWidth,
    int BottomHeight,
    bool AdaptiveConversationWidth = false)
{
    /// <summary>Serialises the original six layout fields plus a 1/0 adaptive conversation-width flag.</summary>
    public string Serialize() =>
        $"{(LeftCollapsed ? 1 : 0)}|{(RightCollapsed ? 1 : 0)}|{(BottomCollapsed ? 1 : 0)}|{LeftWidth}|{RightWidth}|{BottomHeight}|{(AdaptiveConversationWidth ? 1 : 0)}";

    /// <summary>Accepts legacy six-field pixel layouts and the current seven-field format.</summary>
    public static bool TryParse(string? value, out PanelState state)
    {
        state = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('|');
        if (parts.Length is not (6 or 7)
            || (parts.Length == 7 && parts[6] is not ("0" or "1"))
            || !int.TryParse(parts[3], out var leftWidth)
            || !int.TryParse(parts[4], out var rightWidth)
            || !int.TryParse(parts[5], out var bottomHeight))
        {
            return false;
        }

        state = new PanelState(parts[0] == "1", parts[1] == "1", parts[2] == "1", leftWidth, rightWidth, bottomHeight,
            parts.Length == 7 && parts[6] == "1");
        return true;
    }
}
