# Workspace features (skills, instructions, workspace agents, tools)

Workspace agents read skills, instructions and agent definitions from a caller-selected folder on
the AiService machine. Set **Workspace** in Web Settings or send `workspace` with a
[chat request](agents.md#post-chat). Use only trusted folders: path checks and terminal restrictions
are **not a sandbox**. Read the [security policy](../SECURITY.md).

## Workspace skills (the Coder agent)

Skills are named playbooks loaded on demand through `ReadSkill`. Coder and all workspace-defined
agents support them. The model receives names/descriptions first, then requests a full body when needed.

Supported locations, relative to the workspace:

| Format | Locations |
| --- | --- |
| Folder with YAML-frontmatter `SKILL.md` | `skills/*/SKILL.md`, `.github/skills/*/SKILL.md`, `.claude/skills/*/SKILL.md` |
| Flat playbook, named from its file | `.github/prompts/*.prompt.md`, `.claude/commands/*.md` |

For example, a playbook at `skills/get-date/SKILL.md`:

```markdown
---
name: get-date
description: Get the current date and time on a Windows machine using the terminal.
---
Run Get-Date with PowerShell and report the result.
```

Skills default **on**. Uncheck them in Settings or send `disabledSkills` on either chat path;
disabled skills are neither advertised nor loadable through `ReadSkill`. Names match case-insensitively,
then by unambiguous partial match. A skill supplies guidance, not new tool permissions.

`POST /skills` accepts `{ Workspace }` and returns `{ Skills: [{ Name, Description }] }`.
Missing/invalid paths or no skills produce an empty list. Web reads this catalogue before a run.
[SkillLoader](../src/AgenticLab.AiService/Application/Skills/SkillLoader.cs) owns discovery and
deduplication; [SkillsTool](../src/AgenticLab.AiService/Application/Tools/SkillsTool.cs) loads bodies.
Code-defined agents opt in with `SupportsSkills` (default `false`).

## Workspace custom instructions (the instructions/ folder)

Any workspace-requiring agent can use custom instructions. Unlike skills, their **full body** is
injected without a tool call. They default **off**: select files in Settings or send their names in
`enabledInstructions` for the run.

Discovery reads `instructions/*.instructions.md` and `.github/instructions/*.instructions.md`, plus
whole-file `.github/copilot-instructions.md`, root `CLAUDE.md` and root `AGENTS.md`. Instruction files
may have a frontmatter `description`; names default to the filename. For example:

```markdown
---
description: How the assistant should format and sign off its replies in this workspace.
---
Keep replies concise and state what was verified.
```

Try the shipped [response-style instructions](../instructions/response-style.instructions.md) with
Ask, Plan or Coder and this repository as the workspace. The root agent instructions are also
available but are not injected unless explicitly enabled.

`POST /instructions` accepts `{ Workspace }` and returns `{ Instructions: [{ Name, Description }] }`,
or an empty list for a missing/invalid path or no files. Web shows the catalogue in Settings and the
expanded host; captured `llm-request` data shows the actual injected text.
[InstructionLoader](../src/AgenticLab.AiService/Application/Instructions/InstructionLoader.cs)
owns discovery, deduplication and the enabled-content block.

## Workspace-defined agents (the agents/ folder)

Agents can be declared in either format below. They can select existing backend capabilities, not
install tools or grant permissions beyond the platform's available set.

| Format | Locations | Persona |
| --- | --- | --- |
| YAML | `agents/*.agent.yaml`, `.agents/*.agent.yaml` | `persona` field |
| Markdown with optional YAML frontmatter | `.github/agents/*.md`, `.github/chatmodes/*.chatmode.md`, `.claude/agents/*.md`, `.agents/*.md` | Body after frontmatter, used verbatim |

A YAML definition such as [agents/reviewer.agent.yaml](../agents/reviewer.agent.yaml):

```yaml
name: Reviewer
description: Reviews the workspace's code for risks without changing anything.
tools:
  - ReadFile
  - ListFiles
risk: Low
persona: |
  Review the code for risks. Do not change files.
```

A Markdown definition can use editor tool aliases:

```markdown
---
name: mcp-agent
description: Describe what this custom agent does and when to use it.
tools: [read/readFile, search/fileSearch, search/listDirectory, web/fetch]
---
Read the workspace and fetch relevant reference pages. Do not change files.
```

Optional fields are `risk` (`None`, `Low`, `Medium`, `High`), `guardrails`, `model` (an Azure
deployment; see [precedence](agents.md#per-agent-models)) and `prompts` (a list of example messages,
trimmed and de-duplicated, returned as `suggestedPrompts` for the composer; they are never sent to the
model). Omitted risk defaults to High for
write/delete/terminal tools, otherwise Low; guardrails are derived from tools. These are metadata,
not extra enforcement. The parsed `skills` field does not disable skills: every workspace agent
requires a workspace, supports skills and receives `ReadSkill` even when omitted from its tool list.

YAML definitions take precedence over Markdown for the same name. Invalid/unnamed definitions and
Markdown files with empty bodies are skipped. Both chat paths try the built-in catalogue first,
then resolve workspace agents by name, case-insensitively.

`POST /agents/workspace` accepts `{ Workspace }` and returns agent metadata, including `ToolMappings`.
Web appends these agents to hosts that already offer a workspace mode, such as Default
(Ask, Plan and Coder). Discovery is per workspace, not a startup registration.

### Tool name mapping

YAML uses backend tool names directly. Markdown aliases match case-insensitively after the last `/`;
for example, `search/fileSearch` maps to `ListFiles`, not a full implementation of the editor's search.

| Backend tool | Aliases |
| --- | --- |
| `ReadFile` | `readFile`, `read` |
| `ListFiles` | `fileSearch`, `listDirectory`, `list`, `search`, `glob`, `grep`, `ls` |
| `WriteFile` | `editFile`, `edit`, `createFile`, `applyPatch`, `write`, `multiEdit` |
| `DeleteFile` | `deleteFile`, `delete` |
| `RunCommand` | `runCommands`, `runInTerminal`, `terminal`, `shell`, `runTasks`, `bash` |
| `ReadSkill` | `readSkill` |
| `AskQuestion` | `askQuestion`, `ask` |
| `WebFetch` | `fetch`, `web`, `webFetch` |

Unknown names are dropped, never replaced with broader access. Settings shows declared-to-backend
mappings and **no matching tool** for dropped aliases; YAML identity mappings are hidden.
[WorkspaceToolAliases](../src/AgenticLab.AiService/Application/Workspace/WorkspaceToolAliases.cs)
owns the map. [WorkspaceAgentLoader](../src/AgenticLab.AiService/Application/Workspace/WorkspaceAgentLoader.cs)
parses definitions; [WorkspaceAgentResolver](../src/AgenticLab.AiService/Application/Workspace/WorkspaceAgentResolver.cs)
limits them to application tools, not demo tools or arbitrary editor capabilities.

## Workspace-scoped tools (the Coder agent)

Coder offers `ReadFile`, `ListFiles`, `WriteFile` (create/overwrite), `DeleteFile` (files only),
`RunCommand` and `ReadSkill`. Ask and Plan have no write/delete/terminal tools. A missing or invalid
required workspace causes `POST /chat` to return `400`; `/chat/stream` emits `error` and stops.

[FileSystemTool](../src/AgenticLab.AiService/Application/Tools/FileSystemTool.cs) checks normalized
paths against the workspace root/prefix, rejecting paths outside it. Absolute paths inside it may
be accepted. **This is a lexical check, not symlink resolution or per-user access control.**

[TerminalTool](../src/AgenticLab.AiService/Application/Tools/TerminalTool.cs) uses the root as its
working directory and a 60-second timeout. `Coder:AllowedCommands` overrides the default executables:
`dotnet`, `git`, `ls`, `dir`, `npm`, `node`, `python`, `pip`, `powershell`, `pwsh`. Commands run through
`cmd.exe /c` on Windows or `/bin/sh -c` elsewhere, with shell operators/interpolation syntax and
newlines rejected. Coder receives the current OS, shell and allowlist in its environment instructions.

**Security limits:** command arguments are not checked for filesystem escape. Allowed interpreters,
build tools and package tools can execute code, access other files, inherit the service environment
and use the network with the service account's permissions. The allowlist, path checks and timeout
do not prevent that. Prompts, skills and manual stepping are not authorization controls.

### Workspace path suggestions

In Settings, **Repo base folders** accepts parent paths, one per line or separated by `;`.
`POST /workspaces` accepts `{ Bases }` and returns `{ Directories: [{ Path, Name, Base }] }`:
immediate subfolders only, excluding hidden/system folders, deduplicated and capped at 300.
Web suggests up to eight recent workspaces first, then these folders. Preferences stay in browser
storage under `agenticlab-workspace-bases` and `agenticlab-workspace-recent`; runs remember the
selected path. These endpoints inspect the service filesystem, not the browser's local files.

## Per-run implementation

[WorkspaceScope](../src/AgenticLab.AiService/Application/Workspace/WorkspaceScope.cs) carries the
root for singleton tools. [RunScopeSet](../src/AgenticLab.AiService/Application/Flow/RunScopeSet.cs)
groups workspace, tool/skill/instruction filters and other ambient scopes, reactivating them across
streaming advances. Both chat paths use it. Skill catalogues and enabled instruction bodies are
rebuilt per run, appended to base instructions and not persisted as those instruction blocks in the
conversation session. Tool results, including loaded skill content, can still enter conversation history.
