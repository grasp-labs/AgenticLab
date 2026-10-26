using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// The repository-root .env (also read by Docker Compose) overrides appsettings and user-secrets;
// real environment variables and command-line arguments still win, so they are re-applied after it.
builder.Configuration
    .AddInMemoryCollection(DotEnvFile.Read(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".env"))))
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// A tiny Model Context Protocol server exposing the local time, consumed by the AI service over MCP.
var timeMcp = builder.AddProject<Projects.AgenticLab_McpServer>("mcpserver")
    .WithHttpHealthCheck("/health");

// An Agent2Agent (A2A) server hosting a research sub-agent, called by the AI service's Orchestrator agent
// over the A2A protocol. It hosts an LLM agent, so it needs the same model provider settings as the AI
// service (injected from the AppHost user-secrets).
var a2aServer = builder.AddProject<Projects.AgenticLab_A2AServer>("a2aserver")
    .WithHttpHealthCheck("/health");

var aiService = builder.AddProject<Projects.AgenticLab_AiService>("aiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(timeMcp)
    .WaitFor(timeMcp)
    .WithReference(a2aServer)
    .WaitFor(a2aServer);

var modelProvider = builder.Configuration["Models:Provider"]?.Trim().ToUpperInvariant() switch
{
    null or "" or "AZUREOPENAI" => "AzureOpenAI",
    "OPENAI" => "OpenAI",
    "GEMINI" => "Gemini",
    "BEDROCK" => "Bedrock",
    _ => throw new InvalidOperationException("Unsupported configuration 'Models:Provider'. Choose AzureOpenAI, OpenAI, Gemini or Bedrock."),
};
aiService.WithEnvironment("Models__Provider", modelProvider);
a2aServer.WithEnvironment("Models__Provider", modelProvider);
string[] modelSettings = modelProvider switch
{
    "AzureOpenAI" => ["AzureOpenAI:Endpoint", "AzureOpenAI:Deployment", "AzureOpenAI:ApiKey", "AzureOpenAI:ForceDefaultModel"],
    "Bedrock" => ["Bedrock:Model", "Bedrock:Region", "Bedrock:ApiKey", "Bedrock:Profile"],
    _ => [$"{modelProvider}:Model", $"{modelProvider}:ApiKey"],
};
foreach (var key in modelSettings.Prepend("Models:ForceDefaultModel"))
{
    if (builder.Configuration[key] is not { } value) continue;
    var name = key.Replace(":", "__", StringComparison.Ordinal);
    aiService.WithEnvironment(name, value);
    a2aServer.WithEnvironment(name, value);
}
// Bedrock uses Bedrock:ApiKey when set; otherwise it signs with AWS credentials. A named profile is read
// from ~/.aws by each service; AWS_* credentials from the environment or .env are forwarded only for the
// selected provider.
if (modelProvider == "Bedrock")
{
    foreach (var name in new[] { "AWS_PROFILE", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN" })
    {
        if (builder.Configuration[name] is not { Length: > 0 } value) continue;
        aiService.WithEnvironment(name, value);
        a2aServer.WithEnvironment(name, value);
    }
}
string[] agentModelSettings = modelProvider == "AzureOpenAI" ? ["Model", "Deployment"] : ["Model"];
foreach (var agent in builder.Configuration.GetSection("Agents").GetChildren())
{
    foreach (var setting in agentModelSettings)
    {
        if (agent[setting] is { } value)
            aiService.WithEnvironment($"Agents__{agent.Key}__{setting}", value);
    }
}

// Blazor web UI that visualizes the live data flow through the agent. Reaches the AI service
// via service discovery and is exposed on an external HTTP endpoint.
var web = builder.AddProject<Projects.AgenticLab_Web>("web")
    .WithReference(aiService)
    .WaitFor(aiService)
    .WithExternalHttpEndpoints();

// Optional Entra ID sign-in for Web (AzureAd:TenantId, ClientId, ClientSecret); never sent to other services.
foreach (var setting in builder.Configuration.GetSection("AzureAd").AsEnumerable().Where(setting => setting.Value is { Length: > 0 }))
{
    web.WithEnvironment(setting.Key.Replace(":", "__", StringComparison.Ordinal), setting.Value);
}

if (builder.Configuration.GetSection("Examples").GetChildren().Any(example => example.GetValue<bool>("Enabled")))
{
    timeMcp.WithReference(aiService);
    foreach (var setting in builder.Configuration.GetSection("Examples").AsEnumerable().Where(setting => setting.Value is not null))
    {
        var name = setting.Key.Replace(":", "__", StringComparison.Ordinal);
        aiService.WithEnvironment(name, setting.Value);
        timeMcp.WithEnvironment(name, setting.Value);
        a2aServer.WithEnvironment(name, setting.Value);
        web.WithEnvironment(name, setting.Value);
    }
}

builder.Build().Run();
