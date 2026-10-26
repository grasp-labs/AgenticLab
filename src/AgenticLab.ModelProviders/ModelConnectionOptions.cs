using System.ClientModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace AgenticLab.ModelProviders;

/// <summary>The inference backend selected at application startup, independently of agent host branding.</summary>
public enum ModelProvider
{
    /// <summary>An Azure OpenAI deployment, including deployments managed through Foundry.</summary>
    AzureOpenAI,
    /// <summary>The direct OpenAI API.</summary>
    OpenAI,
    /// <summary>The Gemini API through Google's OpenAI-compatible chat endpoint.</summary>
    Gemini,
    /// <summary>Amazon Bedrock's Converse API, authenticated with a Bedrock API key or standard AWS credentials.</summary>
    Bedrock,
}

/// <summary>Validates only the selected backend's settings and keeps its credential out of public metadata.</summary>
public sealed class ModelConnectionOptions
{
    /// <summary>Reads server-side settings, retaining Azure OpenAI as the default for existing installations.</summary>
    public ModelConnectionOptions(IConfiguration configuration)
    {
        Provider = configuration["Models:Provider"]?.Trim().ToUpperInvariant() switch
        {
            null or "" or "AZUREOPENAI" => ModelProvider.AzureOpenAI,
            "OPENAI" => ModelProvider.OpenAI,
            "GEMINI" => ModelProvider.Gemini,
            "BEDROCK" => ModelProvider.Bedrock,
            _ => throw new InvalidOperationException(
                "Unsupported configuration 'Models:Provider'. Choose AzureOpenAI, OpenAI, Gemini or Bedrock."),
        };

        var section = Provider.ToString();
        if (Provider == ModelProvider.Bedrock)
        {
            AwsRegion = BedrockRegion(configuration);
            AwsProfile = string.IsNullOrWhiteSpace(configuration["Bedrock:Profile"]) ? null : configuration["Bedrock:Profile"]!.Trim();
            BedrockApiKey = string.IsNullOrWhiteSpace(configuration["Bedrock:ApiKey"]) ? null : configuration["Bedrock:ApiKey"]!.Trim();
            if (BedrockApiKey is not null && AwsProfile is not null)
                throw new InvalidOperationException("Ambiguous configuration: set either 'Bedrock:ApiKey' or 'Bedrock:Profile', not both.");
        }
        else
        {
            Credential = new ApiKeyCredential(Required(configuration, $"{section}:ApiKey"));
        }
        DefaultModel = Required(configuration, $"{section}:{(Provider == ModelProvider.AzureOpenAI ? "Deployment" : "Model")}");
        Endpoint = Provider switch
        {
            ModelProvider.OpenAI => new Uri("https://api.openai.com/v1/"),
            ModelProvider.Gemini => new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"),
            ModelProvider.Bedrock => new Uri($"https://bedrock-runtime.{AwsRegion}.amazonaws.com{(AwsRegion!.StartsWith("cn-", StringComparison.Ordinal) ? ".cn" : "")}/"),
            _ => AzureEndpoint(configuration),
        };
        ForceDefaultModel = OptionalBoolean(configuration, "Models:ForceDefaultModel")
            ?? (Provider == ModelProvider.AzureOpenAI
                && (OptionalBoolean(configuration, "AzureOpenAI:ForceDefaultModel") ?? false));
    }

    /// <summary>The actual inference provider, not a persona or illustrative product label.</summary>
    public ModelProvider Provider { get; }

    /// <summary>The server endpoint used by the selected provider.</summary>
    public Uri Endpoint { get; }

    /// <summary>The default Azure deployment name or direct-provider model identifier.</summary>
    public string DefaultModel { get; }

    /// <summary>Whether execution uses the default model while retaining agents' declared model labels.</summary>
    public bool ForceDefaultModel { get; }

    /// <summary>The API key for the OpenAI-protocol providers; null for Bedrock, which has its own settings.</summary>
    internal ApiKeyCredential? Credential { get; }

    /// <summary>
    /// A Bedrock API key sent as an HTTP bearer token instead of signing with AWS credentials, or null to
    /// use <see cref="AwsProfile"/> or the default credential chain.
    /// </summary>
    internal string? BedrockApiKey { get; }

    /// <summary>The AWS region hosting the Bedrock model; null for other providers.</summary>
    internal string? AwsRegion { get; }

    /// <summary>
    /// The named AWS profile (including SSO profiles) that signs Bedrock requests, or null to use the
    /// AWS SDK's default credential chain (environment credentials, <c>AWS_PROFILE</c>, attached roles).
    /// </summary>
    internal string? AwsProfile { get; }

    private static string Required(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"Missing configuration '{key}'. Set it in user-secrets or environment variables.")
            : configuration[key]!.Trim();

    private static Uri AzureEndpoint(IConfiguration configuration)
    {
        var value = Required(configuration, "AzureOpenAI:Endpoint");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("https" or "http")
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            throw new InvalidOperationException("Invalid configuration 'AzureOpenAI:Endpoint'. Use an absolute HTTP(S) service URL without credentials, query or fragment.");
        }
        return endpoint;
    }

    private static string BedrockRegion(IConfiguration configuration)
    {
        var value = Required(configuration, "Bedrock:Region");
        return Regex.IsMatch(value, "^[a-z]{2}(-[a-z]+)+-[0-9]+$")
            ? value
            : throw new InvalidOperationException("Invalid configuration 'Bedrock:Region'. Use an AWS region code such as eu-north-1.");
    }

    private static bool? OptionalBoolean(IConfiguration configuration, string key)
    {
        if (configuration[key] is not { } value) return null;
        return bool.TryParse(value, out var result) ? result
            : throw new InvalidOperationException($"Invalid configuration '{key}'. Use true or false.");
    }
}