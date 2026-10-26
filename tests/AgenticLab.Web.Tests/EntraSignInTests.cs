using AgenticLab.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgenticLab.Web.Tests;

public sealed class EntraSignInTests
{
    private const string Tenant = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public async Task WithoutAClientIdTheAppStaysOpen()
    {
        using var provider = Services(new()).BuildServiceProvider();

        Assert.Null(provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy);
        // UseAuthentication always runs, so its services must resolve even when sign-in is off.
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Null(await schemes.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme));
    }

    [Fact]
    public async Task WithAClientIdEveryPageRequiresASignedInUser()
    {
        using var provider = Services(new()
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = Tenant,
            ["AzureAd:ClientId"] = "client",
            ["AzureAd:ClientSecret"] = "test-secret",
        }).BuildServiceProvider();

        var fallback = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;
        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, requirement =>
            requirement is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
        Assert.NotNull(await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme));
    }

    [Theory]
    [InlineData("AzureAd:TenantId", null)]
    [InlineData("AzureAd:ClientSecret", null)]
    [InlineData("AzureAd:TenantId", "organizations")]
    public void IncompleteOrMultiTenantSettingsFailWithoutEchoingTheSecret(string key, string? value)
    {
        var settings = new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = Tenant,
            ["AzureAd:ClientId"] = "client",
            ["AzureAd:ClientSecret"] = "test-secret",
            [key] = value,
        };

        var error = Assert.Throws<InvalidOperationException>(() => Services(settings));

        Assert.Contains(key, error.Message);
        Assert.DoesNotContain("test-secret", error.Message);
    }

    private static IServiceCollection Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddEntraSignIn(configuration);
    }
}
