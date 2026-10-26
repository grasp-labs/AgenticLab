using AgenticLab.ModelProviders;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using AgenticLab.Extensibility.Examples;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery and resilience.
builder.AddServiceDefaults();

var connection = new ModelConnectionOptions(builder.Configuration);
IChatClient chatClient = new ModelClientFactory(connection).Create()
    .AsBuilder()
    .UseFunctionInvocation()
    .UseOpenTelemetry()
    .Build();

builder.Services.AddSingleton(chatClient);

// The agents this server hosts are declared in configuration (A2A:Agents), so a new specialist agent can
// be added without code changes — just add an entry and restart. Each is a persona-only agent (no tools),
// keeping this server focused on demonstrating agent-to-agent (A2A) communication rather than tool use.
var configuredAgents = builder.Configuration.GetSection("A2A:Agents").Get<A2AAgentConfig[]>() ?? [];
if (configuredAgents.Length == 0)
{
    // Fallback so the server is useful even without configuration: a single research specialist.
    configuredAgents =
    [
        new A2AAgentConfig
        {
            Name = "research",
            Description = "A research specialist that answers general-knowledge questions.",
            Instructions = "You are a knowledgeable research specialist. Answer the caller's question " +
                "directly, accurately and concisely from your own knowledge. If you are unsure, say so.",
        },
    ];
}

// Register each configured agent and its A2A server, remembering the path each is mapped at.
configuredAgents = configuredAgents.Concat(builder.Services
    .Where(descriptor => descriptor.ServiceType == typeof(IExampleModule))
    .Select(descriptor => descriptor.ImplementationInstance).OfType<IA2AExample>()
    .SelectMany(module => module.Specialists)
    .Select(agent => new A2AAgentConfig { Name = agent.Name, Description = agent.Description, Instructions = agent.Instructions }))
    .ToArray();
if (configuredAgents.GroupBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
    throw new InvalidOperationException("Remote agent names must be unique across configuration and examples.");

var hosted = new List<HostedA2AAgent>();
foreach (var config in configuredAgents)
{
    if (string.IsNullOrWhiteSpace(config.Name))
    {
        continue;
    }

    var path = string.IsNullOrWhiteSpace(config.Path) ? $"/a2a/{config.Name}" : config.Path;
    var agent = builder.AddAIAgent(config.Name, instructions: config.Instructions);
    agent.AddA2AServer();
    hosted.Add(new HostedA2AAgent(config.Name, path, config.Description, agent));
}

var app = builder.Build();

app.MapDefaultEndpoints();

// Expose each agent over the A2A protocol (JSON-RPC binding) so other agents can delegate to it.
foreach (var agent in hosted)
{
    app.MapA2AJsonRpc(agent.Builder, agent.Path);
}

// Discovery: lets a client learn which agents this server hosts (name, path, description) so it can reach
// them without sharing this server's configuration. Adding an agent here surfaces it to clients automatically.
app.MapGet("/agents", () => Results.Ok(new A2AAgentsResponse(
    hosted.Select(a => new A2AAgentSummary(a.Name, a.Path, a.Description)).ToList())));

app.Run();

/// <summary>A hosted agent declared in configuration: the persona and the path it is exposed at.</summary>
internal sealed class A2AAgentConfig
{
    /// <summary>The agent's unique name (also used as the default path segment).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The route the A2A endpoints are mapped at; defaults to <c>/a2a/{Name}</c> when blank.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>A short, user-facing description of what the agent is good at.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The agent's system instructions (its persona).</summary>
    public string Instructions { get; set; } = string.Empty;
}

/// <summary>A registered agent plus its mapped path and the builder used to map its endpoints.</summary>
internal sealed record HostedA2AAgent(string Name, string Path, string Description, IHostedAgentBuilder Builder);

/// <summary>The discovery response listing the agents this server hosts.</summary>
internal sealed record A2AAgentsResponse(IReadOnlyList<A2AAgentSummary> Agents);

/// <summary>A discoverable agent's name, A2A path and description.</summary>
internal sealed record A2AAgentSummary(string Name, string Path, string Description);
