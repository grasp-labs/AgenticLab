using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AgenticLab.ParameterStore;

/// <summary>
/// Loads configuration from AWS Systems Manager Parameter Store, for deployments that cannot pass secrets as
/// environment variables. Enabled only when <c>ParameterStore:Path</c> is set: every parameter under that path
/// becomes a configuration key with <c>/</c> mapped to <c>:</c> (<c>{path}/AzureAd/ClientSecret</c> becomes
/// <c>AzureAd:ClientSecret</c>), SecureString values are decrypted, and parameters override earlier sources for
/// the keys they define. Credentials and region come from the AWS SDK default chain; a failed load stops startup.
/// </summary>
public static class ParameterStoreConfiguration
{
    /// <summary>The configuration key holding the parameter path, e.g. <c>/my-app/sandbox</c>.</summary>
    public const string PathKey = "ParameterStore:Path";

    /// <summary>Adds the Parameter Store source when <see cref="PathKey"/> is set; call before reading settings.</summary>
    /// <exception cref="InvalidOperationException">The path is not absolute.</exception>
    public static TBuilder AddParameterStore<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var path = builder.Configuration[PathKey]?.Trim();
        if (string.IsNullOrEmpty(path)) return builder;
        if (!path.StartsWith('/'))
            throw new InvalidOperationException($"Invalid configuration '{PathKey}'. Use an absolute parameter path such as /my-app/sandbox.");

        builder.Configuration.AddSystemsManager(path);
        return builder;
    }
}
