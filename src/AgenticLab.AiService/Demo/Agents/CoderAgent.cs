using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Demo.Agents;

/// <summary>
/// A workspace-scoped coding agent that can read, list, write and delete files and run allowlisted
/// shell commands to generate and modify code in the user's chosen folder. It declares
/// <see cref="RequiresWorkspace"/> so the chat endpoints insist on a workspace path before it runs,
/// and it overrides the shared <see cref="AgentDefinitionBase.Harness"/> with stronger operating rules
/// suited to making real changes on disk.
/// </summary>
public sealed class CoderAgent(FileSystemTool files, TerminalTool terminal, SkillsTool skills) : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = SharedAgentNames.Coder;

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Workspace-scoped coding agent that reads, writes and deletes files and runs allowlisted commands.";

    /// <inheritdoc />
    public override bool RequiresWorkspace => true;

    /// <inheritdoc />
    public override bool SupportsSkills => true;

    /// <inheritdoc />
    /// <remarks>Highest risk: it writes and deletes files and runs commands on the host it executes on.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.High;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Confined to the workspace folder (no ../ or absolute-path escape)",
        "Commands restricted to an allowlist of executables",
        "Shell-operator chaining (& | ; etc.) is rejected",
        "Commands time out after 60 seconds",
        "File tools operate on files only — cannot delete directories",
        "Individual tools can be toggled off per run",
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "Create notes/skills.md with a one-line summary of each skill in this workspace.",
        "Use the get-date skill to tell me today's date, then explain which tool call it required.",
    ];

    /// <inheritdoc />
    protected override string Harness =>
        "You run inside an automated coding harness with direct, scoped access to the user's workspace " +
        "through a small set of tools (read, list, write and delete files, and run allowlisted shell " +
        "commands). Every tool is confined to the workspace folder — you cannot read or change anything " +
        "outside it. Follow these rules on every turn: " +
        "plan briefly before acting, and prefer reading or listing files to understand the project before you change it; " +
        "make the smallest change that satisfies the request and never modify files you were not asked to touch; " +
        "always use the file tools to read a file before overwriting it so you preserve existing content you don't mean to change; " +
        "ground everything you do in actual tool results — never claim a file was created, changed, or that a command succeeded unless a tool confirms it; " +
        "use the terminal only for allowlisted commands (e.g. build, test, version checks) and report the exit code and relevant output; " +
        "do not attempt to escape the workspace, delete directories, or run destructive commands; " +
        "when a workspace skill listed in the <skills> section fits the task, call the ReadSkill tool to load its full instructions and follow them rather than improvising; " +
        "when you finish, summarize exactly which files you created, updated or deleted and which commands you ran." +
        $"\n\n<environment>\n{terminal.EnvironmentInfo}\n</environment>";

    /// <inheritdoc />
    protected override string Persona =>
        "You are Coder, a pragmatic and careful senior software engineer. " +
        "You turn a request into working changes in the workspace: scaffolding new files, editing existing " +
        "code, and verifying your work with build or test commands when appropriate. " +
        "You write clean, idiomatic code that fits the conventions of the surrounding project, keep changes " +
        "focused, and explain what you changed and why in clear, concise terms.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. files.AsTools(), .. terminal.AsTools(), .. skills.AsTools()];
}
