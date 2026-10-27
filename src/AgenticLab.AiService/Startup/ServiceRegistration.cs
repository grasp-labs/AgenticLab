using OpenTelemetry.Metrics;
using AgenticLab.AiService.Demo.Agents;
using AgenticLab.AiService.Demo.Tools;
using AgenticLab.AiService.Demo.Vendors;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Extensibility.Runtime;

namespace AgenticLab.AiService.Startup;

/// <summary>
/// The service registrations for the AI service, grouped by concern so <c>Program.cs</c> reads as a short
/// composition of the pieces: harness tools, workspace features, demo agents, vendor harnesses, the chat
/// clients + agent catalog, conversation memory, the flow tracer and MCP/A2A discovery.
/// </summary>
internal static class ServiceRegistration
{
    /// <summary>The shared demo tools (Wikipedia and calculator) and the harness's own workspace/web/question tools.</summary>
    public static IServiceCollection AddHarnessTools(this IServiceCollection services)
    {
        // Wikipedia requires a descriptive User-Agent.
        services.AddHttpClient("wikipedia", client =>
        {
            client.BaseAddress = new Uri("https://en.wikipedia.org");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AgenticLab-WikiAssistant/1.0 (https://github.com/o-viken/agenticlab)");
        });
        services.AddSingleton(sp =>
            new WikiTool(sp.GetRequiredService<IHttpClientFactory>().CreateClient("wikipedia")));
        services.AddSingleton<CalculatorTool>();
        services.AddSingleton<IHostToolSource, DemoToolSource>();

        // Workspace-scoped tools for the coding agents.
        services.AddSingleton<FileSystemTool>();
        services.AddSingleton<TerminalTool>();

        // A bounded timeout keeps a slow or hostile endpoint from hanging a run.
        services.AddHttpClient("webfetch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AgenticLab-WebFetch/1.0 (https://github.com/o-viken/agenticlab)");
        })
        // Public addresses only, re-checked on every connection including redirects; no proxy, so the check sees the target.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { ConnectCallback = WebFetchTool.ConnectToPublicAddressAsync, UseProxy = false });
        services.AddSingleton(sp =>
            new WebFetchTool(sp.GetRequiredService<IHttpClientFactory>().CreateClient("webfetch")));

        // Lets an agent pause a streaming run to ask the user a clarifying question.
        services.AddSingleton<AskQuestionTool>();
        return services;
    }

    /// <summary>Workspace skills, custom instructions and user-authored agents, all discovered per run from the active workspace.</summary>
    public static IServiceCollection AddWorkspaceFeatures(this IServiceCollection services)
    {
        services.AddSingleton<SkillLoader>();
        services.AddSingleton<SkillMatcher>();
        services.AddSingleton<SkillsTool>();
        services.AddSingleton<InstructionLoader>();
        services.AddSingleton<WorkspaceAgentLoader>();
        services.AddSingleton<WorkspaceAgentResolver>();
        return services;
    }

    /// <summary>
    /// The vendor harness catalog and the built-in Default host. Optional examples contribute all
    /// branded hosts; Default's empty harness means "keep the agent's own harness".
    /// </summary>
    public static IServiceCollection AddVendorHarnesses(this IServiceCollection services)
    {
        services.AddSingleton<IVendorHarness, DefaultHarness>();
        services.AddSingleton<VendorHarnessCatalog>();
        return services;
    }

    /// <summary>The demo agent definitions (first registered is the default) and the catalog that builds them on their chat clients.</summary>
    public static IServiceCollection AddDemoAgents(this IServiceCollection services)
    {
        services.AddSingleton<ExampleCatalog>();
        services.AddSingleton<IAgentRunContext, AgentRunContext>();
        services.AddSingleton<IAgentDefinition, ChatAgent>();
        services.AddSingleton<IAgentDefinition, WikiAssistantAgent>();
        services.AddSingleton<IAgentDefinition, MathTutorAgent>();
        // services.AddSingleton<IAgentDefinition, TriviaMasterAgent>();
        services.AddSingleton<IAgentDefinition, AskAgent>();
        services.AddSingleton<IAgentDefinition, PlanAgent>();
        services.AddSingleton<IAgentDefinition, CoderAgent>();
        services.AddSingleton<IAgentDefinition, TimeKeeperAgent>();
        services.AddSingleton<IAgentDefinition, OrchestratorAgent>();

        // One cached chat client per Azure OpenAI deployment so agents can run on different models.
        services.AddSingleton<ChatClientProvider>();
        // The default deployment's client, for components not tied to a specific agent.
        services.AddSingleton(sp => sp.GetRequiredService<ChatClientProvider>().Get(null));
        services.AddSingleton(sp =>
            new AgentCatalog(sp.GetRequiredService<ChatClientProvider>(), sp.GetServices<IAgentDefinition>()));
        return services;
    }

    /// <summary>In-memory conversation memory (one thread per conversation id) with its sliding expiry and metrics.</summary>
    public static IServiceCollection AddConversationMemory(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ConversationStoreOptions>(configuration.GetSection("Conversations"));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ConversationStore>();
        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(ConversationStore.MeterName));
        return services;
    }

    /// <summary>The flow tracer that projects a real agent run into paced flow events, and the session registry that drives it.</summary>
    public static IServiceCollection AddFlowTracing(this IServiceCollection services)
    {
        services.AddSingleton<FlowControlRegistry>();
        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(FlowControlRegistry.MeterName));
        services.AddSingleton<FlowTracer>();
        return services;
    }

    /// <summary>MCP tool discovery, A2A agent discovery and the tracer that re-runs them as an observable process.</summary>
    public static IServiceCollection AddDiscovery(this IServiceCollection services)
    {
        services.AddSingleton<McpToolProvider>();
        services.AddSingleton<A2AAgentProvider>();
        services.AddSingleton<IMcpToolSource>(provider => provider.GetRequiredService<McpToolProvider>());
        services.AddSingleton<IAgentDelegation>(provider => provider.GetRequiredService<A2AAgentProvider>());
        services.AddSingleton<DiscoveryTracer>();
        return services;
    }
}
