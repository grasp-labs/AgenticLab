# Agentic Lab

Agentic Lab was created to **demystify agentic AI and show how it works**.
It brings together two complementary parts:

1. **[Learn how agentic AI works](#1-learn-how-agentic-ai-works).** Guided lessons and simple
   explanations introduce agents, models, tools and context.
2. **[See it happen in Live Flow](#2-see-it-happen-in-live-flow).** Run an agent and watch the steps
   between your question and its answer: what the model receives, which tools it asks to use,
   and what comes back.

> [!WARNING]
> This is an educational sample, not a production-ready agent platform. Use a trusted local
> development environment. Before shared or public deployment, add authentication, authorization,
> sandboxing, network/tool policy, resource limits, and a reviewed data-retention policy.
> Read [SECURITY.md](SECURITY.md) before using real credentials or workspace data.

## Project Status

Agentic Lab is actively evolving. APIs, UI details, and configuration may change between versions.
Security fixes target the latest `main` branch; older releases have no separate support commitment.

## 1. Learn How Agentic AI Works

The **Learn** experience explains the building blocks of an agent through guided lessons on models,
tools and context. Start with the core idea:

**Agent = Agent host + Model.** The model chooses the next step. The agent host is the code around
it that supplies context, manages tools and controls execution.

1. You ask a question.
2. The host sends the model your question, instructions, context and available tools.
3. The model returns an answer or asks to use a tool.
4. The host checks and runs allowed tool calls, then sends the results back to the model.

This loop continues until the model gives a final answer. A request to use a tool is not permission
to run it: the host decides what is allowed.

Explore the guided lessons at `/learn` with the [learning-only setup](#learning-only); no model
credentials or live model calls are needed.

[![The agent-loop lesson showing context, model decisions, host tool execution, observations, and final answers.](docs/images/06-guided-learning.png)](docs/images/06-guided-learning.png)

The agent loop in the guided Learn experience. [Learning guide](docs/learning.md).

## 2. See It Happen in Live Flow

**Live Flow** is the hands-on workspace where you run an agent and see what is happening as it runs.

- Watch the flow between the agent host, model and tools as your question is processed.
- Inspect the actual model requests, responses, tool calls and results.
- Pause, step through execution and replay a captured run without running it again.

The captured activity shows what the application sends and receives, not the model's private
reasoning. Illustrations of model internals are labelled simulations.

Built with [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0),
[Aspire](https://aspire.dev/) and your choice of Azure OpenAI, OpenAI, Gemini or Amazon Bedrock. Read the [vision](VISION.md) for more on the project's
purpose and direction.

[![Completed Wikipedia-and-calculator conversation beside the user, agent host, tools, and model diagram.](docs/images/01-live-workspace.png)](docs/images/01-live-workspace.png)

Follow a conversation alongside its agent and tools. [Workspace guide](docs/web-flow-page.md).

Screenshots captured locally on 2026-09-22 from a running Blazor build. The live run uses public
Wikipedia data and real calculator calls. Gray masks cover deployment identifiers. The
ChatGPT-labelled host (from an example removed in this fork) is a representative demo backed by Azure OpenAI, not a connection to the
ChatGPT product. Select an image to open it at full size.

## Security, Data, and Costs

AiService and the sample MCP/A2A services do not provide caller authentication or
per-user authorization. Chat endpoints can invoke tools; control and workspace endpoints also need
protection. The terminal allowlist and workspace working directory are **not a sandbox**: processes
run with the service account's permissions and can access files and networks outside that directory.
Use only trusted workspaces and integrations. See the [security policy](SECURITY.md) for the full
trust boundaries and private vulnerability-reporting channel.

- Live runs send prompts, conversation history, enabled instructions, selected context, and tool
   results to the configured model service. Provider charges may apply; local startup does not mean
   data stays local. Wikipedia, web-fetch, MCP, and A2A integrations can contact other services.
- Flow captures include model/tool payloads and can contain private data. Backend conversation
   history and frontend captures have separate lifetimes. Reset does not erase external copies,
   exported logs, screenshots, browser workspace preferences, or tool side effects. See
   [conversation memory](docs/agents.md) and [Blazor replay retention](docs/execution-explorer.md#replay-retention).
- OpenTelemetry collects logs, traces, and metrics for the configured collector. Review their
   contents, access, and retention before using sensitive data; aggregate metrics without content
   do not guarantee that every log or trace is free of sensitive information.

To explore without model or protocol calls, use the [learning-only Web guide](#learning-only)
without starting AppHost or the model/protocol services. Disabling an example or individual tools
does not turn live chat into an offline workflow or impose a cost limit.

## Prerequisites

For the full local application:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- [Aspire CLI](https://aspire.dev/get-started/install-cli/) matching the pinned **13.5.4** version.
  The AppHost restores its Aspire SDK automatically and uses the SDK-paired CLI through `dnx`
  as a fallback when a compatible CLI is not on `PATH`.
- One model provider with a chat model that supports tool calling: an
   [Azure OpenAI](https://learn.microsoft.com/azure/ai-services/openai/) deployment (including Foundry),
   an [OpenAI API key](https://platform.openai.com/api-keys), a
   [Gemini API key](https://aistudio.google.com/apikey), or
   [Amazon Bedrock](https://aws.amazon.com/bedrock/) access through a Bedrock API key or AWS credentials.
- Git to clone the repository, or download and extract its source archive.

Docker is not required for the default local setup. The [learning-only option](#learning-only)
needs just the .NET SDK and the source code, without model credentials or Aspire orchestration.

## Run Locally

### Get the Source

```sh
git clone https://github.com/o-viken/agenticlab.git
cd agenticlab
```

Run the following commands from the repository root.

### Configure a Model Provider

Choose one provider and put its settings in a repository-root `.env` file. AppHost reads every key;
Docker Compose passes only the provider and `AWS_*` keys listed in `docker-compose.yml`. Start from the template; `.env` is git- and Docker-ignored:

```sh
cp .env.example .env
```

Keys use environment-variable form (`Bedrock__ApiKey` for `Bedrock:ApiKey`); blank values are ignored.
`.env` overrides AppHost appsettings and user-secrets, while real environment variables and
command-line arguments still override `.env`. The `dotnet user-secrets` commands below remain an
alternative. Both AiService and A2AServer use the selected provider; restart the application after
changing it. Keys stay in server-side configuration, not browser settings. Never commit credentials.

#### Configure Azure OpenAI

Azure OpenAI remains the default when `Models:Provider` is absent. Existing Foundry configuration
continues to work. To select it explicitly, including when switching back from another provider:

```sh
dotnet user-secrets set "Models:Provider" "AzureOpenAI" --project src/AgenticLab.AppHost
dotnet user-secrets set "AzureOpenAI:Endpoint" "<your-azure-openai-endpoint>" --project src/AgenticLab.AppHost
dotnet user-secrets set "AzureOpenAI:Deployment" "<your-deployment-name>" --project src/AgenticLab.AppHost
dotnet user-secrets set "AzureOpenAI:ApiKey" "<your-api-key>" --project src/AgenticLab.AppHost
```

Use the **deployment name** you created in Azure, not just the model's name.

#### OpenAI API

```sh
dotnet user-secrets set "Models:Provider" "OpenAI" --project src/AgenticLab.AppHost
dotnet user-secrets set "OpenAI:Model" "<your-openai-model-id>" --project src/AgenticLab.AppHost
dotnet user-secrets set "OpenAI:ApiKey" "<your-openai-api-key>" --project src/AgenticLab.AppHost
```

Use an OpenAI API key, not a ChatGPT login. API usage is
[billed separately from ChatGPT subscriptions](https://help.openai.com/en/articles/8156019-how-can-i-move-my-chatgpt-subscription-to-the-api).

#### Gemini API

```sh
dotnet user-secrets set "Models:Provider" "Gemini" --project src/AgenticLab.AppHost
dotnet user-secrets set "Gemini:Model" "<your-gemini-model-id>" --project src/AgenticLab.AppHost
dotnet user-secrets set "Gemini:ApiKey" "<your-gemini-api-key>" --project src/AgenticLab.AppHost
```

Get the key from [Google AI Studio](https://aistudio.google.com/apikey). Gemini uses Google's
[OpenAI-compatible chat API](https://ai.google.dev/gemini-api/docs/openai), including streaming and
tool calls. This compatibility API is beta; provider-specific features outside chat are not exposed.

#### Amazon Bedrock

Bedrock uses its native [Converse API](https://docs.aws.amazon.com/bedrock/latest/userguide/conversation-inference.html).
Authenticate with either a [Bedrock API key](https://docs.aws.amazon.com/bedrock/latest/userguide/api-keys.html)
or AWS credentials, not both:

```sh
dotnet user-secrets set "Models:Provider" "Bedrock" --project src/AgenticLab.AppHost
dotnet user-secrets set "Bedrock:Region" "<aws-region, e.g. eu-north-1>" --project src/AgenticLab.AppHost
dotnet user-secrets set "Bedrock:Model" "<model-or-inference-profile-id>" --project src/AgenticLab.AppHost
# Either: a Bedrock API key, sent as a bearer token instead of AWS signing.
dotnet user-secrets set "Bedrock:ApiKey" "<your-bedrock-api-key>" --project src/AgenticLab.AppHost
# Or: a named profile from ~/.aws, including SSO profiles (run `aws sso login --profile <name>` first).
dotnet user-secrets set "Bedrock:Profile" "<aws-profile-name>" --project src/AgenticLab.AppHost
```

Create the API key in the Bedrock console for the same region. Store it as `Bedrock:ApiKey`; the
`AWS_BEARER_TOKEN_BEDROCK` variable from AWS's own examples is ignored by this app. Prefer a short-term key; long-term keys
are tied to an IAM user and should be scoped to Bedrock and rotated.

Copy the model ID or cross-region inference profile ID (for example one starting with `eu.`) from the
Bedrock console, and make sure the model is enabled for your account in that region. Choose a model
that supports tool use in the Converse API. Without `Bedrock:ApiKey`, `Bedrock:Profile` takes precedence
over `AWS_PROFILE`. Without either, the AWS SDK's [default credential chain](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html)
applies, which includes `AWS_PROFILE`, `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` /
`AWS_SESSION_TOKEN` and attached IAM roles; set only one kind to avoid ambiguity. The identity needs `bedrock:InvokeModel` and
`bedrock:InvokeModelWithResponseStream` for the model.

OpenAI, Gemini and Bedrock need no Azure credentials or deployment. Choose a model available to your
account that supports chat and function (tool) calling.

Environment variables work too: use `Models__Provider`, `OpenAI__ApiKey` / `OpenAI__Model`,
`Gemini__ApiKey` / `Gemini__Model`, or `Bedrock__Region` / `Bedrock__Model` with `Bedrock__ApiKey` or `Bedrock__Profile`. Aspire explicitly forwards only the selected connection settings
to the inference services. Live requests send context to that provider and may incur its charges.
See [model configuration](docs/agents.md#model-providers) for model overrides and validation rules.

### Build and Start

Using the .NET CLI:

```sh
dotnet build AgenticLab.slnx
dotnet run --project src/AgenticLab.AppHost
```

Alternatively, use the Aspire CLI from the repository root:

```sh
aspire run
```

Aspire finds the AppHost through [aspire.config.json](aspire.config.json), restores and builds
it and its dependencies, then starts the application. No separate build command is needed to run it.

Open the Aspire dashboard URL printed in the terminal, then open the **web** resource's endpoint.
The dashboard also provides service logs and traces.

Development startup enables **Default** and **Copilot 365**.
Start with a conversation in Default, or select **Copilot 365 / chat** and try:

> What is blocking the Q3 launch?

Watch the model and tool activity, inspect the captured data, or use Manual mode to advance one
step at a time. Copilot 365 is a representative demo over synthetic data, backed by your configured
model API, not a connection to Microsoft 365 or the Microsoft 365 Copilot product.

For a coding example, use [sample-workspace](sample-workspace/README.md) with the **Default**
host and **Coder**. Its short guide shows how to select the folder in Settings and try its sample content.

### Run With Docker

[docker-compose.yml](docker-compose.yml) builds the four service targets in [Dockerfile](Dockerfile).
Compose reads the same repository-root `.env` as AppHost, so `docker compose up --build -d` works
once it is filled in. AppHost user-secrets are not loaded into containers; shell environment
variables override `.env`. For example, in PowerShell without a `.env`:

```powershell
$env:Models__Provider = "AzureOpenAI"
$env:AzureOpenAI__Endpoint = "https://<resource>.openai.azure.com/"
$env:AzureOpenAI__Deployment = "<deployment>"
$env:AzureOpenAI__ApiKey = Read-Host "Azure OpenAI API key" -MaskInput
docker compose up --build -d
```

For OpenAI, Gemini or Bedrock, use the matching provider and environment variables described above.
For Bedrock in containers, export `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` and, for temporary
credentials, `AWS_SESSION_TOKEN` (or set `Bedrock__ApiKey`), plus `Bedrock__Region` and `Bedrock__Model`. Compose does not mount
`~/.aws`, so `Bedrock__Profile` and SSO profiles do not work there.
Enter keys locally, never in chat or committed files. Inference services require valid provider
configuration; this full-stack command is not a credential-free learning mode.

Open <http://localhost:8080>. Published ports are bound to localhost: Web 8080, MCP 8081 and
AiService 8083. Compose does not run the A2A server, so the Orchestrator agent has no remote
specialists there (use Aspire for A2A). Internal service discovery uses HTTP port 8080 through
`services__aiservice__http__0` and `services__mcpserver__http__0`;
`ASPNETCORE_HTTP_PORTS` alone does not configure outgoing connections. Compose uses Production
settings with Default and Copilot 365 (set `Examples__copilot365__Enabled=false` in `.env` to hide it),
without the Aspire dashboard.

After changing only Compose settings, run `docker compose up -d` from the credential-configured
terminal; rebuilding is unnecessary. Check `docker compose ps -a` and `docker compose logs --tail 100`.
Startup ordering is not readiness: if protocol discovery runs before a server is listening, use
Discovery to retry once the services are ready. Stop with `docker compose down`.

Web's data-protection keys are not persisted across container replacement. Clear this site's
cookies or use a private window if an old antiforgery cookie cannot be decrypted. This warning is
separate from service connection errors; the local sample does not disable antiforgery protection.

### Sign In With Microsoft Entra ID

Web can require a Microsoft Entra ID sign-in so only accounts in your tenant can open it. Register
an app in your tenant and add **Web** redirect URIs for each address you use: `/signin-oidc` and
`/signout-callback-oidc` on `http://localhost:5140` (Aspire) and/or `http://localhost:8080` (Docker).
Create a client secret, then add to `.env`:

```
AzureAd__TenantId=<directory-tenant-id>
AzureAd__ClientId=<application-client-id>
AzureAd__ClientSecret=<client-secret>
```

With `AzureAd__ClientId` set, every Web page requires sign-in (authorization-code flow with PKCE,
single tenant only) and the header shows the user with **Sign out**; without it Web stays open as
before. Any account your tenant accepts can sign in, including B2B guests; to limit it to specific
people, turn on **Assignment required** on the Enterprise application and assign users or groups.
Open the app as `localhost` (not `127.0.0.1`, which would not match the redirect URI), in Chrome, Edge
or Firefox: sign-in over plain `http://localhost` needs browsers that treat localhost as secure. AppHost passes these settings
only to Web. AiService, MCP and A2A still have no authentication: keep them unreachable from other
machines (Aspire and Compose bind them to localhost).

### Deploy Behind a Shared Host

The images run behind a reverse proxy that serves several apps on one host, such as the Grasp
sandbox Kubernetes cluster (`DsApplication` manifest `apps/sandbox/ds-agentic-lab` in
`grasp-labs/ds-application-manifests`). Three settings support that:

- `PathBase=/agentic-lab` serves Web under a URL prefix. Links, assets, cookies and the sign-in
  redirect URI include it, whether or not the proxy strips the prefix before forwarding. Set
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` when TLS ends at the proxy, so the redirect URI is `https`.
- `ParameterStore__Path=/some/path` (Web and AiService) loads every AWS Systems Manager parameter
  under that path at startup, decrypting SecureStrings and mapping `/` to `:`
  (`/some/path/Bedrock/ApiKey` becomes `Bedrock:ApiKey`). Use it for secrets the platform cannot pass as
  environment variables. AWS credentials and region come from the SDK default chain (`AWS_REGION`, node
  or pod role), and a failed load stops startup.
- `/alive` answers without sign-in in every environment, for load-balancer health checks. It reports
  only the process self-check; `/health` remains Development-only.

Register `https://<host><PathBase>/signin-oidc` and `/signout-callback-oidc` on the Entra app. Run one
Web replica: Blazor Server circuits and data-protection keys are per pod, so users sign in again after
a restart.

[infra/](infra) holds the Terraform for the ECR repositories (`ds-agentic-lab-<component>`), and the
[Makefile](Makefile) builds `linux/amd64` images and pushes them: run `make provision` once, then
`make deploy`, which refuses uncommitted changes, tags images with the commit hash and `latest`, and
prints the image references to pin in the manifest. The Dockerfile's SDK stage runs natively and
publishes portable output, so the amd64 build is fast on arm64 machines.

### Learning Only

To explore the guided lessons without configuring a model provider, run only the Web project:

```sh
dotnet run --project src/AgenticLab.Web
```

Open the listening URL printed in the terminal and visit `/learn`. The guide works without
the AI service; live chat requires the full setup above.

### Self-Contained Examples

Optional examples are registered as independent projects, keeping their domain code, UI, assets,
tests and documentation together. This fork keeps one example,
[Copilot 365](src/AgenticLab.Examples.Copilot365/README.md): workplace chat, research and analysis
over synthetic Microsoft 365 data. See the [example contributor guide](docs/examples.md) to add more.
The example owns its host-specific prompts, branding and tests. The shared learning guide remains
available independently of enabled examples. Enable it with `Examples:copilot365:Enabled=true`.
AppHost's Development settings enable it; use `--Examples:copilot365:Enabled=false` to disable it.
Other environments start with only Default unless the example is explicitly enabled.

For example, enable the workplace Copilot host and its three agents:

```sh
dotnet run --project src/AgenticLab.AppHost -- --Examples:copilot365:Enabled=true
```

## Documentation

| Guide | What You Will Find |
|-------|--------------------|
| [Agents and API](docs/agents.md) | Agent personas, chat API, conversation memory, prompts, and model configuration. |
| [Web Flow Workspace](docs/web-flow-page.md) | Live visualization, panels, captured context, and execution controls. |
| [Blazor Design System](docs/design-system.md) | Shared tokens, components, local fonts, accessibility, and the development catalogue. |
| [Execution Explorer](docs/execution-explorer.md) | Replay, breakpoints, streaming/control API, and retention limits. |
| [Learning](docs/learning.md) | Guided lessons and the contextual Learn panel. |
| [Workspace Features](docs/workspace.md) | Workspace agents, skills, custom instructions, and file/terminal tools. |
| [Protocols](docs/protocols.md) | MCP tools, A2A agents, discovery, and protocol integration tests. |
| [Example Modules](docs/examples.md) | Self-contained community examples, extension contracts and registration. |
| [Architecture and Conventions](AGENTS.md) | Project structure and implementation guidance for contributors. |
| [Browser Checks](tools/README.md) | Responsive UI smoke checks and separate browser load-test setup. |
| [Security Policy](SECURITY.md) | Private vulnerability reporting, tool risks, credentials, and data boundaries. |
| [Contributing](CONTRIBUTING.md) | Development workflow, checks, pull requests, and maintainer publication gates. |

## Contributing

Contributions can improve code, tests, documentation, lessons, or examples. Read
[CONTRIBUTING.md](CONTRIBUTING.md) for setup, credential-free .NET tests, browser checks,
and pull-request expectations. Discuss substantial changes in an issue first and keep changes focused.

Never include credentials, private workspace content, or sensitive captures in issues or pull requests.
Report suspected vulnerabilities privately through the [security policy](SECURITY.md).

## License

Originally conceived and created by [o-viken](https://github.com/o-viken). Code and documentation
are licensed under the [Apache License 2.0](LICENSE), except where otherwise noted.
See [NOTICE](NOTICE) for project attribution. Third-party assets retain their own licenses,
including the [Web icons](src/AgenticLab.Web/wwwroot/icons/lucide/LICENSE). Published container images include the
project license and attribution at `/app/LICENSE` and `/app/NOTICE`.