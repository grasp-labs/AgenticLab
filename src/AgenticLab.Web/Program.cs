using OpenTelemetry.Metrics;
using AgenticLab.Web;
using AgenticLab.Web.Components;
using AgenticLab.Web.Flow;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Copilot365;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery and resilience.
builder.AddServiceDefaults();
builder.Services.AddExample<Copilot365Example>(builder.Configuration, ExampleHost.Web);

// Optional Entra ID single sign-on (AzureAd:ClientId, TenantId, ClientSecret); off when not configured.
builder.Services.AddEntraSignIn(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Loads the in-app learning content (Concepts/<id>/meta.json + body.md) for the concept drawer.
builder.Services.AddSingleton<ConceptCatalog>();

builder.Services.AddOptions<ReplayRetentionOptions>()
    .BindConfiguration("ReplayRetention")
    .Validate(options => options.MaxArchivedExchanges >= 0 && options.MaxArchivedPayloadBytes >= 0,
        "Replay retention limits must be nonnegative.")
    .ValidateOnStart();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(ReplayHistory.MeterName, FlowRunController.MeterName));

// Talks to the AI service. Under Aspire orchestration the name resolves via service discovery;
// when run standalone, override with AiService:Url (e.g. https://localhost:7123).
var serviceUrl = builder.Configuration["AiService:Url"] ?? "https+http://aiservice";
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental but is the supported way to opt out.
builder.Services.AddHttpClient<AiServiceClient>(client => client.BaseAddress = new Uri(serviceUrl))
    // The flow stream is a long-lived, pausable SSE response; the default resilience timeouts and
    // retries would cancel a paused run and must not apply here.
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapEntraSignOut();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
