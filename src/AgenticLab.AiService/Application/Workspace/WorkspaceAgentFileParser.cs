using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgenticLab.AiService.Application.Workspace;

/// <summary>
/// Parses a single workspace agent file into a <see cref="WorkspaceAgentDefinition"/>: either a pure YAML
/// <c>&lt;name&gt;.agent.yaml</c> (which declares backend tool names directly) or a markdown custom-agent
/// file whose <c>---</c>-delimited frontmatter carries the metadata and whose body is the persona (its
/// VS Code-style tool tokens go through <see cref="WorkspaceToolAliases"/>). Both return null for a file
/// that is unnamed, empty or malformed so one bad file never aborts discovery of the rest.
/// </summary>
internal static class WorkspaceAgentFileParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Parses one <c>.agent.yaml</c> file.</summary>
    internal static WorkspaceAgentDefinition? TryParseYaml(string file, string relativePath)
    {
        YamlAgent? yaml;
        try
        {
            yaml = Deserializer.Deserialize<YamlAgent>(File.ReadAllText(file));
        }
        catch
        {
            return null;
        }

        if (yaml is null || string.IsNullOrWhiteSpace(yaml.Name))
        {
            return null;
        }

        var tools = (yaml.Tools ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList();

        // The .agent.yaml convention declares backend tool names directly, so each mapping is an identity.
        var mappings = tools.Select(t => new ToolMapping(t, t)).ToList();

        return new WorkspaceAgentDefinition(
            yaml.Name.Trim(),
            (yaml.Description ?? string.Empty).Trim(),
            (yaml.Persona ?? string.Empty).Trim(),
            tools,
            ParseRisk(yaml.Risk, tools),
            Guardrails(yaml.Guardrails, tools),
            yaml.Skills,
            string.IsNullOrWhiteSpace(yaml.Model) ? null : yaml.Model.Trim(),
            relativePath,
            mappings,
            Prompts(yaml.Prompts));
    }

    /// <summary>Parses one markdown custom-agent file (frontmatter + body-as-persona).</summary>
    internal static WorkspaceAgentDefinition? TryParseMarkdown(string file, string relativePath)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch
        {
            return null;
        }

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return null;
        }

        var frontmatterLines = new List<string>();
        var bodyStart = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                bodyStart = i + 1;
                break;
            }

            frontmatterLines.Add(lines[i]);
        }

        if (bodyStart < 0)
        {
            return null;
        }

        YamlAgent? yaml;
        try
        {
            yaml = Deserializer.Deserialize<YamlAgent>(string.Join('\n', frontmatterLines));
        }
        catch
        {
            return null;
        }

        var body = bodyStart < lines.Length
            ? string.Join('\n', lines[bodyStart..]).Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(yaml?.Name)
            ? Path.GetFileNameWithoutExtension(file)
            : yaml.Name.Trim();

        var (tools, mappings) = WorkspaceToolAliases.Map(yaml?.Tools ?? []);

        return new WorkspaceAgentDefinition(
            name,
            (yaml?.Description ?? string.Empty).Trim(),
            body,
            tools,
            ParseRisk(yaml?.Risk, tools),
            Guardrails(yaml?.Guardrails, tools),
            yaml?.Skills ?? false,
            string.IsNullOrWhiteSpace(yaml?.Model) ? null : yaml.Model!.Trim(),
            relativePath,
            mappings,
            Prompts(yaml?.Prompts));
    }

    private static IReadOnlyList<string> Prompts(List<string>? declared) =>
        (declared ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // Uses the declared risk when valid; otherwise infers it from whether the agent can change the workspace.
    private static AgentRiskLevel ParseRisk(string? declared, IReadOnlyList<string> tools)
    {
        if (!string.IsNullOrWhiteSpace(declared) &&
            Enum.TryParse<AgentRiskLevel>(declared.Trim(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return HasWriteAccess(tools) ? AgentRiskLevel.High : AgentRiskLevel.Low;
    }

    private static IReadOnlyList<string> Guardrails(List<string>? declared, IReadOnlyList<string> tools) =>
        declared is { Count: > 0 }
            ? declared.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList()
            : DefaultGuardrails(tools);

    // The guardrails enforced for a workspace agent, derived from the tools it was granted.
    private static IReadOnlyList<string> DefaultGuardrails(IReadOnlyList<string> tools)
    {
        var guardrails = new List<string> { "Confined to the workspace folder (no ../ or absolute-path escape)" };

        if (tools.Any(t => t.Equals("RunCommand", StringComparison.OrdinalIgnoreCase)))
        {
            guardrails.Add("Commands restricted to an allowlist of executables");
            guardrails.Add("Shell-operator chaining (& | ; etc.) is rejected");
            guardrails.Add("Commands time out after 60 seconds");
        }

        if (tools.Any(t => t.Equals("WriteFile", StringComparison.OrdinalIgnoreCase) ||
                           t.Equals("DeleteFile", StringComparison.OrdinalIgnoreCase)))
        {
            guardrails.Add("File tools operate on files only — cannot delete directories");
        }

        return guardrails;
    }

    private static bool HasWriteAccess(IReadOnlyList<string> tools) =>
        tools.Any(t =>
            t.Equals("WriteFile", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("DeleteFile", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("RunCommand", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("SendMail", StringComparison.OrdinalIgnoreCase));

    // The shape deserialized from an .agent.yaml file or a .md file's frontmatter. Names map to camelCase
    // YAML keys; the markdown convention simply omits Persona (the body is used instead).
    private sealed class YamlAgent
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Persona { get; set; }
        public List<string>? Tools { get; set; }
        public string? Risk { get; set; }
        public bool Skills { get; set; }
        public string? Model { get; set; }
        public List<string>? Guardrails { get; set; }
        public List<string>? Prompts { get; set; }
    }
}
