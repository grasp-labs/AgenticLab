using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AgenticLab.Web;

/// <summary>
/// Optional Microsoft Entra ID single sign-on for the Web app. It is enabled only when
/// <c>AzureAd:ClientId</c> is configured; every page then requires a signed-in user from the configured
/// single tenant, while health endpoints stay anonymous. Without it the app behaves as before.
/// </summary>
internal static class EntraSignIn
{
    /// <summary>The local route that signs the user out of the app and Entra ID.</summary>
    public const string SignOutPath = "/signout";

    /// <summary>Whether sign-in is configured.</summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientId"]);

    /// <summary>Registers OpenID Connect sign-in and a fallback policy requiring an authenticated user.</summary>
    /// <exception cref="InvalidOperationException">Sign-in is enabled but a required setting is missing; values are never echoed.</exception>
    public static IServiceCollection AddEntraSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCascadingAuthenticationState();
        if (!IsEnabled(configuration))
        {
            // The pipeline always runs the authentication middleware, so its services must exist even unused.
            services.AddAuthentication();
            services.AddAuthorization();
            return services;
        }

        foreach (var key in new[] { "AzureAd:TenantId", "AzureAd:ClientSecret" })
        {
            if (string.IsNullOrWhiteSpace(configuration[key]))
                throw new InvalidOperationException($"Missing configuration '{key}'. Entra sign-in is enabled because AzureAd:ClientId is set.");
        }
        var tenant = configuration["AzureAd:TenantId"]!.Trim();
        if (!Guid.TryParse(tenant, out _))
            throw new InvalidOperationException("Invalid configuration 'AzureAd:TenantId'. Use your directory (tenant) ID so only your organisation can sign in.");

        // Authorization-code flow (with PKCE), redeemed with the client secret. Microsoft.Identity.Web would
        // otherwise use the ID-token flow; no token acquisition is enabled because Web calls no Entra API.
        services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(options =>
            {
                configuration.GetSection("AzureAd").Bind(options);
                options.ResponseType = OpenIdConnectResponseType.Code;
            });
        services.AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy);
        return services;
    }

    /// <summary>Maps <see cref="SignOutPath"/> when sign-in is enabled.</summary>
    public static WebApplication MapEntraSignOut(this WebApplication app)
    {
        if (IsEnabled(app.Configuration))
        {
            app.MapGet(SignOutPath, () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
        }
        return app;
    }
}
