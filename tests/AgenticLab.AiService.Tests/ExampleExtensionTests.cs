using System.Text.Json;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Extensibility.Runtime;
using AgenticLab.AiService.Application.Conversations;
using AgenticLab.AiService.Application.Flow;
using AgenticLab.AiService.Application.Agents;
using AgenticLab.AiService.Demo.Tools;
using AgenticLab.AiService.Demo.Agents;
using AgenticLab.AiService.Demo.Vendors;
using AgenticLab.AiService.Startup;
using AgenticLab.Examples.Copilot365;
using AgenticLab.Examples.Copilot365.Agents;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class ExampleExtensionTests
{
    [Theory]
    [InlineData(null, "default")]
    [InlineData("copilot365", "microsoft365")]
    public void HostExamplesRegisterOnlyTheEnabledHost(string? enabledId, string expectedKey)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(enabledId is null
            ? [] : new Dictionary<string, string?> { [$"Examples:{enabledId}:Enabled"] = "true" }).Build();
        var services = HostServices(configuration);
        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<VendorHarnessCatalog>();
        Assert.Equal(enabledId is null ? new[] { "default" } : new[] { "default", expectedKey },
            catalog.Vendors.Select(host => host.Key));
        Assert.Null(catalog.Resolve("default"));
        Assert.Equal(enabledId is null ? 0 : 1, provider.GetRequiredService<ExampleCatalog>().Modules.Count);
        var agentTypes = services.Where(service => service.ServiceType == typeof(IAgentDefinition))
            .Select(service => service.ImplementationType).ToArray();
        Assert.Equal(agentTypes.Length, agentTypes.Distinct().Count());
        Assert.Contains(typeof(ChatAgent), agentTypes);
        Assert.Contains(typeof(AskAgent), agentTypes);
        Assert.Contains(typeof(PlanAgent), agentTypes);
        Assert.Contains(typeof(CoderAgent), agentTypes);
        Assert.Empty(new ChatAgent().Tools);
    }

    [Fact]
    public void AllHostExamplesCoexistWithoutOwningSharedAgents()
    {
        var ids = new[] { "copilot365" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            ids.ToDictionary(id => $"Examples:{id}:Enabled", _ => (string?)"true")).Build();
        var services = HostServices(configuration);
        using var provider = services.BuildServiceProvider();
        var hosts = provider.GetRequiredService<VendorHarnessCatalog>().Vendors;
        Assert.Equal(new[] { "default", "microsoft365" },
            hosts.Select(host => host.Key));
        var types = services.Where(service => service.ServiceType == typeof(IAgentDefinition))
            .Select(service => service.ImplementationType).ToArray();
        Assert.Equal(types.Length, types.Distinct().Count());
        var examples = provider.GetRequiredService<ExampleCatalog>();
        Assert.Equal(ids.Length, examples.Modules.Count);
        foreach (var name in new[] { SharedAgentNames.Chat, SharedAgentNames.Ask, SharedAgentNames.Plan, SharedAgentNames.Coder })
            Assert.Null(examples.ForAgent(name));
        Assert.Equal("copilot365", examples.ForAgent(Microsoft365Agent.AgentName)?.Id);
        Assert.All(hosts.Where(host => host.Key != "default"), host => Assert.NotNull(
            provider.GetRequiredService<VendorHarnessCatalog>().Resolve(host.Key)));
    }

    [Fact]
    public void DefaultHostOffersWorkspaceModes()
    {
        var modes = new DefaultHarness().Modes.Select(mode => mode.Agent).ToArray();
        Assert.Equal(ChatAgent.AgentName, modes[0]);
        Assert.Contains(SharedAgentNames.Ask, modes);
        Assert.Contains(SharedAgentNames.Plan, modes);
        Assert.Contains(SharedAgentNames.Coder, modes);
    }

    private static IServiceCollection HostServices(IConfiguration configuration) => new ServiceCollection()
        .AddVendorHarnesses().AddDemoAgents()
        .AddExample<Copilot365Example>(configuration, ExampleHost.AiService);

    [Fact]
    public void HostToolsKeepExactSelectionOrderAndOriginalSchemas()
    {
        using var http = new HttpClient();
        var wiki = new WikiTool(http);
        var calculator = new CalculatorTool();
        var source = new DemoToolSource(wiki, calculator);
        var selected = source.GetTools(["Calculate", "GetWikiPage", "SearchWiki"]);
        Assert.Equal(new[] { "Calculate", "GetWikiPage", "SearchWiki" }, selected.Select(tool => tool.Name));
        var originals = wiki.AsTools().Concat(calculator.AsTools()).OfType<AIFunction>()
            .ToDictionary(tool => tool.Name);
        foreach (var tool in selected.Cast<AIFunction>())
        {
            Assert.Equal(originals[tool.Name].Description, tool.Description);
            Assert.Equal(originals[tool.Name].JsonSchema.GetRawText(), tool.JsonSchema.GetRawText());
        }
        Assert.Single(source.GetTools(["Calculate"]));
        Assert.Empty(source.GetTools([]));
    }

    [Theory]
    [InlineData("calculate")]
    [InlineData("WriteFile")]
    [InlineData("GetCurrentTime")]
    [InlineData("DelegateToAgent")]
    public void HostToolsRejectUnpublishedNames(string name)
    {
        using var http = new HttpClient();
        var source = new DemoToolSource(new WikiTool(http), new CalculatorTool());
        Assert.Throws<ArgumentException>(() => source.GetTools(["Calculate", name]));
    }

    [Fact]
    public async Task HostToolsInvokeExistingCalculatorAndWikipediaImplementations()
    {
        using var handler = new WikipediaHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://wikipedia.invalid") };
        var source = new DemoToolSource(new WikiTool(http), new CalculatorTool());
        var calculate = Assert.IsAssignableFrom<AIFunction>(Assert.Single(source.GetTools(["Calculate"])));
        var calculation = await calculate.InvokeAsync(new AIFunctionArguments { ["expression"] = "(6 * 2) + 3" });
        Assert.Equal("15", Assert.IsType<JsonElement>(calculation).GetString());
        Assert.Equal(0, handler.RequestCount);
        var search = Assert.IsAssignableFrom<AIFunction>(Assert.Single(source.GetTools(["SearchWiki"])));
        var result = await search.InvokeAsync(new AIFunctionArguments { ["query"] = "Ada Lovelace" });
        Assert.Contains("Ada Lovelace: Mathematician", Assert.IsType<JsonElement>(result).GetString());
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("/w/rest.php/v1/search/page", handler.RequestUri!.AbsolutePath);
        Assert.Contains("q=Ada%20Lovelace", handler.RequestUri.Query);
        var page = Assert.IsAssignableFrom<AIFunction>(Assert.Single(source.GetTools(["GetWikiPage"])));
        var content = await page.InvokeAsync(new AIFunctionArguments { ["title"] = "Ada Lovelace" });
        Assert.Contains("Analytical Engine", Assert.IsType<JsonElement>(content).GetString());
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("/api/rest_v1/page/summary/Ada%20Lovelace", handler.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task ArbitraryModuleRegistersOnlyEnabledRoleAndOwnsItsRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddExample<ParcelExample>(Configuration(true), ExampleHost.AiService);
        await using var app = builder.Build();
        app.MapExamples();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
        Assert.Equal("parcel-data", await client.GetStringAsync("/examples/parcels/api/status"));
        Assert.Contains("parcels", await client.GetStringAsync("/examples"));
        Assert.Single(app.Services.GetRequiredService<ExampleCatalog>().Modules);
        await app.StopAsync();

        var disabled = new ServiceCollection().AddExample<ParcelExample>(Configuration(false), ExampleHost.AiService);
        using var provider = disabled.BuildServiceProvider();
        Assert.Empty(provider.GetRequiredService<ExampleCatalog>().Modules);
        Assert.Null(provider.GetService<ParcelState>());
        var otherRole = new ServiceCollection().AddExample<ParcelExample>(Configuration(true), ExampleHost.Mcp);
        using var otherProvider = otherRole.BuildServiceProvider();
        Assert.Null(otherProvider.GetService<ParcelState>());
    }

    [Fact]
    public void DuplicateRegistrationsAreRejected()
    {
        var services = new ServiceCollection().AddExample<ParcelExample>(Configuration(true), ExampleHost.AiService);
        Assert.Throws<InvalidOperationException>(() => services.AddExample<ParcelExample>(Configuration(true), ExampleHost.AiService));
    }

    [Fact]
    public void HostPresentationIsOptionalAndDoesNotOwnSharedAgents()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Examples:branded:Enabled"] = "true",
        }).Build();
        using var provider = new ServiceCollection().AddExample<BrandedExample>(configuration, ExampleHost.Web)
            .BuildServiceProvider();
        var catalog = provider.GetRequiredService<ExampleCatalog>();
        var manifest = Assert.Single(catalog.Modules).Manifest;
        Assert.Empty(manifest.AgentNames);
        Assert.Null(catalog.ForAgent(SharedAgentNames.Chat));
        Assert.Equal("LegacyBrand", Assert.Single(manifest.HostPresentation["brand"].LegacyKeys));
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        Assert.Equal("ChatAgent", AgenticLab.AiService.Demo.Agents.ChatAgent.AgentName);
        Assert.Equal(SharedAgentNames.Ask, AgenticLab.AiService.Demo.Agents.AskAgent.AgentName);
        Assert.Equal(SharedAgentNames.Plan, AgenticLab.AiService.Demo.Agents.PlanAgent.AgentName);
        Assert.Equal(SharedAgentNames.Coder, AgenticLab.AiService.Demo.Agents.CoderAgent.AgentName);
    }

    [Fact]
    public void HostPresentationRejectsUnownedKeysAndAmbiguousAliases()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Examples:branded:Enabled"] = "true",
            ["Examples:unowned:Enabled"] = "true",
            ["Examples:collision:Enabled"] = "true",
            ["Examples:ambiguous:Enabled"] = "true",
            ["Examples:unsafe-icon:Enabled"] = "true",
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddExample<UnownedExample>(configuration, ExampleHost.Web));
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddExample<AmbiguousExample>(configuration, ExampleHost.Web));
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddExample<UnsafeIconExample>(configuration, ExampleHost.Web));
        var services = new ServiceCollection().AddExample<BrandedExample>(configuration, ExampleHost.Web);
        Assert.Throws<InvalidOperationException>(() => services.AddExample<AliasCollisionExample>(configuration, ExampleHost.Web));
    }

    [Fact]
    public async Task RunIdentitySurvivesReactivationAndConcurrentRunsStayIsolated()
    {
        var accessor = new AgentRunContext();
        async Task<string> Run(string conversation)
        {
            using var scopes = RunScopeSet.Begin(null, null, null, null, false, conversation, "test-agent");
            await Task.Yield();
            scopes.Activate();
            Assert.Equal("test-agent", accessor.Current?.AgentName);
            return accessor.Current!.ConversationId;
        }
        Assert.Equal(new[] { "first", "second" }, await Task.WhenAll(Run("first"), Run("second")));
        Assert.Null(accessor.Current);
    }

    private static IConfiguration Configuration(bool enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Examples:parcels:Enabled"] = enabled.ToString() }).Build();

    [Fact]
    public void RediscoveryUpdatesAdvertisedToolsAlongsideExecutableAgent()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://unused.invalid", ["AzureOpenAI:Deployment"] = "test",
            ["AzureOpenAI:ApiKey"] = "not-used-by-this-test",
        }).Build();
        var definition = new DiscoveredDefinition();
        var catalog = new AgentCatalog(new ChatClientProvider(configuration), [definition]);
        Assert.Empty(Assert.Single(catalog.Agents).Tools);
        Assert.True(catalog.TryResolve(definition.Name, out var before, out _));
        definition.CurrentTools = [AIFunctionFactory.Create(() => "result", "ParcelRead")];
        catalog.RefreshDiscoveryAgents();
        Assert.Equal("ParcelRead", Assert.Single(Assert.Single(catalog.Agents).Tools));
        Assert.True(catalog.TryResolve(definition.Name, out var after, out _));
        Assert.NotSame(before, after);
        definition.CurrentTools = [];
        catalog.RefreshDiscoveryAgents();
        Assert.Empty(Assert.Single(catalog.Agents).Tools);
    }

    private sealed class WikipediaHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            var body = RequestUri!.AbsolutePath switch
            {
                "/w/rest.php/v1/search/page" => """{"pages":[{"title":"Ada Lovelace","description":"Mathematician"}]}""",
                "/api/rest_v1/page/summary/Ada%20Lovelace" => """{"extract":"Ada Lovelace wrote about the Analytical Engine."}""",
                _ => throw new InvalidOperationException($"Unexpected Wikipedia path: {RequestUri.AbsolutePath}"),
            };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }

    private sealed class DiscoveredDefinition : AgentDefinitionBase
    {
        public override string Name => "Discovered";
        public override string Description => "Test discovery";
        protected override string Persona => "Test";
        public override bool SupportsMcp => true;
        public IList<AITool> CurrentTools { get; set; } = [];
        public override IList<AITool> Tools => CurrentTools;
    }

    public sealed class ParcelState;

    public sealed class BrandedExample : IExampleModule
    {
        public ExampleManifest Manifest { get; } = new("branded", "Branded", ["brand"], [])
        {
            HostPresentation = new Dictionary<string, ExampleHostPresentation>
            {
                ["brand"] = new("_content/Branded/host.svg", 10, "product") { LegacyKeys = ["LegacyBrand"] },
            },
        };
    }

    public sealed class UnownedExample : IExampleModule
    {
        public ExampleManifest Manifest { get; } = new("unowned", "Unowned", ["owned"], [])
        {
            HostPresentation = new Dictionary<string, ExampleHostPresentation> { ["other"] = new() },
        };
    }

    public sealed class AliasCollisionExample : IExampleModule
    {
        public ExampleManifest Manifest { get; } = new("collision", "Collision", ["legacybrand"], []);
    }

    public sealed class AmbiguousExample : IExampleModule
    {
        public ExampleManifest Manifest { get; } = new("ambiguous", "Ambiguous", ["first", "second"], [])
        {
            HostPresentation = new Dictionary<string, ExampleHostPresentation>
            {
                ["first"] = new() { LegacyKeys = ["second"] },
            },
        };
    }

    public sealed class UnsafeIconExample : IExampleModule
    {
        public ExampleManifest Manifest { get; } = new("unsafe-icon", "Unsafe", ["unsafe"], [])
        {
            HostPresentation = new Dictionary<string, ExampleHostPresentation>
            {
                ["unsafe"] = new("https://external.invalid/host.svg"),
            },
        };
    }

    public sealed class ParcelExample : IAiServiceExample
    {
        public ExampleManifest Manifest => new("parcels", "Parcels", ["parcel-host"], ["ParcelAgent"]);
        public void AddAiServices(IServiceCollection services, IConfiguration configuration) => services.AddSingleton<ParcelState>();
        public void MapApi(RouteGroupBuilder group) => group.MapGet("/status", () => Results.Text("parcel-data"));
    }
}