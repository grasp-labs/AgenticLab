# Agents, personas and the layered harness prompt

Agentic Lab's chat API, agent roster, conversation memory and prompt/model configuration.
See [workspace features](workspace.md) for user-defined agents, skills and instructions, and
[Execution](execution-explorer.md) for streaming and controls. These APIs assume a trusted local
environment, not authenticated multi-user access; see [SECURITY.md](../SECURITY.md).

## Chat API

Use the **aiservice** endpoint shown in the Aspire dashboard for direct API calls. These routes
are implemented in [AgentEndpoints](../src/AgenticLab.AiService/Endpoints/AgentEndpoints.cs) and
[ChatEndpoints](../src/AgenticLab.AiService/Endpoints/ChatEndpoints.cs).

### `GET /agents`

Returns `agents` and a `default` name (`ChatAgent`). Each entry describes tools, workspace/skills
support, model, risk and guardrails, plus optional `exampleId`/`requiresExampleUi` metadata.
Enabled [examples](examples.md) can add agents; discover the roster rather than hardcoding it.
Each entry may also carry `suggestedPrompts`: short example messages the agent advertises. Blazor's
empty conversation offers them under **Try asking** as composer pre-fills and never sends them
automatically or as instructions. Code-defined agents override `SuggestedPrompts` on
`AgentDefinitionBase` (default empty); workspace agents declare a `prompts:` list (see
[workspace-defined agents](workspace.md#workspace-defined-agents-the-agents-folder)).
`POST /agents/workspace` lists workspace-defined agents separately. `GET /examples` returns public
module manifests, not component types or remote prompts.

### `POST /chat`

Send a message, optionally choosing an agent (otherwise the default is used):

```json
{ "message": "Who was Alan Turing?", "agent": "WikiAssistant" }
```

An example response:

```json
{
  "reply": "Alan Turing was a British mathematician and computer scientist...",
  "agent": "WikiAssistant",
  "conversationId": "example-conversation-id"
}
```

Reuse the returned `conversationId` to continue the conversation. Agent names are case-insensitive.
An empty message, unknown agent or missing/invalid required workspace returns `400 Bad Request`.

Both chat paths support these optional fields:

| Field | Effect |
| --- | --- |
| `agent` | Select an agent; omission uses the default. |
| `workspace` | Existing service-side folder, required by workspace agents. |
| `vendor` | Replace host guidance, not the agent or its tools. |
| `disabledTools` | Narrow the agent's tool set for this run. |
| `disabledSkills` | Disable named skills; available skills default on. |
| `enabledInstructions` | Inject selected workspace instructions; default off. |

The [streaming API](execution-explorer.md#post-chatstream) additionally exposes optional typed
`usage` on each completed local model response: input, output, total and cached input token counts.
These are provider-reported, not tokenizer estimates; cached input is already part of input.
Missing usage remains unavailable. The non-streaming `/chat` response above is unchanged.

### `POST /chat/reset`

Clear remembered history with `{ "conversationId": "example-conversation-id" }`.
Returns `204 No Content`, or `400 Bad Request` for a blank ID. It does not reset an example's
domain state, approve an action or undo tool side effects. For interactive execution, see
the [streaming API](execution-explorer.md#post-chatstream) and
[control API](execution-explorer.md#post-chatcontrol).

## Conversation retention

Agents are stateless; [ConversationStore](../src/AgenticLab.AiService/Application/Conversations/ConversationStore.cs)
keeps an in-memory session per conversation. History survives agent changes but not service restarts.
Use a conversation sequentially; there is no per-conversation locking. Configure expiry in
[AiService settings](../src/AgenticLab.AiService/appsettings.json):

```json
{
  "Conversations": {
    "InactiveTtl": "01:00:00",
    "CleanupInterval": "00:05:00"
  }
}
```

Each use refreshes the sliding expiry. The `AgenticLab.AiService.Conversations` meter reports
`conversations.retained`, `conversations.expired` and `conversations.reset` counts, never content.
Backend retention is independent of [Web replay retention](execution-explorer.md#replay-retention).

## Conversation memory and the layered prompt

**Host changes:** Blazor starts a fresh conversation and clears captures when Host changes,
preserving drafts and view preferences. Agent-only changes keep history. This is client behavior;
the server does not reset based on `vendor`. Blazor uses a fresh local ID immediately and reports cleanup failures.

**Prompt layers:** the agent host's shared guidance (`<harnessMode>`) precedes its persona
(`<agentMode>`). Harness is the technical name for the host's agent-running machinery. Selecting
a vendor replaces only the host guidance, retaining the persona and configured tool set. It does
not select a different model deployment. Prompts are original teaching examples, not proprietary
vendor prompts or integrations with those products.

`default` preserves the agent's own guidance. Blank/unknown vendor keys and empty overrides also
leave it unchanged. Other hosts require [enabled examples](examples.md#default-startup);
Copilot365 uses module ID `copilot365` but API host key `microsoft365`.

- `GET /vendors` lists registered host names, modes, optional simulated model labels and example metadata.
  `GET /agents` entries carry `modelId` and `provider` (for example `Amazon Bedrock`); Web shows those
  when a host sets no label of its own, as Default and Copilot 365 do.
- `POST /harness` accepts `{ Agent, Vendor }` and returns `{ Prompt }` for the host guidance preview.
- Captured `llm-request` data shows the actual composed instructions sent for a run.

[AgentDefinitionBase](../src/AgenticLab.Extensibility/Agents/AgentDefinitionBase.cs) composes the
layers; [VendorHarnessCatalog](../src/AgenticLab.AiService/Application/Agents/VendorHarnessCatalog.cs)
resolves registered overrides. Since agent instructions are fixed at construction, overrides create
a transient agent on the same chat client instead of mutating the cached agent. For registration
and ownership, see [example modules](examples.md).

## Agents

| Agent | Persona | Tools |
|-------|---------|-------|
| `WikiAssistant` | Concise research helper grounded in Wikipedia. | `SearchWiki`, `GetWikiPage` |
| `MathTutor` | Patient tutor that solves and explains arithmetic. | `Calculate` |
| `TriviaMaster` | Playful trivia host that researches facts and crunches numbers. | `SearchWiki`, `GetWikiPage`, `Calculate` |
| `ChatAgent` (default) | Friendly conversational companion that chats from its own knowledge. | _(none)_ |
| `Ask` | Read-only assistant that answers questions and explains code in the workspace without changing anything. **Requires a workspace.** | `ReadFile`, `ListFiles` |
| `Plan` | Read-only planner that investigates the workspace and proposes an implementation plan without changing anything. **Requires a workspace.** | `ReadFile`, `ListFiles`, `AskQuestion` |
| `Coder` | Workspace-scoped coding agent that generates and edits code and runs allowlisted commands. **Requires a workspace.** | `ReadFile`, `ListFiles`, `WriteFile`, `DeleteFile`, `RunCommand`, `ReadSkill` |
| `TimeKeeper` | Tells the current time using a tool **discovered from the MCP server** (real MCP client). | `GetCurrentTime` (MCP) |
| `Orchestrator` | Solves arithmetic itself, but delegates general-knowledge/research/creative questions to **specialist agents over the A2A protocol** (real A2A client), routing by name. | `Calculate`, `DelegateToAgent` (A2A) |

The first definition registered by `AddDemoAgents` is the default. `Ask` and `Plan` never write,
delete or run commands. For custom agents, see the
[workspace format](workspace.md#workspace-defined-agents-the-agents-folder).

### Optional Copilot365 example

Enable `Examples:copilot365:Enabled=true` for `M365Copilot`, `M365Researcher` and `M365Analyst`.
The [module guide](../src/AgenticLab.Examples.Copilot365/README.md) lists their tools. None requires
a workspace or supports skills. Workplace data and `SendMail` are simulated: no Graph calls or mail
delivery. The Medium risk label illustrates sending; confirmation is a persona instruction, not an
enforced approval gate. The researcher's Wikipedia tools still make real public requests.

## Asking the user a question (human-in-the-loop)

`Plan` includes [AskQuestion](../src/AgenticLab.AiService/Application/Tools/AskQuestionTool.cs);
workspace agents can select it too. On `/chat/stream`, an `ask-question` event displays an answer
input. Submit `POST /chat/control` with action `answer` and the `answer` text to resume. The answer
becomes a tool result in conversation history. Several questions may occur in one run.

There is no answer timeout: the run waits until answered, stopped or cancelled. Non-interactive
`POST /chat` returns a tool fallback asking the model to proceed with stated assumptions instead
of blocking. [UserInputScope](../src/AgenticLab.AiService/Application/Flow/UserInputScope.cs)
owns the per-run wait.

## Toggling an agent's tools (per run)

Send `disabledTools` with case-insensitive tool names. This only narrows the declared set; it cannot
grant tools. Web exposes checkboxes in Settings (reset on agent change).

[ToolFilteringChatClient](../src/AgenticLab.AiService/Application/Flow/ToolFilteringChatClient.cs)
filters before function invocation so disabled tools reach neither model nor invocation loop.
Do not implement subtraction through per-run `ChatOptions.Tools`: the framework unions that list
with agent tools. [RunScopeSet](../src/AgenticLab.AiService/Application/Flow/RunScopeSet.cs) keeps
per-run filters together in both chat paths.

## Model providers

Select one inference provider at startup with `Models:Provider`. Values are case-insensitive;
missing or blank selects `AzureOpenAI`. Unsupported values fail configuration validation, never
silently fall back to a different provider. Only the selected provider's settings are required:

| Provider | Required settings | Model meaning |
| --- | --- | --- |
| `AzureOpenAI` (default) | `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`, `AzureOpenAI:Deployment` | Your Azure deployment name, including Foundry-managed deployments. |
| `OpenAI` | `OpenAI:ApiKey`, `OpenAI:Model` | A chat-completions model available to your OpenAI API account. |
| `Gemini` | `Gemini:ApiKey`, `Gemini:Model` | A Gemini API model supporting chat and function calling. |
| `Bedrock` | `Bedrock:Region`, `Bedrock:Model`, plus `Bedrock:ApiKey` or AWS credentials (optional `Bedrock:Profile`) | A Bedrock model ID or inference profile ID that supports Converse tool use. |

Use [AppHost user-secrets or environment variables](../README.md#configure-a-model-provider).
AppHost explicitly forwards the selected settings to AiService and A2AServer, not to Web or
MCP. Standalone inference hosts accept the same configuration. Restart after changing providers,
models or credentials. There is no per-user key entry, runtime provider switch or automatic fallback.

[ModelConnectionOptions](../src/AgenticLab.ModelProviders/ModelConnectionOptions.cs) validates keys,
model names, flags and the Azure endpoint without including credential values in validation errors.
[ModelClientFactory](../src/AgenticLab.ModelProviders/ModelClientFactory.cs) creates raw `IChatClient`
adapters. AiService retains its tool filtering, invocation, capture and telemetry pipeline; A2A keeps
its own invocation and telemetry pipeline. Concrete SDK dependencies stay in this shared runtime
library, outside the frontend and example contracts.

The factory applies a streaming compatibility policy for the three OpenAI-protocol providers: explicit
`"delta": null` values in chat-completion SSE events become empty objects before the OpenAI
adapter reads them. This avoids the reasoning-content null-object exception in
`Microsoft.Extensions.AI.OpenAI` 10.10.0 (also present in 10.10.1). Events are processed individually;
text, reasoning, tool calls, finish reasons and usage remain intact. Non-streaming responses and
HTTP errors are unchanged, and failed runs are not silently retried. Remove the workaround only
after an SDK upgrade passes the null-delta provider regressions.

OpenAI uses `https://api.openai.com/v1/`; Gemini uses
`https://generativelanguage.googleapis.com/v1beta/openai/`. Native endpoints are fixed, not arbitrary
proxy URLs. Gemini's compatibility API is beta. Its adapter retains opaque tool-call `extra_content`
(including thought signatures) with the conversation and preserves it across streamed chunks and
tool-result requests. It does not interpret those signatures or store them in a global cache.
Other Gemini-specific APIs and OpenAI Responses features are outside this chat-completions integration.

Bedrock uses the AWS SDK's `AWSSDK.Extensions.Bedrock.MEAI` adapter over the Converse and
ConverseStream APIs in the configured region, not an OpenAI-compatible endpoint. With `Bedrock:ApiKey`,
requests carry the Bedrock API key as an HTTP bearer token and no AWS credentials are resolved; setting
both `Bedrock:ApiKey` and `Bedrock:Profile` is rejected as ambiguous. Only `Bedrock:ApiKey` selects
bearer authentication: the SDK's ambient `AWS_BEARER_TOKEN_BEDROCK` variable is deliberately ignored.
Otherwise requests are always signed with SigV4: `Bedrock:Profile` names a profile in the local AWS config/credentials files (SSO profiles
included) and takes precedence over `AWS_PROFILE`; without it the SDK's default credential chain
applies (environment credentials, `AWS_PROFILE`, attached roles; see the AWS SDK for .NET guide for
the exact order). The profile is resolved once per host and
shared by every model's client. A missing named profile fails when the first client is created
(AiService's agent catalogue, A2AServer startup); expired SSO sessions fail on the first request until
`aws sso login` is run again. When Bedrock is selected, AppHost explicitly sets `AWS_PROFILE`,
`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` and `AWS_SESSION_TOKEN` from its own environment on
AiService and A2AServer. Bedrock model IDs are case-sensitive, like OpenAI and Gemini model names.

Converse is stricter about history than the other providers. Conversations are shared across agents,
so [BedrockChatClient](../src/AgenticLab.ModelProviders/BedrockChatClient.cs) adapts each request
without changing stored history: when a request offers no tools (a tool-free agent, all tools disabled,
or MCP unavailable), earlier tool calls and results are sent as bracketed text, because Converse
rejects tool blocks without a tool configuration. Blank turns are dropped and adjacent same-role turns
merged so roles alternate. A leading assistant turn is not repaired.

Host branding and illustrative model labels remain independent of provider selection. Only
setting `Models:Provider=Gemini` connects to Google. API billing is
separate from consumer subscriptions, and live calls transmit context to the selected provider.

## Per-agent models

Agents can select different models within the single configured provider, sharing its credentials.
[ChatClientProvider](../src/AgenticLab.AiService/Application/Agents/ChatClientProvider.cs) caches one
client per model; native identifiers are case-sensitive, while Azure retains case-insensitive caching.
The declared model is resolved in this order:

1. `Agents:{Name}:Model` in AiService configuration.
2. `Agents:{Name}:Deployment`, **only for Azure OpenAI**.
3. The agent's `ModelId`, or a workspace agent's `model` field.
4. The selected provider's default model or deployment.

AppHost forwards explicitly configured agent model overrides to AiService. These may also be set
directly in [AiService settings](../src/AgenticLab.AiService/appsettings.json). For example:

```json
{
  "Agents": {
    "Coder": { "Model": "<model-id-for-the-selected-provider>" }
  }
}
```

The existing `Agents:Coder:Deployment` sample is ignored for OpenAI/Gemini/Bedrock, so switching providers
does not send an Azure deployment name to another API. Explicit `Model` and workspace `model` values
are not translated; update them for the provider you select. A2A specialists all use the provider's
global default, without per-specialist model overrides.

`GET /agents` exposes the resolved **declared** model as `ModelId`. The Details inspector labels it
accordingly, rather than claiming that host branding or a model name proves which provider executes.

**Display versus execution:** `Models:ForceDefaultModel=true` makes every local agent execute on the
provider's global default while leaving declared model labels unchanged. When the neutral setting is
absent, Azure honors `AzureOpenAI:ForceDefaultModel` as before; OpenAI/Gemini/Bedrock default to `false` and
ignore the Azure flag. An explicit neutral `false` also overrides a legacy Azure `true`.

The shipped AiService Azure settings enable the legacy flag. This existing example still displays
Coder's `gpt-5.3-codex` but runs it on `gpt-5.3-chat`:

```json
{
  "AzureOpenAI": {
    "Deployment": "gpt-5.3-chat",
    "ForceDefaultModel": true
  },
  "Agents": {
    "Coder": { "Deployment": "gpt-5.3-codex" }
  }
}
```

## Provider verification

`ModelProviderTests` uses injected HTTP transports with fake keys to check endpoint/authentication,
ordinary and streaming tool loops, serialized Gemini signatures, cancellation, configuration and
model precedence. `ProtocolIntegrationTests` also delegates through each factory-backed adapter on
loopback A2A servers. These tests do not contact model providers or prove live model availability:

```sh
dotnet test tests/AgenticLab.AiService.Tests/AgenticLab.AiService.Tests.csproj --filter "FullyQualifiedName~ModelProviderTests|FullyQualifiedName~ProtocolIntegrationTests|FullyQualifiedName~FlowExecutionTests"
```

For live verification, explicitly configure development credentials, start AppHost, and try a short
chat, a streamed calculator request and Orchestrator delegation for each provider. Use synthetic
input; these checks incur normal provider usage. Record the provider and model actually tested.
