using System.ClientModel.Primitives;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AgenticLab.ModelProviders;

/// <summary>Creates raw model clients; each consuming host remains responsible for its own execution pipeline.</summary>
public sealed class ModelClientFactory
{
    private readonly ModelConnectionOptions _connection;
    private readonly PipelineTransport? _transport;
    private readonly HttpClientFactory? _awsHttpClientFactory;
    private readonly Lazy<AWSCredentials?> _awsCredentials;

    /// <summary>Uses validated startup settings without contacting the provider until a model request is made.</summary>
    public ModelClientFactory(ModelConnectionOptions connection) : this(connection, null) { }

    internal ModelClientFactory(ModelConnectionOptions connection, PipelineTransport? transport)
    {
        _connection = connection;
        _transport = transport;
        // One credentials object per factory, so SSO tokens are shared by every model's client.
        _awsCredentials = new(() => ProfileCredentials(connection.AwsProfile));
    }

    internal ModelClientFactory(ModelConnectionOptions connection, HttpClientFactory awsHttpClientFactory, AWSCredentials awsCredentials)
    {
        _connection = connection;
        _awsHttpClientFactory = awsHttpClientFactory;
        _awsCredentials = new(awsCredentials);
    }

    /// <summary>Creates a client for an explicit model or deployment, falling back to the configured default.</summary>
    public IChatClient Create(string? model = null)
    {
        var modelId = string.IsNullOrWhiteSpace(model) ? _connection.DefaultModel : model.Trim();
        if (_connection.Provider == ModelProvider.Bedrock) return CreateBedrock(modelId);
        if (_connection.Provider == ModelProvider.AzureOpenAI)
        {
            var options = new AzureOpenAIClientOptions();
            options.AddPolicy(new NullDeltaStreamingPolicy(), PipelinePosition.PerCall);
            if (_transport is not null) options.Transport = _transport;
            return new AzureOpenAIClient(_connection.Endpoint, _connection.Credential!, options)
                .GetChatClient(modelId).AsIChatClient();
        }

        var nativeOptions = new OpenAIClientOptions { Endpoint = _connection.Endpoint };
        nativeOptions.AddPolicy(new NullDeltaStreamingPolicy(), PipelinePosition.PerCall);
        if (_transport is not null) nativeOptions.Transport = _transport;
        var client = new OpenAIClient(_connection.Credential!, nativeOptions)
            .GetChatClient(modelId).AsIChatClient();
        return _connection.Provider == ModelProvider.Gemini ? new GeminiChatClient(client) : client;
    }

    private IChatClient CreateBedrock(string modelId)
    {
        var config = new AmazonBedrockRuntimeConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(_connection.AwsRegion) };
        if (_awsHttpClientFactory is not null) config.HttpClientFactory = _awsHttpClientFactory;
        if (_connection.BedrockApiKey is { } apiKey)
        {
            // A Bedrock API key replaces SigV4 signing; no AWS credentials are resolved.
            config.AWSTokenProvider = new StaticTokenProvider(apiKey);
            config.AuthSchemePreference = ["httpBearerAuth"];
            return new BedrockChatClient(new AmazonBedrockRuntimeClient(config).AsIChatClient(modelId));
        }

        // Without Bedrock:ApiKey, always sign with AWS credentials: the SDK would otherwise pick up an
        // ambient AWS_BEARER_TOKEN_BEDROCK and silently prefer it over the configured profile or chain.
        config.AWSTokenProvider = new AWSTokenProviderChain();
        config.AuthSchemePreference = ["sigv4"];
        var credentials = _awsCredentials.Value;
        var runtime = credentials is null
            ? new AmazonBedrockRuntimeClient(config)
            : new AmazonBedrockRuntimeClient(credentials, config);
        return new BedrockChatClient(runtime.AsIChatClient(modelId));
    }

    // Reads only local AWS config/credentials files; SSO tokens are refreshed by the SDK on first request.
    private static AWSCredentials? ProfileCredentials(string? profile) =>
        profile is null ? null
        : new CredentialProfileStoreChain().TryGetAWSCredentials(profile, out var credentials) ? credentials
        : throw new InvalidOperationException("Invalid configuration 'Bedrock:Profile'. The named AWS profile was not found in the local AWS config or credentials files.");
}
