using AgenticLab.AiService.Application.Agents;
using AgenticLab.AiService.Application.Workspace;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class WorkspaceAgentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("agentic-lab-ws-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("read/readFile", "ReadFile")]
    [InlineData("search/fileSearch", "ListFiles")]
    [InlineData("RUNCOMMANDS", "RunCommand")]
    [InlineData("bash", "RunCommand")]
    [InlineData("web/fetch", "WebFetch")]
    public void ToolAliases_MapKnownTokensOnTheLastSegment(string token, string expected)
    {
        var (tools, mappings) = WorkspaceToolAliases.Map([token]);
        Assert.Equal([expected], tools);
        Assert.Equal(new ToolMapping(token, expected), Assert.Single(mappings));
    }

    [Fact]
    public void ToolAliases_DedupeResolvedToolsAndRecordDroppedTokens()
    {
        var (tools, mappings) = WorkspaceToolAliases.Map(["read", "readFile", " ", "vscode/openSimpleBrowser", "edit"]);

        Assert.Equal(["ReadFile", "WriteFile"], tools);
        // One mapping per declared token (blank skipped), even when two tokens resolve to the same tool.
        Assert.Equal(4, mappings.Count);
        Assert.Contains(new ToolMapping("vscode/openSimpleBrowser", null), mappings);
    }

    [Fact]
    public void YamlAgent_DeclaresBackendToolsDirectlyAndInfersRiskFromWriteAccess()
    {
        var file = Write("agents/reviewer.agent.yaml", """
            name: Reviewer
            description: Reviews code.
            tools: [ReadFile, ListFiles]
            persona: |
              Be thorough.
            """);

        var agent = WorkspaceAgentFileParser.TryParseYaml(file, "agents/reviewer.agent.yaml");

        Assert.NotNull(agent);
        Assert.Equal("Reviewer", agent.Name);
        Assert.Equal("Be thorough.", agent.Persona);
        Assert.Equal(["ReadFile", "ListFiles"], agent.ToolNames);
        Assert.Equal(AgentRiskLevel.Low, agent.RiskLevel);
        Assert.All(agent.ToolMappings, m => Assert.Equal(m.Declared, m.Mapped));
        Assert.Contains(agent.Guardrails, g => g.Contains("Confined to the workspace"));
        Assert.DoesNotContain(agent.Guardrails, g => g.Contains("allowlist"));
    }

    [Fact]
    public void YamlAgent_HonoursDeclaredRiskModelAndGuardrails()
    {
        var file = Write("agents/ops.agent.yaml", """
            name: Ops
            tools: [RunCommand]
            risk: medium
            model: gpt-5.3-codex
            skills: true
            guardrails: ["Only in CI"]
            persona: Run things.
            """);

        var agent = WorkspaceAgentFileParser.TryParseYaml(file, "agents/ops.agent.yaml")!;

        Assert.Equal(AgentRiskLevel.Medium, agent.RiskLevel);
        Assert.Equal("gpt-5.3-codex", agent.ModelId);
        Assert.True(agent.SupportsSkills);
        Assert.Equal(["Only in CI"], agent.Guardrails);
    }

    [Fact]
    public void YamlAgent_CollectsDistinctTrimmedPrompts()
    {
        var file = Write("agents/prompts.agent.yaml", """
            name: Prompter
            tools: [ReadFile]
            prompts:
              - "  Summarize the README.  "
              - ""
              - Summarize the README.
              - List the tests.
            persona: Suggest things.
            """);

        var agent = WorkspaceAgentFileParser.TryParseYaml(file, "agents/prompts.agent.yaml")!;

        Assert.Equal(["Summarize the README.", "List the tests."], agent.SuggestedPrompts);
        Assert.Empty(WorkspaceAgentFileParser.TryParseYaml(
            Write("agents/plain.agent.yaml", "name: Plain\ntools: [ReadFile]\npersona: Plain.\n"), "agents/plain.agent.yaml")!.SuggestedPrompts!);
    }

    [Fact]
    public void MarkdownAgent_MapsVsCodeTokensAndUsesTheBodyAsPersona()
    {
        var file = Write(".github/agents/mcp-agent.md", """
            ---
            description: A VS Code style agent.
            tools: [read/readFile, search/listDirectory, web/fetch, runCommands]
            ---
            You help with the repo.

            Be brief.
            """);

        var agent = WorkspaceAgentFileParser.TryParseMarkdown(file, ".github/agents/mcp-agent.md");

        Assert.NotNull(agent);
        Assert.Equal("mcp-agent", agent.Name);
        Assert.Equal("You help with the repo.\n\nBe brief.", agent.Persona);
        Assert.Equal(["ReadFile", "ListFiles", "WebFetch", "RunCommand"], agent.ToolNames);
        Assert.Equal(AgentRiskLevel.High, agent.RiskLevel);
        Assert.Contains(agent.Guardrails, g => g.Contains("allowlist"));
        Assert.Contains(new ToolMapping("search/listDirectory", "ListFiles"), agent.ToolMappings);
    }

    [Theory]
    [InlineData("agents/blank.agent.yaml", "")]
    [InlineData("agents/noname.agent.yaml", "description: no name here\ntools: [ReadFile]\n")]
    [InlineData("agents/broken.agent.yaml", "name: [unterminated\n  tools: - nope")]
    public void YamlAgent_SkipsUnnamedOrMalformedFiles(string relative, string content)
    {
        var file = Write(relative, content);
        Assert.Null(WorkspaceAgentFileParser.TryParseYaml(file, relative));
    }

    [Theory]
    [InlineData("---\nname: x\n---\n")]
    [InlineData("---\nname: x\nno closing fence\n")]
    [InlineData("no frontmatter at all\n")]
    public void MarkdownAgent_RequiresFrontmatterAndABody(string content)
    {
        var file = Write(".github/agents/bad.md", content);
        Assert.Null(WorkspaceAgentFileParser.TryParseMarkdown(file, ".github/agents/bad.md"));
    }

    [Fact]
    public void Loader_MergesConventionsAndPrefersYamlOnNameClash()
    {
        Write("agents/reviewer.agent.yaml", "name: Reviewer\npersona: yaml wins\ntools: [ReadFile]\n");
        Write(".github/agents/reviewer.md", "---\nname: Reviewer\n---\nmarkdown loses\n");
        Write(".claude/agents/poet.md", "---\ndescription: Writes verse.\n---\nRhyme.\n");
        Write("agents/README.md", "not an agent file");

        using var scope = WorkspaceScope.Begin(_root);
        var agents = new WorkspaceAgentLoader().Load();

        Assert.Equal(["poet", "Reviewer"], agents.Select(a => a.Name));
        Assert.Equal("yaml wins", agents.Single(a => a.Name == "Reviewer").Persona);
        Assert.Equal(".claude/agents/poet.md", agents.Single(a => a.Name == "poet").RelativePath);
    }

    [Fact]
    public void Loader_ReturnsNothingWithoutAWorkspaceScope()
    {
        Assert.Empty(new WorkspaceAgentLoader().Load());
    }

    [Fact]
    public void WorkspaceScope_TryBeginRejectsMissingOrBlankPaths()
    {
        Assert.Null(WorkspaceScope.TryBegin(null));
        Assert.Null(WorkspaceScope.TryBegin("   "));
        Assert.Null(WorkspaceScope.TryBegin(Path.Combine(_root, "does-not-exist")));

        using var scope = WorkspaceScope.TryBegin(_root);
        Assert.NotNull(scope);
        Assert.Same(scope, WorkspaceScope.Current);
    }
}
