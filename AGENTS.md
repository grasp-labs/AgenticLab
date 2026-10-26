# Agentic Lab

A .NET 10 [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) sample: AI agents backed by
Azure OpenAI, OpenAI, Gemini or Amazon Bedrock, each with its own persona and toolset, answering questions using
Wikipedia and a calculator as tools.

See [README.md](README.md) for a user-facing overview, prerequisites, local setup and contribution steps.
See [CONTRIBUTING.md](CONTRIBUTING.md) for contributor checks and publication gates, and
[SECURITY.md](SECURITY.md) for private vulnerability reporting and the trusted-local security boundary.
The [agents guide](docs/agents.md#chat-api) covers the `POST /chat` / `GET /agents` API.

## Architecture

Service hosts, reusable .NET libraries and an optional Copilot 365 example module, orchestrated by Aspire
(solution: [AgenticLab.slnx](AgenticLab.slnx)):

| Project | Role |
|---------|------|
| [src/AgenticLab.AppHost](src/AgenticLab.AppHost/AppHost.cs) | Aspire orchestrator. Wires up resources, injects the selected model provider's config, sets service references. |
| [src/AgenticLab.AiService](src/AgenticLab.AiService/Program.cs) | ASP.NET Core minimal-API service exposing `POST /chat`, `POST /chat/stream`, `POST /chat/control`, `POST /chat/reset`, `GET /agents`, `POST /skills` and `POST /harness`. Hosts the agent catalog. |
| [src/AgenticLab.Web](src/AgenticLab.Web/Program.cs) | Blazor Server app that visualizes the live data flow (User → Application/Harness → Tools → LLM) by consuming the `/chat/stream` Server-Sent Events. |
| [src/AgenticLab.ServiceDefaults](src/AgenticLab.ServiceDefaults/Extensions.cs) | Shared OpenTelemetry, health checks, resilience, and service discovery. Referenced by every service. |
| [src/AgenticLab.ModelProviders](src/AgenticLab.ModelProviders/ModelClientFactory.cs) | Shared model configuration and raw Azure/OpenAI/Gemini/Bedrock clients for AiService and A2AServer. No Flow state or frontend dependencies. |
| [src/AgenticLab.Extensibility](src/AgenticLab.Extensibility/Examples/ExampleModule.cs) | Shared agent/harness contracts, explicit role-based example registration, runtime/panel contracts and reusable stateless controls. No domain workflow state. |
| [src/AgenticLab.Examples.Copilot365](src/AgenticLab.Examples.Copilot365/README.md) | Optional workplace Copilot example: three agents, host prompt, simulated Microsoft 365 tools/data, resource/risk metadata and tests. No custom panel or protocol service. |
| [src/AgenticLab.McpServer](src/AgenticLab.McpServer/Program.cs) | Minimal Model Context Protocol (MCP) server exposing a `GetCurrentTime` tool over HTTP. Consumed by the AiService over MCP. |
| [src/AgenticLab.A2AServer](src/AgenticLab.A2AServer/Program.cs) | Minimal Agent2Agent (A2A) server hosting **config-declared persona-only agents** (a Research agent and a Poet by default) over the A2A protocol (Microsoft Agent Framework's `AddAIAgent` + `MapA2AJsonRpc`), plus a `GET /agents` discovery endpoint. Called by the AiService's `Orchestrator` agent over A2A. |

Key flow: Web → `POST /chat` or `POST /chat/stream` (with an optional agent name and conversation id) on AiService → `AgentCatalog` resolves the selected `ChatClientAgent` (configured model provider) → the agent calls its tool subset → answers. The service is layered for separation of concerns:

- [src/AgenticLab.AiService/Program.cs](src/AgenticLab.AiService/Program.cs) is a short composition: it calls the `Add*` registration groups in [Startup/ServiceRegistration.cs](src/AgenticLab.AiService/Startup/ServiceRegistration.cs) and the `Map*Endpoints` groups under [Endpoints/](src/AgenticLab.AiService/Endpoints) — `AgentEndpoints` (`GET /agents`, `POST /agents/workspace`, `POST /harness`, `GET /vendors`), `WorkspaceEndpoints` (`POST /skills`, `POST /instructions`, `POST /workspaces`), `DiscoveryEndpoints` (`GET /mcp`, `GET /a2a`, `GET /discovery`, `POST /discovery/stream`) and `ChatEndpoints` (`POST /chat`, `/chat/stream`, `/chat/control`, `/chat/reset`). Each endpoint file declares its own request/response records at the bottom.
- [Application/](src/AgenticLab.AiService/Application) is the reusable runtime infrastructure, grouped by feature with a matching namespace (`AgenticLab.AiService.Application.<Folder>`, imported project-wide by [GlobalUsings.cs](src/AgenticLab.AiService/GlobalUsings.cs)): `Agents` (`AgentCatalog`, `AgentInfo`, `ChatClientProvider`, `VendorHarnessCatalog`), `Flow` (`FlowTracer`, `FlowEvent`, `FlowSession`, `FlowControlRegistry`, `BreakpointNotice`, `FlowExecutionScope`, `FlowCaptureScope`, `CapturingChatClient`, `ToolFilteringChatClient`, `ToolFilterScope`, `UserInputScope`, `RunScopeSet`), `Conversations` (`ConversationStore`, `AgentRunScope`, `AgentRunContext`), `Discovery` (`McpToolProvider`, `A2AAgentProvider`, `DiscoveryTracer`, `DiscoveryModels`, `DiscoverySnapshot`), `Workspace` (`WorkspaceScope`, `WorkspaceAgentLoader` + `WorkspaceAgentFileParser` + `WorkspaceToolAliases`, `WorkspaceAgentResolver`, `WorkspaceDefinedAgent`, `WorkspaceAgentDefinition`), `Skills`, `Instructions` and the harness's own `Tools` (`FileSystemTool`, `TerminalTool`, `SkillsTool`, `AskQuestionTool`, `WebFetchTool`). Shared agent definitions and host contracts live in [Extensibility/Agents](src/AgenticLab.Extensibility/Agents).
- [Demo/](src/AgenticLab.AiService/Demo) holds the built-in sample content: generic agent personas under `Demo/Agents`, shared tools (`WikiTool`, `CalculatorTool` and their bounded `DemoToolSource` adapter) under `Demo/Tools`, and only the non-brand Default host under `Demo/Vendors/Default`. Every other host owns its prompt, dedicated agents, branding, tests and guide in an opt-in example. `Application` never depends on `Demo` or an example; shared interfaces define the dependency boundary.
- Both chat paths share [Application/Flow/RunScopeSet.cs](src/AgenticLab.AiService/Application/Flow/RunScopeSet.cs), which begins, re-activates and disposes the per-run ambient scopes (workspace, disabled tools/skills, enabled instructions, user input and agent/conversation identity) together, and `WorkspaceScope.TryBegin` to turn a bad path into a 400 / error event.

The Blazor Web app mirrors the split. Its flow page cascades two page-scoped state roots from [src/AgenticLab.Web/Flow](src/AgenticLab.Web/Flow): `FlowViewState` (the user's selections, exposing feature collaborators under `Flow/ViewState/` — `Layout`, `Concepts`, `Options`, `WorkspacePrefs`, `Diagram`, `Cursor`, `Roster`, `Agent`, `Harness`) and `FlowRunController` (the live run lifecycle, exposing `Projections`, `Replay`, `Focus`, `Status` and `Catalogs` under `Flow/Run/`). Components read them as `View.Layout.X` / `Run.Replay.Y`; the pure builders (`PromptSignatureBuilder`, `InferenceBuilder`, `EmbeddingBuilder`, `NetworkSimulation`, `ExecutionReplayBuilder`, `A2AFlowBuilder`) stay static and unit-testable. Each Razor component owns its scoped `.razor.css`; a component whose `@code` grows past a screen moves it into a `.razor.cs` code-behind.

Blazor's [design system](docs/design-system.md) needs no JavaScript framework
runtime or npm build dependency. [design-system.css](src/AgenticLab.Web/wwwroot/design-system.css)
owns `--lab-*` tokens and locally served IBM Plex fonts; shared Razor controls under
[Extensibility/Components](src/AgenticLab.Extensibility/Components) and Web's
[Components/Shared](src/AgenticLab.Web/Components/Shared) own reusable presentation and accessibility.
`AppHeader` is reused by Flow, Learn and Discovery; `/design-system` is a backend-free Development-only
catalogue and returns 404 in Production. Feature CSS owns layout, not another generic palette.
Flow places Host/Agent selectors above a conversation/live-flow split, with Settings beside Conversation
and `FlowRunControls` above the diagram. Execution stays beneath live flow. `PanelLayout` starts with
adaptive conversation sizing; dragging selects pixels. `PanelState` reads legacy six-field preferences
and writes a seventh adaptive-width flag under the `agenticlab-panels` key. Reflow never
changes run state or saved preferences; Settings exposes Reset layout.

Flow's root cascades have stable identity. `FlowContent` re-publishes them to content only when
`FlowViewState.ContentVersion`, captured state, run notifications or errors change; layout notifications
still raise `Changed` without advancing the content version. Keep run/status/catalogue notifications
distinct from layout changes. Conversation, Learn and Execution opt into `SidePanel.KeepContentMounted`;
hidden content defers rendering until expanded. Desktop workspace rows and scroll regions have bounded
sizing; narrow stacked layouts keep natural height. `RunProjections` builds simulated inference and
embeddings only on demand, independently of ordinary context/execution projections.

`CapturingChatClient` snapshots provider `UsageContent` into optional typed `FlowEvent.Usage` on
completed local `llm-response` events. Counts include input/output/total and cached input (already
part of input), with nullable missing values. Web mirrors the wire DTO; `TokenUsageBuilder` aggregates
per-exchange counts and independent coverage, cached by `RunProjections`. Conversation footers remain
exchange-wide; Execution displays captured payloads without a separate token-usage block.
Remote A2A usage, billing and simulated tokenization are separate; do not infer cache hits from text.

`TokenUsageFooter` owns conversation usage rendering, scoped CSS, visibility and per-exchange
projection lookup; `FlowChat` passes only the exchange ID for current and archived replies.
`PromptTokenSignature` owns the token chart and its scoped CSS. Keep capture and counting out of
these presentation components; they consume the existing cached projections.

`FlowViewState.Presentation` (`PresentationOptions`) owns page-wide display preferences independently
of diagram presets and run controls. `ShowTokenUsageSummaries` defaults off and uses the content
notification path. Settings exposes it under Display; `Flow.razor.cs` restores/saves the independent
`agenticlab-token-usage` browser key with best-effort storage. It gates conversation footers and prompt
signature actual/cache rows, not estimates, Delta changes, Execution details or capture. Layout reset,
new conversations and host changes preserve it; do not extend `PanelState` for presentation settings.

Prompt signature retains its character calculation and adds a transient Tokens view.
`PromptTokenSignatureBuilder` uses bundled `Microsoft.ML.Tokenizers` reference encodings
(`o200k_base`/`cl100k_base`) for input-only category estimates, including tool definitions but excluding
the new response. `RunProjections` and `RunReplay` compute/cache it only on demand. Actual input/cache
counts pair with the representative request's model turn inside the same causally bounded exchange;
they are not exchange totals. Encoding is explicit, not inferred from deployment aliases. Token Delta
is signed size change, never a cache-reuse claim. See [docs/web-flow-page.md](docs/web-flow-page.md).

Discovery is a shared non-routed `Discovery` component: `DiscoveryPage` supplies the standalone
`/discovery` route and render mode; Flow's `DiscoveryOverlay` hosts it in a native modal without
disposing Flow or its conversation. Visibility lives in `FlowViewState.Layout` and is not persisted.
Learn has no Discovery entry. Overlay re-discovery is disabled during the parent chat run, and
closing cancels only Discovery's stream. See [docs/protocols.md](docs/protocols.md).

Flow has independent Details and Learn docks, visible simultaneously. `InspectorPanel` owns the
Details dock; `FlowViewState.Details` (`HostDetailsSelection`) owns its transient width, collapse state,
host-section selection and optional A2A agent selection. Learn keeps the existing `PanelLayout`
right-panel state. Both reuse
`SidePanel`, with a separate `SizeVariable` for Details. Selection is independent of diagram and run
options; `HostSection` renders clickable headings only while **Expand agent host** is enabled,
and plain labels otherwise, including A2A chips, remote headings and catalogue entries.
A2A details reuse the bounded live/replay projection;
remote prompts, model settings and tools remain unavailable. An already-open inspector survives
collapsing the host.
`HostDetailsBuilder` projects current configuration separately from causally bounded, attributed
captures. `ConfigurationVersion` and per-fetch generations prevent asynchronous catalogue/prompt
responses from publishing data for old selections. See [docs/web-flow-page.md](docs/web-flow-page.md).

## Example Modules

Examples use the shared contracts in [Extensibility](src/AgenticLab.Extensibility) and are explicitly
registered per role through `AddExample<T>(configuration, ExampleHost)`. Keep every example's domain
logic, tools, endpoint groups, personas, UI/controller, fixtures, assets, tests and guide under its
own project directory. Production examples must not reference executable hosts. Host changes are
limited to reusable extension adapters plus composition references/registration calls; do not add
domain enum entries, Flow state properties or tools to core. See [docs/examples.md](docs/examples.md).

`IAgentDefinition`, `AgentDefinitionBase`, `AgentRiskLevel`, `IVendorHarness` and `VendorMode` now live
in `AgenticLab.Extensibility.Agents`; runtime catalogues remain in AiService's `Application/Agents`.
`IAgentRunContext` exposes host-owned identity through `AgentRunScope`, reactivated by `RunScopeSet`.
MCP/A2A shared interfaces expose bounded tool selection and typed delegation outcomes, not host
implementation types. Startup discovery precedes catalogue creation; rediscovery refreshes both
the executable agents and their advertised tool lists.

`IHostToolSource` publishes explicitly shared local tools by exact name, in requested order, and rejects
unpublished names. AiService's `DemoToolSource` adapts only Wikipedia and calculator tools; examples
must not depend on their concrete host types. Copilot365 is enabled with
`Examples:copilot365:Enabled=true`; its API host key remains `microsoft365` and its agent names remain
`M365Copilot`, `M365Researcher` and `M365Analyst` for compatibility.

Web host selection uses catalogue keys and legacy host-value aliases under `agenticlab-vendor`.
Locally registered
`IWebExample` panels receive only `ExamplePanelContext`; awaited `IExamplePanel` cleanup precedes
Web conversation reset. Core Flow roots own no example state. Keep module case history separate
from captured execution replay. `LabButton`, `LabField`, `LabStatus` and `MiniIcon` now live in
[Extensibility/Components](src/AgenticLab.Extensibility/Components), reused by Web and modules;
document tokens, fonts and page/dock controls remain owned by Web.

Enabled modules without panels still register in Web for manifest resources and tool risks.
Flow includes their metadata when `RequiresUi=false`; modules requiring UI remain gated on a local
`IWebExample` implementation. Copilot365 adds no custom Web panel, MCP/A2A role or new endpoint.

Examples require explicit configuration. AppHost forwards `Examples` configuration generically. MCP can
reference AiService for invocation-time reads but must not wait for it or contact it during tool
registration/listing: AiService discovers protocols before listening. Do not introduce circular
`WaitFor` dependencies. Nested module test projects must be excluded from the production RCL's
default items. Module-specific launch and verification steps belong in its README.

Base configuration enables only Default. AppHost's Development settings additionally enable
`copilot365`; `Examples:copilot365:Enabled=false` overrides this default. Other environments
remain Default-only unless configured otherwise (docker-compose.yml enables `copilot365` by default);
use `Examples:copilot365:Enabled=true` to opt in.
AiService and Web explicitly register the participating modules. Harness-only manifests declare no
owned agents: `SharedAgentNames` supplies the existing ChatAgent/Ask/Plan/Coder identities,
registered once in core.

`ExampleManifest.HostPresentation` owns local SVG paths, ordering, product concept links and legacy
host selection aliases. Registration rejects unowned presentation keys and ambiguous aliases;
Default is reserved. Web looks up branding by host key, not agent ownership. There is no branding
enum or branded lookup table in core. Canonical keys and enabled aliases restore `agenticlab-vendor`;
unavailable selections fall back to Default. Shared Learn content remains in Web. Module static
assets may still be built/published when disabled; enablement controls registration, not assembly loading.

## Documentation map

The detailed design notes live under [docs/](docs) — read the page for the area you are changing and keep it in sync:

| Page | Covers |
|------|--------|
| [docs/agents.md](docs/agents.md) | Chat API, conversation memory and retention, the layered harness + persona prompt, per-vendor harnesses, the agent table, human-in-the-loop questions, per-run tool toggles, per-agent models / `ForceDefaultModel`. |
| [docs/web-flow-page.md](docs/web-flow-page.md) | The live flow visualization: page shell and panels, perspectives, diagram toggles, conversation surface, captured LLM payloads, prompt signature, the simulated inference / embeddings / network panels, backend-gated stepping, environment & risk view. |
| [docs/design-system.md](docs/design-system.md) | Blazor tokens, shared components, local assets, accessibility, responsive patterns and the development-only catalogue. |
| [docs/examples.md](docs/examples.md) | Reusable module interfaces, per-host registration, ownership rules, UI lifecycle and community example catalogue. |
| [docs/execution-explorer.md](docs/execution-explorer.md) | The Execution dock (replay of a captured run, bounded archives, resource baseline), chat execution breakpoints and streaming/control API. |
| [docs/learning.md](docs/learning.md) | The in-app Learn panel (concept content) and the standalone guided `/learn` journey. |
| [docs/workspace.md](docs/workspace.md) | Workspace skills, custom instructions, workspace-defined agents (YAML + markdown conventions, tool aliases) and the workspace-scoped file/terminal tools. |
| [docs/protocols.md](docs/protocols.md) | The MCP server, the A2A server and the observable discovery process + Discovery page. |
| [README.md](README.md) | Purpose, prerequisites, local startup, contribution steps and documentation index. |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Contributor workflow, credential-free verification, templates, and owner-controlled publication gates. |
| [SECURITY.md](SECURITY.md) | Private reporting, supported versions, service/tool trust boundaries, credentials, and data retention limits. |
| [tools/README.md](tools/README.md) | No-model-call browser smoke checks and the separate Playwright load-test profile. |

## Build and Run

- Build: `dotnet build AgenticLab.slnx`
- Run everything (launches the Aspire dashboard): `dotnet run --project src/AgenticLab.AppHost`
- Docker: `docker compose up --build -d` builds the four Dockerfile service targets. Configure
  provider environment variables in that shell; Compose does not load AppHost user-secrets.
  Localhost ports 8080/8081/8082/8083 expose Web/MCP/A2A/AiService. Compose supplies explicit
  `services__<name>__http__0` endpoints on internal port 8080, uses Production settings, and
  orders startup without readiness checks. See [README.md](README.md#run-with-docker).
- AppHost pins Aspire 13.5.4 with `AspireUseCliBundle=true`: use the matching Aspire CLI on `PATH`; the SDK-paired CLI package through `dnx` is the fallback.
- Test: `dotnet test AgenticLab.slnx`. AiService's `ModelProviderTests`, `FlowExecutionTests` and
  `ProtocolIntegrationTests` cover provider configuration/authentication, streaming tools, Gemini
  signature retention, history, cancellation and loopback MCP/A2A round trips without API credentials.
  Protocol tests use ephemeral ports, fake models and SDK clients with injected HTTP responses.
- [CI](.github/workflows/ci.yml) runs solution restore, Release build, and matching no-build tests on
  every PR and main push, without Azure credentials or starting AppHost. Keep its `dotnet-build-test`
  check unconditional. Reproduce the commands in [CONTRIBUTING.md](CONTRIBUTING.md#verification).
  The container image workflow remains separate.
- The Web app (`web` resource) starts automatically and is exposed on an external HTTP endpoint; open it from the Aspire dashboard to use the flow visualizer.
- Blazor UI smoke: install temporary Playwright as described in [tools/README.md](tools/README.md), then
  `AGENTICLAB_URL=<web-url> NODE_PATH=/tmp/agentic-lab-loadtest/node_modules node tools/web-smoke.mjs`.
  Use a Development Web instance with an available agent catalogue. This does not send chat or run
  discovery; `/design-system` is also available there for isolated component inspection.

## Configuration

Select one backend at startup with `Models:Provider=AzureOpenAI|OpenAI|Gemini|Bedrock`; absence preserves
Azure OpenAI. Settings come from the git-ignored repository-root **`.env`** (template `.env.example`,
read by AppHost's `DotEnvFile` and by Docker Compose), **AppHost user-secrets** or environment variables.
`.env` overrides appsettings/user-secrets; real environment variables and command-line arguments override `.env`. AppHost explicitly
forwards only the selected connection settings to AiService and A2AServer, never to Web/MCP.
Standalone inference hosts accept the same schema. Existing Azure configuration remains valid:

```
dotnet user-secrets set "AzureOpenAI:Endpoint" "<url>" --project src/AgenticLab.AppHost
dotnet user-secrets set "AzureOpenAI:Deployment" "<deployment>" --project src/AgenticLab.AppHost
dotnet user-secrets set "AzureOpenAI:ApiKey" "<key>" --project src/AgenticLab.AppHost
```

OpenAI requires `OpenAI:ApiKey` and `OpenAI:Model`; Gemini requires `Gemini:ApiKey` and
`Gemini:Model`, without any Azure settings. Bedrock requires `Bedrock:Region` and `Bedrock:Model`
and either `Bedrock:ApiKey` (bearer token) or AWS credentials (optional `Bedrock:Profile`, otherwise the
SDK default chain), never both; AppHost forwards standard `AWS_*` credential variables only when Bedrock is selected. Use `__` instead of `:` for environment variables.
See [README.md](README.md#configure-a-model-provider) for key setup and billing caveats. Provider
selection is independent of host branding, requires a restart and has no runtime fallback.

`ModelConnectionOptions` validates only the selected provider and never prints credential values.
`ModelClientFactory` owns raw SDK construction; each inference host keeps its own wrapper pipeline.
Keep SDK dependencies out of Extensibility and ServiceDefaults. Gemini's provider-local adapter
preserves opaque tool continuation metadata with conversation content, scoped to one response while
streaming; do not replace this with a global signature cache. Bedrock's provider-local
`BedrockChatClient` adapts shared history per request (tool blocks as text when no tools are offered,
blank turns dropped, same-role turns merged) without mutating stored history. Never commit secrets.

Web's optional Entra ID sign-in lives in [EntraSignIn.cs](src/AgenticLab.Web/EntraSignIn.cs): enabled only when
`AzureAd:ClientId` is set (then `AzureAd:TenantId` must be a tenant GUID and `AzureAd:ClientSecret` is required),
it uses `Microsoft.Identity.Web` with the authorization-code flow (no token acquisition or token cache), sets an
authenticated-user fallback policy, always registers authentication services (the middleware always runs), keeps health endpoints anonymous
and maps `/signout`. AppHost forwards the `AzureAd` section to Web only. `AppHeader` reads the cascading
authentication state instead of `AuthorizeView`, so it also renders where no auth services are registered.

In-memory conversation retention is configured in the AiService settings. `InactiveTtl` is a sliding
window refreshed whenever a conversation is used; `CleanupInterval` controls the expiry scan:

```json
"Conversations": {
  "InactiveTtl": "01:00:00",
  "CleanupInterval": "00:05:00"
}
```

Per-agent models use `Agents:{Name}:Model`, then Azure-only `Agents:{Name}:Deployment`, then the
agent/workspace `ModelId`, then the provider default. `Models:ForceDefaultModel` overrides execution;
when absent, only Azure inherits `AzureOpenAI:ForceDefaultModel`. AppHost forwards explicit agent
overrides to AiService; A2A specialists use the global default. See
[docs/agents.md](docs/agents.md#per-agent-models).

## Conventions

- Product name: **Agentic Lab**; project/namespace prefix: `AgenticLab`. Use `https://github.com/o-viken/agenticlab` for project repository links,
  `agenticlab-*` for container image names and browser preference keys, and `AGENTICLAB_*` for
  browser-tool environment variables. This is a clean rename: do not add legacy product-name
  fallbacks or migrate old browser preferences. Preserve the AppHost `UserSecretsId`.
  Generic "agentic AI" is a subject,
  not an obsolete product name.

- Teaching and UI vocabulary: **Agent = Agent host + Model**. The host manages context, instructions, tools, memory and execution controls; the model reasons, plans and chooses a next step or final answer. Tool requests are not authorization: the host checks and executes permitted actions. Use **agent host** as the primary label, with **harness** explained as its agent-running machinery. Preserve technical identifiers, event kinds and existing concept/stage URLs when editing terminology.

- Target framework `net10.0`; `Nullable` and `ImplicitUsings` enabled across all projects.
- Use top-level statements in `Program.cs` and minimal APIs (no controllers).
- DTOs are `internal sealed record` types declared at the bottom of the file that uses them — for the HTTP API that is the `Endpoints/<Group>Endpoints.cs` file mapping the route.
- Service registrations are grouped into `Add<Concern>()` extension methods in `Startup/ServiceRegistration.cs`; endpoints into `Map<Group>Endpoints()` extension methods under `Endpoints/`. `Program.cs` only composes them.
- `Application/` files live in a feature folder whose name is the last namespace segment; new cross-cutting per-run state goes in an `AsyncLocal` `*Scope` under the feature it belongs to and is re-`Activate()`d via `RunScopeSet` when it must survive a streaming `yield`.
- Web flow state: add view/preference state to the matching `Flow/ViewState/` collaborator (or a new one) and run-derived state to a `Flow/Run/` collaborator; the two roots only hold the top-level selections and the run lifecycle. Keep the collaborator constructors and the roots' public constructors/`Changed` events stable — the Web tests construct them directly.
- Diagram visibility is owned by `DiagramOptions`: Basic/Technical presets apply option values atomically, and Custom is derived from those values. Components render from options, never preset identity; display changes must not modify `RunOptions`, capture, conversation or replay state.
- Blazor components: one scoped `.razor.css` per component that owns its own markup's styles (use `::deep` only for genuinely shared base styles reaching into children); a `@code` block that outgrows a screen moves to a `.razor.cs` partial, and pure presentation math goes to a static class under `Flow/`.
- Blazor presentation: reuse `--lab-*` tokens and existing `LabButton`, `LabField`, `LabSegmented`,
  `LabStatus`, `AppHeader`, `PageNotice`, `SidePanel` and icons before adding generic controls. Shared
  controls take parameters/events, never Flow state. Add a real consumer and a catalogue example for
  new primitives; follow [docs/design-system.md](docs/design-system.md). Native ARIA boolean attributes
  must render explicit `"true"`/`"false"` strings, not minimised Razor boolean attributes.
- Agent capabilities are plain methods annotated with `[Description]` (on the method and each parameter) and exposed via `AIFunctionFactory.Create(...)` in each tool's `AsTools()` (see `WikiTool`, `CalculatorTool`, `FileSystemTool`, `TerminalTool`). Tools live under [src/AgenticLab.AiService/Application/Tools](src/AgenticLab.AiService/Application/Tools) (harness/app tools, used by the workspace agents) and [src/AgenticLab.AiService/Demo/Tools](src/AgenticLab.AiService/Demo/Tools) (demo tools); add new ones there the same way. Resolve file-tool paths via the active `WorkspaceScope.ResolvePath` and take the terminal working directory from that scope. These lexical path checks and the command allowlist are not a filesystem/process sandbox; preserve the trust-boundary guidance in [SECURITY.md](SECURITY.md).
- Add a new agent by inheriting `AgentDefinitionBase` and supplying its name, description, `Persona` (its own system prompt, layered on top of the shared harness prompt), and tool subset under `src/AgenticLab.AiService/Demo/Agents/`, then registering it as a singleton `IAgentDefinition` in [Program.cs](src/AgenticLab.AiService/Program.cs). Declare the name as a `public const string AgentName` and return it from `Name`, so vendor harnesses and other call sites reference the constant instead of repeating the string. Override `RequiresWorkspace => true` when the agent's tools need a workspace root (the endpoints then insist on a `Workspace` path and open a `WorkspaceScope` for the run). Override `SupportsSkills => true` to opt into workspace skills (the endpoints then inject the `<skills>` catalogue per run; see [workspace skills](docs/workspace.md#workspace-skills-the-coder-agent)). Override `RiskLevel` (an `AgentRiskLevel`) and `Guardrails` (an `IReadOnlyList<string>` of human-readable safety mechanisms) to communicate how risky the agent is and what constrains it — both default to `None` / empty in `AgentDefinitionBase`, are surfaced over `GET /agents`, and drive the Environment & risk view's risk meter and guardrails chips. Override `SuggestedPrompts` (an `IReadOnlyList<string>`, default empty) with two or three short example messages that exercise the agent's tools; `GET /agents` returns them as `suggestedPrompts`, Blazor's empty conversation offers them as composer pre-fills, and they are never sent to the model or auto-submitted. Override `ModelId` (a `string?`, default `null`) to declare a preferred model or Azure deployment in code, though configured agent model overrides take precedence (see [per-agent models](docs/agents.md#per-agent-models)). Alternatively, ship an agent **in a workspace** as an `agents/<name>.agent.yaml` file (no code, no redeploy) — it can only use existing backend tools and is discovered + run per request; see [workspace-defined agents](docs/workspace.md#workspace-defined-agents-the-agents-folder).
- Services reach each other by Aspire resource name (e.g. `https+http://aiservice`) through service discovery, not hardcoded URLs.
- Agents are stateless; the shared `IChatClient` and the `AgentCatalog` are registered as singletons. Per-conversation history lives outside the agents in the singleton `ConversationStore` (keyed by `ConversationId`), not on the agents themselves, and expires after the configured sliding inactivity window.

## Documentation

- **Keep documentation present and up to date.** Whenever you change behavior, structure, or conventions, update the relevant docs in the same change so they never drift from the code.
- Update **this [AGENTS.md](AGENTS.md)** when you add/remove a project, change build/run commands, alter the architecture or dependency flow, or introduce a new convention; update the matching page under [docs/](docs) (see the documentation map) when you change a feature's behaviour, and add a new page + map row for a new feature area.
- Add or update **XML doc comments** (`/// <summary>`) on public types and members, in the style of the existing ones (state what the code cannot show on its own, one short paragraph).
- When adding a new endpoint, service, or cross-host component, document its purpose and any non-obvious behavior (e.g. that correct answers are never exposed).
