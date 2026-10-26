# Browser checks

The Blazor browser tools live outside the solution. Neither adds npm work to a normal .NET build.
`web-smoke.mjs` checks presentation/navigation without model calls; `flow-loadtest.mjs` sends real chat
requests and measures concurrent browser sessions.

## Setup

Install Playwright and Chromium in a temporary location:

```sh
mkdir -p /tmp/agentic-lab-loadtest
npm install --prefix /tmp/agentic-lab-loadtest --no-save --package-lock=false playwright
node /tmp/agentic-lab-loadtest/node_modules/playwright/cli.js install chromium
```

Start the application first, for example with `dotnet run --project src/AgenticLab.AppHost`, and set
`AGENTICLAB_URL` to its externally reachable **web** resource. The example port
below is illustrative; use the actual URL. Only the `AGENTICLAB_*` variable names are supported.

## UI smoke

Use a **Development** Web instance with a reachable AI service catalogue. The script creates isolated
browser contexts; it does not overwrite preferences in your own browser, send chat, reset server
conversations, or invoke rediscovery. No Azure model request is made by the script.
The Default host must offer Orchestrator with at least one connected A2A agent for the per-agent
inspector checks; the normal Aspire setup supplies these agents.

```sh
AGENTICLAB_URL=http://127.0.0.1:5140 \
NODE_PATH=/tmp/agentic-lab-loadtest/node_modules \
node tools/web-smoke.mjs
```

AppHost's normal Development configuration uses
`AGENTICLAB_HOSTS=default,microsoft365`. For a Default-only profile, restart with
`--Examples:copilot365:Enabled=false` and use `AGENTICLAB_HOSTS=default`. This optional
comma-separated list asserts the exact available order; the script otherwise checks the returned
choices generically. Set `AGENTICLAB_HOST_ALIASES='{"Microsoft365":"microsoft365"}'`
to check legacy host values too; use `"default"` for each expected value in the disabled profile.
The smoke check restores selections in isolated contexts without resetting server conversations,
checks canonical keys case-insensitively and verifies an unavailable saved host falls back to Default.
Module logos are fetched from local RCL URLs, decoded to check nonblank pixels, checked for stable
dimensions, and captured at desktop/mobile widths. No vendor identities are hardcoded in the script.

Coverage includes 1440x1000, 1024x900, 390x844 and 1920x1080 viewports plus 200% CSS zoom:
conversation split/stack, draft retention between tabs, pointer/keyboard resizing, saved/legacy layout
restoration, reset, token-usage visibility defaults, keyboard toggle and saved/invalid preference
restoration, Execution maximise/collapse, independent Details/Learn docks, host-only anatomy
with the Client Learn topic retained, individual A2A
inspection from chips/headings/catalogue entries, keyboard focus restoration, Discovery focus containment and Escape/backdrop
dismissal, lesson progression and detailed diagrams, shared-control states, reduced motion and 404
pages. It checks locally loaded fonts and page/toolbar overflow. Screenshots go to the system temporary
directory under `agentic-lab-web-smoke`; override with `AGENTICLAB_SCREENSHOTS`.

The development catalogue is checked at `/design-system`. Separately verify a **published Production**
instance returns 404 there. `dotnet run --no-build` against development output is not a valid production
asset check; use `dotnet publish` and run the published DLL with its output as the content root.

This is not a replacement for live run verification. Auto/manual pacing, breakpoints, answers,
pause/resume/stop, replay and workspace-agent changes should also be exercised against a deterministic
local API or a configured service. A real service may incur model cost. The Web unit suite covers the
state and replay contracts without credentials:

```sh
dotnet test tests/AgenticLab.Web.Tests/AgenticLab.Web.Tests.csproj
```

## Load test

Optional example modules own their scenario-specific checks inside their project folders; see
the [example catalogue](../docs/examples.md). The core smoke script remains domain-neutral.

`flow-loadtest.mjs` exercises the Interactive Server Flow page with concurrent browser contexts.
The defaults are 10 concurrent users and 3 rounds. `AGENTICLAB_WARMUP_MS` sets the startup wait
(default 1000 ms), and `AGENTICLAB_MESSAGE` sets the message prefix (default `Load test message`).

Users run concurrently; rounds within each user's page run sequentially. The harness waits for Blazor
interactivity before filling the form and matches each submitted message to its new exchange. A failed
round is reported with its user/round and stops that user's remaining rounds. Against a real AI service,
this sends real model requests and can incur cost; use a fake API for an initial harness smoke test.

```sh
AGENTICLAB_URL=http://127.0.0.1:5140 \
AGENTICLAB_USERS=25 \
AGENTICLAB_ROUNDS=5 \
NODE_PATH=/tmp/agentic-lab-loadtest/node_modules \
node tools/flow-loadtest.mjs
```

The JSON output reports `firstExchangeMs` (local exchange appearance, not the first backend event),
completion time, successful sample count, Chromium JS heap when available, and page/run errors.
The browser heap estimate may be rounded or shared across pages and is not total browser memory.
Missing servers and initialization failures return a nonzero exit code with an error message.
Record Web and AiService process working set, managed heap, allocation rate,
`AgenticLab.Web.Flow` metrics, `AgenticLab.AiService.Flow` metrics and latency from Aspire/OpenTelemetry
at the same time. Repeat the profile for idle tabs, active runs, paused/manual runs, long conversations,
New conversation and closed tabs. Run the profile before and after any CSR migration; it is not a CI test
and it does not claim to measure server managed heap by itself.