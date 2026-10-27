# Security Policy

## Supported Versions

Security fixes target the latest code on the `main` branch. Older commits and releases do not have
a separate security-support commitment. Agentic Lab is an evolving educational sample, not a
production-ready agent platform.

## Reporting a Vulnerability

Use [GitHub private vulnerability reporting](https://github.com/o-viken/agenticlab/security/advisories/new).
Do not open a public issue or pull request for a suspected vulnerability or publish exploit details.
The form requires a public repository with private reporting enabled; if it is unavailable, do not
post sensitive details in a public channel.

Include the affected commit or version, relevant component, operating system, potential impact,
and minimal reproduction steps using synthetic data. Describe any relevant configuration with
credentials and private identifiers removed. Do not include API keys, tokens, private prompts,
workspace files, personal data, or sensitive captures in reports, logs, screenshots, or attachments,
including private reports.

If a credential has been exposed, revoke or rotate it immediately. Deleting a file or rewriting
history does not make a credential safe again. Coordinate any history or artifact cleanup with the
repository owner; copies can remain in forks, caches, logs, packages, and releases.

## Intended Use and Trust Boundaries

Run Agentic Lab only in a trusted local development environment with workspaces and services you
control. Publishing the source does not make the running services safe to expose to an untrusted
network. Before any shared or public deployment, add authentication, per-user authorization,
process/filesystem isolation, network policy, resource and cost limits, and a reviewed retention
policy. A reverse proxy alone does not provide all of these controls.

- Web can require Microsoft Entra ID sign-in for one tenant (`AzureAd__*`, see the README); any
  account the tenant accepts can sign in unless the Enterprise application requires assignment. It
  protects the pages, not the services behind them. AiService and the sample MCP/A2A services do not
  implement caller authentication or per-user authorization. Conversation and session IDs identify state; they are not access control.
- `POST /chat` and `POST /chat/stream` can invoke the selected agent's tools. `POST /chat/control`
  can affect a live run, including releasing execution or answering a question; `POST /chat/reset`
  removes backend conversation history. None of these endpoints should be exposed unprotected.
- Workspace, agent, and harness endpoints can reveal local directory information, workspace
  metadata, and configured prompts without sending a chat message. Protect them as well as chat.
- Keep MCP/A2A services and the Aspire dashboard private too; review remote servers and their tools before connecting them.

### Workspace and Terminal Tools

Only select trusted workspaces. Agent definitions, skills, instructions, fetched pages, and tool
results can influence model requests; instructions and tool descriptions are not a security boundary.
Disable tools you do not need, and avoid running agents with access to valuable credentials or data.

`WebFetch` connects only to public addresses: it refuses loopback, private, link-local (including the
`169.254.169.254` cloud metadata endpoint), shared and multicast ranges, checked on every connection
including redirects. File and terminal tools have no such boundary; on a shared deployment narrow
`Coder:AllowedCommands` and treat the host's own files and credentials as reachable by signed-in users.

File tools check normalized paths against the selected workspace root. These are lexical checks,
not a symlink-resolving filesystem sandbox. Callers supply the root, so a workspace path is not a
per-user permission boundary either.

Terminal commands run with the service account's operating-system permissions and environment,
using the workspace as their working directory. The executable allowlist, shell-operator checks,
and timeout are limited safeguards, **not sandboxing**. Allowed interpreters, package managers,
and build tools can execute code, access files outside that directory, and contact the network.
Use operating-system isolation and least privilege for stronger protection; do not rely on prompts,
manual stepping, or the working directory to constrain a process.

## Credentials, Data, and Costs

Keep Azure OpenAI, OpenAI or Gemini credentials in the AppHost's local user-secrets store or
server-side environment variables as described in [Run Locally](README.md#run-locally). Bedrock uses
either a Bedrock API key stored the same way (prefer short-term keys) or AWS credentials from a local
profile, the standard `AWS_*` environment variables, or an attached role. Grant only Bedrock invoke
permissions. Aspire
explicitly forwards only the selected provider's settings to AiService and A2AServer, not to Web
or MCP. Provider selection is local startup configuration, not a browser credential field.
Never put credentials in source, frontend assets, images, sample configuration, or reports.
The user-secrets store is a development convenience, not a production secret-management system.

Live runs send prompts, conversation history, enabled instructions, selected context, and tool
results to the configured model service and may incur Azure, OpenAI, Google or AWS charges. API usage
is separate from consumer chat subscriptions; there is no automatic fallback to another provider.
Gemini tool-call signatures are opaque continuation metadata retained with conversation history,
not a credential or readable reasoning trace. Wikipedia, web-fetch, MCP,
and A2A integrations can make additional network requests. Local startup does not mean data stays
local. Disabling an example or a tool is not a general network or cost limit.

Streaming flow captures deliberately include model requests/responses and tool arguments/results.
Treat captures, screenshots, transcripts, and diagnostic output as sensitive. Backend conversation
history and frontend captures have separate lifetimes:

- Backend history is in memory with a configurable sliding inactivity TTL and periodic cleanup;
  `POST /chat/reset` removes the selected entry. See [conversation memory](docs/agents.md).
- Blazor retains the current exchange and a bounded per-page replay archive. A completed run does
  not immediately disappear, and the current exchange is outside the archive budget until the next
  Send. New conversation clears the page's retained conversation/captures; page disposal releases
  them too. See [replay retention](docs/execution-explorer.md#replay-retention).

Reset is not secure erasure and does not undo tool side effects or delete data already sent to
external services, exported logs, screenshots, or other copies. Browser preferences can also retain
workspace paths independently of conversation reset.

OpenTelemetry includes logs, traces, and metrics and can export them to the configured collector.
The application's aggregate retention metrics omit content and IDs, and the chat-client setup does
not explicitly enable sensitive prompt-content telemetry. This is not a guarantee that all logs,
traces, dependencies, or collector configurations are free of sensitive data. Review collection,
export, access, and retention before using real data.

For exploration without model calls, start only the Web project's
[learning-only guide](README.md#learning-only). Do not start model/protocol services or use live
chat/discovery for that workflow.