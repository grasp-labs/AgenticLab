using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgenticLab.Web;

/// <summary>
/// Typed client for the AI service. Discovers the available agents and consumes the
/// <c>POST /chat/stream</c> Server-Sent Events endpoint that drives the flow animation.
/// </summary>
/// <param name="http">The HttpClient configured to reach the <c>aiservice</c> resource.</param>
internal sealed class AiServiceClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Lists the available agents and which one is used by default.</summary>
    public async Task<AgentsResponse?> GetAgentsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<AgentsResponse>("/agents", JsonOptions, cancellationToken);

    /// <summary>
    /// Lists the brand vendors with their metadata (display name, simulated model label and the modes each
    /// offers) so the vendor picker can be built from the service rather than hard-coded. The non-brand
    /// Default vendor is the Web app's own baseline and is not returned here.
    /// </summary>
    public async Task<VendorsResponse?> GetVendorsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<VendorsResponse>("/vendors", JsonOptions, cancellationToken);

    /// <summary>
    /// Lists the user-authored agents declared in the given workspace's agents/ folder so they can be
    /// offered alongside the built-in agents. Returns an empty list when the path is missing/invalid or
    /// the workspace declares none.
    /// </summary>
    /// <param name="workspace">The workspace path to scan for agents; null/blank yields an empty list.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<AgentsResponse?> GetWorkspaceAgentsAsync(string? workspace, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("/agents/workspace", new WorkspaceAgentsRequest(workspace), JsonOptions, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AgentsResponse>(JsonOptions, cancellationToken)
            : null;
    }

    /// <summary>
    /// Lists the immediate sub-folders of the given base folders so the workspace input can suggest repo
    /// paths (e.g. the folders under a "GitHub" directory). Returns null on failure; skips bases that do
    /// not exist.
    /// </summary>
    /// <param name="bases">The base folders to enumerate sub-directories of.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<WorkspaceBrowseResponse?> GetWorkspaceDirectoriesAsync(IReadOnlyList<string> bases, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("/workspaces", new WorkspaceBrowseRequest(bases), JsonOptions, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<WorkspaceBrowseResponse>(JsonOptions, cancellationToken)
            : null;
    }

    /// <summary>
    /// Lists the skills discovered in the given workspace (names + descriptions) so the catalogue can be
    /// shown in the harness before a run. Returns an empty list when the path is missing or declares none.
    /// </summary>
    /// <param name="workspace">The workspace path to scan for skills; null/blank yields an empty list.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<SkillsResponse?> GetSkillsAsync(string? workspace, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("/skills", new SkillsRequest(workspace), JsonOptions, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SkillsResponse>(JsonOptions, cancellationToken)
            : null;
    }

    /// <summary>
    /// Lists the custom instructions discovered in the given workspace (names + descriptions) so they can
    /// be shown in the harness anatomy before a run. Returns an empty list when the path is missing or
    /// declares none. The instructions' full content is always injected into the agent's context per run.
    /// </summary>
    /// <param name="workspace">The workspace path to scan for custom instructions; null/blank yields an empty list.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<InstructionsResponse?> GetInstructionsAsync(string? workspace, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("/instructions", new InstructionsRequest(workspace), JsonOptions, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<InstructionsResponse>(JsonOptions, cancellationToken)
            : null;
    }

    /// <summary>
    /// Gets the effective harness (system) prompt for the given agent and vendor so the harness anatomy can
    /// show the active system prompt before a run. A selected vendor's harness replaces the agent's own.
    /// </summary>
    /// <param name="agent">The selected agent, or null/blank for the default.</param>
    /// <param name="vendor">A brand/vendor key whose harness replaces the shared one; null/blank for the agent's own.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<HarnessResponse?> GetHarnessAsync(string? agent, string? vendor, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("/harness", new HarnessRequest(agent, vendor), JsonOptions, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<HarnessResponse>(JsonOptions, cancellationToken)
            : null;
    }

    /// <summary>
    /// Sends a message and yields each <see cref="FlowEvent"/> as the agent run progresses. The run is
    /// paced on the server by the matching <see cref="SendControlAsync"/> calls (keyed by session id).
    /// </summary>
    /// <param name="message">The user's message.</param>
    /// <param name="agent">The agent to use, or null/blank for the default.</param>
    /// <param name="sessionId">The unique id shared with the control calls.</param>
    /// <param name="conversationId">The conversation to continue, so the run remembers prior turns.</param>
    /// <param name="manual">When <c>true</c>, the run starts in manual stepping mode.</param>
    /// <param name="stepDelayMs">The auto-mode server-side delay applied before each step, in milliseconds.</param>
    /// <param name="workspace">The workspace path for agents that require one; null/blank otherwise.</param>
    /// <param name="disabledTools">The names of the agent's tools to hide from the model for this run; null/empty to offer them all.</param>
    /// <param name="disabledSkills">The names of the agent's skills to hide from the model for this run; null/empty to offer them all.</param>
    /// <param name="enabledInstructions">The names of the workspace custom instructions to inject this run; null/empty to inject none (they default off).</param>
    /// <param name="vendor">A brand/vendor key whose harness replaces the shared harness for this run; null/blank to keep the agent's own.</param>
    /// <param name="cancellationToken">A token to cancel the stream.</param>
    /// <param name="breakpoints">Execution boundaries that pause even in auto mode.</param>
    public async IAsyncEnumerable<FlowEvent> StreamFlowAsync(
        string message,
        string? agent,
        string sessionId,
        string conversationId,
        bool manual,
        int stepDelayMs,
        string? workspace,
        IReadOnlyList<string>? disabledTools,
        IReadOnlyList<string>? disabledSkills,
        IReadOnlyList<string>? enabledInstructions,
        string? vendor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IReadOnlyList<string>? breakpoints = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chat/stream")
        {
            Content = JsonContent.Create(new FlowChatRequest(message, agent, sessionId, conversationId, manual, stepDelayMs, workspace, disabledTools, disabledSkills, enabledInstructions, vendor, breakpoints)),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var parser = SseParser.Create(stream, (_, data) =>
            JsonSerializer.Deserialize<FlowEvent>(data, JsonOptions));

        await foreach (var item in parser.EnumerateAsync(cancellationToken))
        {
            if (item.Data is { } flowEvent)
            {
                yield return flowEvent;
            }
        }
    }

    /// <summary>
    /// Drives an in-flight flow run on the server: <c>next</c>, <c>pause</c>, <c>resume</c> or
    /// <c>stop</c>, and optionally switches mode or changes the auto delay.
    /// </summary>
    /// <param name="sessionId">The id of the run to control.</param>
    /// <param name="action">The action: <c>next</c>, <c>pause</c>, <c>resume</c>, <c>stop</c> or <c>answer</c>.</param>
    /// <param name="manual">Optionally switch the stepping mode.</param>
    /// <param name="delayMs">Optionally change the auto-mode step delay.</param>
    /// <param name="answer">The user's reply for an <c>answer</c> action (to a tool's question).</param>
    /// <param name="breakpoints">A replacement selection for future boundaries; null leaves it unchanged.</param>
    /// <param name="breakpointId">The exact pause occurrence to release with next or resume.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task SendControlAsync(
        string sessionId,
        string? action = null,
        bool? manual = null,
        int? delayMs = null,
        string? answer = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? breakpoints = null,
        string? breakpointId = null)
    {
        using var response = await http.PostAsJsonAsync(
            "/chat/control",
            new FlowControlRequest(sessionId, action, manual, delayMs, answer, breakpoints, breakpointId),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Lists the MCP servers connected to the AI service and the tools discovered from them, so the
    /// harness can show MCP discovery. Returns null on failure.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<McpResponse?> GetMcpAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("/mcp", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<McpResponse>(JsonOptions, cancellationToken)
            : null;
    }

    /// <summary>
    /// Lists the agents the AI service can reach over the A2A protocol, so the harness can show the
    /// sub-agents the selected agent can delegate to. Returns null on failure.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<A2AResponse?> GetA2AAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("/a2a", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<A2AResponse>(JsonOptions, cancellationToken)
            : null;
    }

    /// <summary>Clears a conversation's remembered history so the next message starts fresh.</summary>
    /// <param name="conversationId">The id of the conversation to reset.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <exception cref="HttpRequestException">The server could not clear the remembered history.</exception>
    public async Task ResetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            "/chat/reset",
            new ConversationResetRequest(conversationId),
            JsonOptions,
            cancellationToken);
            response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Gets a snapshot of every discovery source's status (endpoint, state, last-run time and discovered
    /// tools/agents) plus whether discovery runs at startup, so the discovery page can render the last-known
    /// result without running discovery again. Returns null on failure.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    public async Task<DiscoverySnapshotResponse?> GetDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("/discovery", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<DiscoverySnapshotResponse>(JsonOptions, cancellationToken)
            : null;
    }

    /// <summary>
    /// Runs a discovery pass on the service for the requested source (clean up, re-discover MCP tools
    /// and/or A2A agents, then refresh the discovery-using agents) and yields each
    /// <see cref="DiscoveryEventDto"/> as a Server-Sent Event so the discovery process can be visualized
    /// live. The run is paced on the server by the matching <see cref="SendControlAsync"/> calls (keyed by
    /// session id), so it can be stepped, paused, resumed or stopped.
    /// </summary>
    /// <param name="sessionId">The unique id shared with the control calls.</param>
    /// <param name="source">The source to discover: <c>"mcp"</c>, <c>"a2a"</c>, or <c>"all"</c>/null for both.</param>
    /// <param name="manual">When <c>true</c>, the run starts in manual stepping mode.</param>
    /// <param name="stepDelayMs">The auto-mode server-side delay applied before each step, in milliseconds.</param>
    /// <param name="cancellationToken">A token to cancel the stream.</param>
    public async IAsyncEnumerable<DiscoveryEventDto> StreamDiscoveryAsync(
        string sessionId,
        string? source,
        bool manual,
        int stepDelayMs,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/discovery/stream")
        {
            Content = JsonContent.Create(new DiscoveryStreamRequest(sessionId, source, manual, stepDelayMs)),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var parser = SseParser.Create(stream, (_, data) =>
            JsonSerializer.Deserialize<DiscoveryEventDto>(data, JsonOptions));

        await foreach (var item in parser.EnumerateAsync(cancellationToken))
        {
            if (item.Data is { } evt)
            {
                yield return evt;
            }
        }
    }
}

internal sealed record AgentInfo(string Name, string Description, IReadOnlyList<string> Tools, bool RequiresWorkspace = false, bool SupportsSkills = false, bool SupportsMcp = false, bool SupportsA2A = false, string RiskLevel = "None", IReadOnlyList<string>? Guardrails = null, string ModelId = "", IReadOnlyList<ToolMapping>? ToolMappings = null, string? ExampleId = null, bool RequiresExampleUi = false, string Provider = "", IReadOnlyList<string>? SuggestedPrompts = null);
internal sealed record ToolMapping(string Declared, string? Mapped);
internal sealed record AgentsResponse(IReadOnlyList<AgentInfo> Agents, string Default);
internal sealed record SkillsRequest(string? Workspace);
internal sealed record SkillsResponse(IReadOnlyList<SkillInfo> Skills);
internal sealed record SkillInfo(string Name, string Description);
internal sealed record McpResponse(IReadOnlyList<McpServerInfo> Servers);
internal sealed record McpServerInfo(string Name, IReadOnlyList<McpToolDescriptor> Tools);
internal sealed record McpToolDescriptor(string Name, string Description);
internal sealed record A2AResponse(IReadOnlyList<A2AAgentDescriptor> Agents);
internal sealed record A2AAgentDescriptor(string Name, string Description);
internal sealed record InstructionsRequest(string? Workspace);
internal sealed record InstructionsResponse(IReadOnlyList<InstructionInfo> Instructions);
internal sealed record InstructionInfo(string Name, string Description);
internal sealed record WorkspaceAgentsRequest(string? Workspace);
internal sealed record WorkspaceBrowseRequest(IReadOnlyList<string> Bases);
internal sealed record WorkspaceBrowseResponse(IReadOnlyList<WorkspaceEntry> Directories);
internal sealed record WorkspaceEntry(string Path, string Name, string Base);
internal sealed record HarnessRequest(string? Agent, string? Vendor);
internal sealed record HarnessResponse(string Prompt);
internal sealed record VendorInfo(string Key, string DisplayName, string ModelLabel, IReadOnlyList<VendorModeInfo> Modes, string? ExampleId = null, bool RequiresExampleUi = false);
internal sealed record VendorModeInfo(string Agent, string Label);
internal sealed record VendorsResponse(IReadOnlyList<VendorInfo> Vendors);
internal sealed record FlowChatRequest(string Message, string? Agent, string SessionId, string ConversationId, bool Manual, int StepDelayMs, string? Workspace = null, IReadOnlyList<string>? DisabledTools = null, IReadOnlyList<string>? DisabledSkills = null, IReadOnlyList<string>? EnabledInstructions = null, string? Vendor = null, IReadOnlyList<string>? Breakpoints = null);
internal sealed record FlowControlRequest(string SessionId, string? Action, bool? Manual, int? DelayMs, string? Answer = null, IReadOnlyList<string>? Breakpoints = null, string? BreakpointId = null);
internal sealed record BreakpointNotice(string Id, string Kind, string? Tool, bool Paused, bool Manual);
internal sealed record ConversationResetRequest(string ConversationId);
/// <summary>A captured flow step; optional usage describes only its completed local model response.</summary>
public sealed record FlowEvent(int Sequence, string Kind, string Label, string? Detail, int Turn = 0, string? Data = null, string? CallId = null, FlowToolCall? ToolCall = null, FlowTokenUsage? Usage = null);

/// <summary>Provider-reported counts; cached input is a subset of input, and null means unavailable.</summary>
public sealed record FlowTokenUsage(long? InputTokenCount, long? OutputTokenCount, long? TotalTokenCount, long? CachedInputTokenCount);

/// <summary>A captured function name and structured arguments, separate from readable display text.</summary>
public sealed record FlowToolCall(string Name, System.Text.Json.JsonElement Arguments);

/// <summary>A snapshot of every discovery source's status plus whether discovery runs at startup.</summary>
public sealed record DiscoverySnapshotResponse(bool DiscoverOnStartup, IReadOnlyList<DiscoverySourceStatusDto> Sources);

/// <summary>The last-known status of a discovery source (MCP or A2A).</summary>
public sealed record DiscoverySourceStatusDto(string Source, string? Endpoint, string State, string? Error, DateTimeOffset? LastRunUtc, IReadOnlyList<DiscoveryItemDto> Items);

/// <summary>A discovered tool (MCP) or agent (A2A): its name and description.</summary>
public sealed record DiscoveryItemDto(string Name, string Description);

/// <summary>A single streamed step of a live discovery run.</summary>
public sealed record DiscoveryEventDto(string Source, string Kind, string Message, DiscoveryItemDto? Item = null, int Sequence = 0);

/// <summary>Requests a discovery run for a source, paced by a stepping session (reusing /chat/control).</summary>
internal sealed record DiscoveryStreamRequest(string SessionId, string? Source = null, bool Manual = false, int StepDelayMs = 0);
