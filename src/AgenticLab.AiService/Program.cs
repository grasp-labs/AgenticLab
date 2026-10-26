using AgenticLab.AiService.Endpoints;
using AgenticLab.AiService.Startup;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Copilot365;

var builder = WebApplication.CreateBuilder(args);

// The /chat/stream SSE response can stay idle for a long time while the client pauses or
// single-steps the flow, so disable Kestrel's minimum response data rate to avoid aborting it.
builder.WebHost.ConfigureKestrel(options => options.Limits.MinResponseDataRate = null);

// OpenTelemetry, health checks, service discovery and resilience.
builder.AddServiceDefaults();

builder.Services
    .AddHarnessTools()
    .AddWorkspaceFeatures()
    .AddVendorHarnesses()
    .AddDemoAgents()
    .AddConversationMemory(builder.Configuration)
    .AddFlowTracing()
    .AddDiscovery();

builder.Services.AddExample<Copilot365Example>(builder.Configuration, ExampleHost.AiService);

var app = builder.Build();

// Discover the MCP server's tools and connect to the A2A server at startup so discovery-using agents pick
// them up, unless disabled via Discovery:OnStartup. Either way discovery can be (re)run on demand from the
// discovery page. Degrades gracefully when a server is unavailable.
if (app.Configuration.GetValue("Discovery:OnStartup", true))
{
    await app.Services.GetRequiredService<McpToolProvider>().ConnectAsync();
    await app.Services.GetRequiredService<A2AAgentProvider>().ConnectAsync();
}

app.MapDefaultEndpoints();
app.MapAgentEndpoints();
app.MapWorkspaceEndpoints();
app.MapDiscoveryEndpoints();
app.MapChatEndpoints();
app.MapExamples();

app.Run();
