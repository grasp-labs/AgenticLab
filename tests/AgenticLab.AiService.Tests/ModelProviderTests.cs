using System.ClientModel.Primitives;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using AgenticLab.AiService.Application.Agents;
using AgenticLab.AiService.Application.Flow;
using AgenticLab.ModelProviders;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class ModelProviderTests
{
    [Theory]
    [InlineData("AzureOpenAI", "null")]
    [InlineData("OpenAI", "null")]
    [InlineData("Gemini", "null")]
    [InlineData("AzureOpenAI", "{}")]
    [InlineData("OpenAI", "{}")]
    [InlineData("Gemini", "{}")]
    public async Task StreamingNullDeltaPreservesTextFinishReasonAndUsage(string provider, string delta)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new ResponseHandler(_ => Task.FromResult(StreamResponse($$$$"""
            : keepalive

            data: {"id":"reply-null","object":"chat.completion.chunk","created":1,"model":"test-model",
            data: "choices":[{"index":0,"delta":{"role":"assistant","content":"Hello","reasoning_content":"Thinking"},"finish_reason":null}]}

            data: {"id":"reply-null","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{{{{delta}}}},"finish_reason":"stop"}]}

            data: {"id":"reply-null","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[],"usage":{"prompt_tokens":4,"completion_tokens":1,"total_tokens":5,"prompt_tokens_details":{"cached_tokens":2}}}

            data: [DONE]

            """)));
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(new ModelConnectionOptions(Configuration(provider)),
            new HttpClientPipelineTransport(httpClient)).Create();

        using var capture = FlowCaptureScope.Begin();
        using var capturingClient = new CapturingChatClient(client);
        var response = await capturingClient.GetStreamingResponseAsync("Hi", cancellationToken: timeout.Token)
            .ToChatResponseAsync(cancellationToken: timeout.Token);

        Assert.Equal("Hello", response.Text);
        Assert.Equal("Thinking", Assert.Single(response.Messages.SelectMany(message => message.Contents)
            .OfType<TextReasoningContent>()).Text);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(5, response.Usage?.TotalTokenCount);
        Assert.Equal(4, response.Usage?.InputTokenCount);
        Assert.Equal(1, response.Usage?.OutputTokenCount);
        Assert.Equal(2, response.Usage?.CachedInputTokenCount);
        var usage = Assert.Single(capture.Turns).Usage;
        Assert.Equal(new FlowTokenUsage(4, 1, 5, 2), usage);
        var capturedEvent = new FlowEvent(2, "llm-response", "Response", Usage: usage);
        Assert.Equal(capturedEvent, JsonSerializer.Deserialize<FlowEvent>(JsonSerializer.Serialize(capturedEvent)));
    }

    [Theory]
    [InlineData("AzureOpenAI")]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    public async Task CachedUsagePreservesMissingDetailsAndSdkZeroDefaults(string provider)
    {
        foreach (var (details, expected) in new (string Details, long? Expected)[]
        {
            ("", null),
            (",\"prompt_tokens_details\":{\"cached_tokens\":0}", 0),
            (",\"prompt_tokens_details\":{}", 0),
        })
        {
            using var handler = new ResponseHandler(_ => Task.FromResult(StreamResponse($$$$"""
                data: {"id":"cache","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[],"usage":{"prompt_tokens":4,"completion_tokens":1,"total_tokens":5{{{{details}}}}}}

                data: [DONE]

                """)));
            using var httpClient = new HttpClient(handler);
            using var client = new ModelClientFactory(new ModelConnectionOptions(Configuration(provider)),
                new HttpClientPipelineTransport(httpClient)).Create();
            var response = await client.GetStreamingResponseAsync("Hi").ToChatResponseAsync();
            Assert.Equal(expected, response.Usage?.CachedInputTokenCount);
        }
    }

    [Theory]
    [InlineData("AzureOpenAI")]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    public async Task StreamingDeliversPartialEventsAndHonorsCancellation(string provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipe = new Pipe();
        await using var writer = pipe.Writer.AsStream();
        using var handler = new ResponseHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(pipe.Reader.AsStream())
            {
                Headers = { ContentType = new("text/event-stream") },
            },
        }));
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(new ModelConnectionOptions(Configuration(provider)),
            new HttpClientPipelineTransport(httpClient)).Create();
        await using var updates = client.GetStreamingResponseAsync("Hi", cancellationToken: timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        await writer.WriteAsync(Encoding.UTF8.GetBytes("data: {\"id\":\"partial\",\"object\":\"chat.completion.chunk\","), timeout.Token);
        await writer.WriteAsync(Encoding.UTF8.GetBytes("\"created\":1,\"model\":\"test-model\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n"), timeout.Token);

        Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
        Assert.Equal("Hello", updates.Current.Text);
        var pending = updates.MoveNextAsync().AsTask();
        timeout.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData("OpenAI", "https://api.openai.com/v1/chat/completions")]
    [InlineData("Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions")]
    public async Task NativeProvidersUseTheirOwnEndpointKeyAndModelWithoutAzure(string provider, string endpoint)
    {
        var configuration = Configuration(provider);
        configuration["Models:Provider"] = $" {provider.ToLowerInvariant()} ";
        configuration["AzureOpenAI:Endpoint"] = "unused-invalid-endpoint";
        configuration["AzureOpenAI:ForceDefaultModel"] = "unused-invalid-flag";
        var connection = new ModelConnectionOptions(configuration);
        using var handler = new ResponseHandler(async request =>
        {
            Assert.Equal(endpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            Assert.False(request.Headers.Contains("api-key"));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
            return JsonResponse("""
                {"id":"reply-1","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"Hello"},"finish_reason":"stop"}]}
                """);
        });
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(connection, new HttpClientPipelineTransport(httpClient)).Create();

        var response = await client.GetResponseAsync("Hi");

        Assert.Equal("Hello", response.Text);
        Assert.False(connection.ForceDefaultModel);
    }

    [Theory]
    [InlineData("AzureOpenAI", false)]
    [InlineData("AzureOpenAI", true)]
    [InlineData("OpenAI", false)]
    [InlineData("OpenAI", true)]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    public async Task ToolCallsSurviveStreamingAndConversationHistory(string provider, bool streaming)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var requests = 0;
        var invocations = new List<string>();
        using var handler = new ResponseHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(timeout.Token));
            var root = body.RootElement;
            Assert.Equal("EchoValue", root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
            if (requests++ == 0) return ToolResponse(streaming);

            var messages = root.GetProperty("messages").EnumerateArray().ToArray();
            var calls = messages.Single(message => message.TryGetProperty("tool_calls", out _)).GetProperty("tool_calls");
            Assert.Equal(2, calls.GetArrayLength());
            Assert.Equal(2, messages.Count(message => message.GetProperty("role").GetString() == "tool"));
            if (provider == "Gemini")
            {
                Assert.Equal("opaque-signature-1", calls[0].GetProperty("extra_content").GetProperty("google").GetProperty("thought_signature").GetString());
            }
            return FinalResponse(root.TryGetProperty("stream", out var stream) && stream.GetBoolean());
        });
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(new ModelConnectionOptions(Configuration(provider)),
            new HttpClientPipelineTransport(httpClient)).Create().AsBuilder().UseFunctionInvocation().Build();
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string value) => { invocations.Add(value); return value; }, name: "EchoValue")],
        };
        var history = new List<ChatMessage> { new(ChatRole.User, "Echo first and second.") };
        var response = streaming
            ? await client.GetStreamingResponseAsync(history, options, timeout.Token).ToChatResponseAsync(cancellationToken: timeout.Token)
            : await client.GetResponseAsync(history, options, timeout.Token);

        Assert.EndsWith("Done", response.Text);
        Assert.Equal(["first", "second"], invocations);
        history.AddRange(response.Messages);
        history.Add(new(ChatRole.User, "Continue."));
        history = JsonSerializer.Deserialize<List<ChatMessage>>(JsonSerializer.Serialize(history))!;
        var followUp = await client.GetResponseAsync(history, options, timeout.Token);
        Assert.EndsWith("Done", followUp.Text);
        Assert.Equal(3, requests);
    }

    [Fact]
    public void ExistingAzureConfigurationRemainsTheDefault()
    {
        var configuration = Configuration("AzureOpenAI");
        configuration["Models:Provider"] = null;
        configuration["AzureOpenAI:ForceDefaultModel"] = "true";

        var connection = new ModelConnectionOptions(configuration);

        Assert.Equal(ModelProvider.AzureOpenAI, connection.Provider);
        Assert.Equal("test-model", connection.DefaultModel);
        Assert.True(connection.ForceDefaultModel);
        configuration["Models:ForceDefaultModel"] = "false";
        Assert.False(new ModelConnectionOptions(configuration).ForceDefaultModel);
    }

    [Theory]
    [InlineData("OpenAI", "OpenAI:ApiKey")]
    [InlineData("OpenAI", "OpenAI:Model")]
    [InlineData("Gemini", "Gemini:ApiKey")]
    [InlineData("Gemini", "Gemini:Model")]
    [InlineData("AzureOpenAI", "AzureOpenAI:Endpoint")]
    [InlineData("Bedrock", "Bedrock:Model")]
    [InlineData("Bedrock", "Bedrock:Region")]
    public void MissingSelectedSettingsNameTheSettingWithoutExposingCredentials(string provider, string key)
    {
        var configuration = Configuration(provider);
        configuration[key] = " ";

        var error = Assert.Throws<InvalidOperationException>(() => new ModelConnectionOptions(configuration));

        Assert.Contains(key, error.Message);
        Assert.DoesNotContain("test-key", error.Message);
    }

    [Fact]
    public void UnknownProviderIsRejectedWithoutFallingBack()
    {
        var configuration = Configuration("OpenAI");
        configuration["Models:Provider"] = "unsupported";

        var error = Assert.Throws<InvalidOperationException>(() => new ModelConnectionOptions(configuration));

        Assert.Contains("Models:Provider", error.Message);
    }

    [Theory]
    [InlineData("AzureOpenAI", "azure-coder")]
    [InlineData("OpenAI", "declared-model")]
    [InlineData("Gemini", "declared-model")]
    [InlineData("Bedrock", "declared-model")]
    public void AgentModelPrecedenceDoesNotLeakAzureDeploymentOverrides(string provider, string expected)
    {
        var configuration = Configuration(provider);
        configuration["Agents:Coder:Deployment"] = "azure-coder";
        var clients = new ChatClientProvider(configuration);
        var definition = new ModelDefinition("declared-model");

        Assert.Equal(expected, clients.ResolveDeployment(definition));
        configuration["Agents:Coder:Model"] = " selected-model ";
        Assert.Equal("selected-model", clients.ResolveDeployment(definition));
        configuration["Agents:Coder:Model"] = " ";
        configuration["Agents:Coder:Deployment"] = null;
        Assert.Equal("declared-model", clients.ResolveDeployment(definition));
        Assert.Equal("test-model", clients.ResolveDeployment(new ModelDefinition(null)));
    }

    [Theory]
    [InlineData("AzureOpenAI", "test-model")]
    [InlineData("OpenAI", "declared-model")]
    [InlineData("Gemini", "declared-model")]
    [InlineData("Bedrock", "declared-model")]
    public void NativeProvidersDoNotInheritTheAzureForceDefaultFlag(string provider, string expected)
    {
        var configuration = Configuration(provider);
        configuration["AzureOpenAI:ForceDefaultModel"] = "true";
        Assert.Equal(expected, new ChatClientProvider(configuration).ExecutionDeployment("declared-model"));

        configuration["Models:ForceDefaultModel"] = "true";
        var clients = new ChatClientProvider(configuration);
        Assert.Equal("declared-model", clients.ResolveDeployment(new ModelDefinition("declared-model")));
        Assert.Equal("test-model", clients.ExecutionDeployment("declared-model"));
        Assert.Equal("test-model", clients.Get(clients.ExecutionDeployment("declared-model"))
            .GetService<ChatClientMetadata>()!.DefaultModelId);
    }

    [Theory]
    [InlineData("AzureOpenAI", "Azure OpenAI")]
    [InlineData("OpenAI", "OpenAI")]
    [InlineData("Gemini", "Gemini")]
    [InlineData("Bedrock", "Amazon Bedrock")]
    public void ProviderNameNamesTheConfiguredBackendNotAHostBrand(string provider, string expected) =>
        Assert.Equal(expected, new ChatClientProvider(Configuration(provider)).ProviderName);

    [Theory]
    [InlineData("AzureOpenAI", true)]
    [InlineData("OpenAI", false)]
    [InlineData("Gemini", false)]
    [InlineData("Bedrock", false)]
    public void ClientCachingRespectsProviderModelIdentifierSemantics(string provider, bool ignoreCase)
    {
        var clients = new ChatClientProvider(Configuration(provider));
        Assert.Same(clients.Get(null), clients.Get(" test-model "));
        Assert.Same(clients.Get(null), clients.Get(" "));
        Assert.Equal(ignoreCase, ReferenceEquals(clients.Get("test-model"), clients.Get("TEST-MODEL")));
    }

    [Fact]
    public async Task AzureRequestsRetainDeploymentPathsAndApiKeyAuthentication()
    {
        using var handler = new ResponseHandler(request =>
        {
            Assert.Equal("/openai/deployments/test-model/chat/completions", request.RequestUri!.AbsolutePath);
            Assert.Contains("api-version=", request.RequestUri.Query);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("api-key")));
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(FinalResponse(false));
        });
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(new ModelConnectionOptions(Configuration("AzureOpenAI")),
            new HttpClientPipelineTransport(httpClient)).Create();

        Assert.Equal("Done", (await client.GetResponseAsync("Hello")).Text);
    }

    [Theory]
    [InlineData("AzureOpenAI", false)]
    [InlineData("AzureOpenAI", true)]
    [InlineData("OpenAI", false)]
    [InlineData("OpenAI", true)]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    public async Task InFlightRequestsHonorCancellation(string provider, bool streaming)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ResponseHandler(request => { started.TrySetResult(); return pending.Task; });
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(new ModelConnectionOptions(Configuration(provider)),
            new HttpClientPipelineTransport(httpClient)).Create();
        using var cancellation = new CancellationTokenSource();
        var operation = streaming
            ? client.GetStreamingResponseAsync("Hello", cancellationToken: cancellation.Token)
                .ToChatResponseAsync(cancellationToken: cancellation.Token)
            : client.GetResponseAsync("Hello", cancellationToken: cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task BedrockSignsConverseRequestsAndRoundTripsToolCalls()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var requests = 0;
        var invocations = new List<string>();
        using var handler = new ResponseHandler(async request =>
        {
            Assert.Equal("bedrock-runtime.eu-north-1.amazonaws.com", request.RequestUri!.Host);
            Assert.Equal("/model/test-model/converse", request.RequestUri.AbsolutePath);
            var authorization = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(",", values) : "";
            Assert.StartsWith("AWS4-HMAC-SHA256 Credential=test-access/", authorization);
            Assert.Contains("/eu-north-1/bedrock/aws4_request", authorization);
            Assert.DoesNotContain("test-secret", authorization);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(timeout.Token));
            var root = body.RootElement;
            Assert.Equal("EchoValue", root.GetProperty("toolConfig").GetProperty("tools")[0]
                .GetProperty("toolSpec").GetProperty("name").GetString());
            if (requests++ == 0)
            {
                return JsonResponse("""
                    {"output":{"message":{"role":"assistant","content":[
                      {"toolUse":{"toolUseId":"call-1","name":"EchoValue","input":{"value":"first"}}},
                      {"toolUse":{"toolUseId":"call-2","name":"EchoValue","input":{"value":"second"}}}]}},
                     "stopReason":"tool_use","usage":{"inputTokens":10,"outputTokens":4,"totalTokens":14},"metrics":{"latencyMs":1}}
                    """);
            }

            var results = root.GetProperty("messages").EnumerateArray()
                .SelectMany(message => message.GetProperty("content").EnumerateArray())
                .Where(content => content.TryGetProperty("toolResult", out _))
                .Select(content => content.GetProperty("toolResult").GetProperty("toolUseId").GetString()!)
                .ToArray();
            Assert.Equal(["call-1", "call-2"], results);
            return JsonResponse("""
                {"output":{"message":{"role":"assistant","content":[{"text":"Done"}]}},
                 "stopReason":"end_turn","usage":{"inputTokens":20,"outputTokens":1,"totalTokens":21},"metrics":{"latencyMs":1}}
                """);
        });
        using var client = BedrockClient(handler).AsBuilder().UseFunctionInvocation().Build();
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string value) => { invocations.Add(value); return value; }, name: "EchoValue")],
        };

        var response = await client.GetResponseAsync("Echo first and second.", options, timeout.Token);

        Assert.Equal("Done", response.Text);
        Assert.Equal(["first", "second"], invocations);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BedrockAdaptsSharedHistoryToConverseRules(bool toolsOffered)
    {
        // An earlier agent's tool turn, a blank final reply, then a new question: valid for the other
        // providers, but Converse needs toolConfig for tool blocks and strictly alternating roles.
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "Be brief."),
            new(ChatRole.User, "What is 2+2?"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "Calculate", new Dictionary<string, object?> { ["expression"] = "2+2" })]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "4")]),
            new(ChatRole.Assistant, ""),
            new(ChatRole.User, "And doubled?"),
        };
        var original = JsonSerializer.Serialize(history);
        JsonElement sent = default;
        using var handler = new ResponseHandler(async request =>
        {
            sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();
            return JsonResponse("""
                {"output":{"message":{"role":"assistant","content":[{"text":"8"}]}},"stopReason":"end_turn",
                 "usage":{"inputTokens":1,"outputTokens":1,"totalTokens":2},"metrics":{"latencyMs":1}}
                """);
        });
        using var client = BedrockClient(handler);
        var options = toolsOffered
            ? new ChatOptions { Tools = [AIFunctionFactory.Create((string expression) => expression, name: "Calculate")] }
            : new ChatOptions { Tools = [] };

        Assert.Equal("8", (await client.GetResponseAsync(history, options)).Text);

        var messages = sent.GetProperty("messages").EnumerateArray().ToArray();
        var roles = messages.Select(message => message.GetProperty("role").GetString()!).ToArray();
        Assert.Equal(["user", "assistant", "user"], roles);
        Assert.Equal("Be brief.", sent.GetProperty("system")[0].GetProperty("text").GetString());
        var blocks = messages.SelectMany(message => message.GetProperty("content").EnumerateArray()).ToArray();
        Assert.DoesNotContain(blocks, block => block.TryGetProperty("text", out var text) && string.IsNullOrWhiteSpace(text.GetString()));
        Assert.Equal(toolsOffered, sent.TryGetProperty("toolConfig", out _));
        Assert.Equal(toolsOffered, blocks.Any(block => block.TryGetProperty("toolUse", out _)));
        Assert.Equal(toolsOffered, blocks.Any(block => block.TryGetProperty("toolResult", out _)));
        var last = messages[^1].GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal("And doubled?", last[^1].GetProperty("text").GetString());
        if (!toolsOffered)
        {
            Assert.Contains("Calculate", messages[1].GetProperty("content")[0].GetProperty("text").GetString());
            Assert.Contains("4", last[0].GetProperty("text").GetString());
        }
        Assert.Equal(original, JsonSerializer.Serialize(history));
    }

    [Fact]
    public async Task BedrockStreamingDeliversTextFinishReasonAndCapturedUsage()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new ResponseHandler(request =>
        {
            Assert.Equal("/model/test-model/converse-stream", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(EventStream(
                    ("messageStart", """{"role":"assistant"}"""),
                    ("contentBlockDelta", """{"contentBlockIndex":0,"delta":{"text":"Hel"}}"""),
                    ("contentBlockDelta", """{"contentBlockIndex":0,"delta":{"text":"lo"}}"""),
                    ("contentBlockStop", """{"contentBlockIndex":0}"""),
                    ("messageStop", """{"stopReason":"end_turn"}"""),
                    ("metadata", """{"usage":{"inputTokens":4,"outputTokens":1,"totalTokens":5},"metrics":{"latencyMs":1}}""")))
                {
                    Headers = { ContentType = new("application/vnd.amazon.eventstream") },
                },
            });
        });
        using var client = BedrockClient(handler);

        using var capture = FlowCaptureScope.Begin();
        using var capturingClient = new CapturingChatClient(client);
        var response = await capturingClient.GetStreamingResponseAsync("Hi", cancellationToken: timeout.Token)
            .ToChatResponseAsync(cancellationToken: timeout.Token);

        Assert.Equal("Hello", response.Text);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(4, response.Usage?.InputTokenCount);
        Assert.Equal(1, response.Usage?.OutputTokenCount);
        Assert.Equal(5, response.Usage?.TotalTokenCount);
        var usage = Assert.Single(capture.Turns).Usage;
        Assert.Equal(4, usage?.InputTokenCount);
        Assert.Equal(5, usage?.TotalTokenCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BedrockInFlightRequestsHonorCancellation(bool streaming)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ResponseHandler(request => { started.TrySetResult(); return pending.Task; });
        using var client = BedrockClient(handler);
        using var cancellation = new CancellationTokenSource();
        var operation = streaming
            ? client.GetStreamingResponseAsync("Hello", cancellationToken: cancellation.Token)
                .ToChatResponseAsync(cancellationToken: cancellation.Token)
            : client.GetResponseAsync("Hello", cancellationToken: cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public void BedrockNeedsNoApiKeyAndUsesTheRegionalRuntimeEndpoint()
    {
        var configuration = Configuration("Bedrock");
        configuration["Bedrock:ApiKey"] = null;
        configuration["Models:Provider"] = " bedrock ";

        var connection = new ModelConnectionOptions(configuration);

        Assert.Equal(ModelProvider.Bedrock, connection.Provider);
        Assert.Equal(new Uri("https://bedrock-runtime.eu-north-1.amazonaws.com/"), connection.Endpoint);
        Assert.Equal("test-model", connection.DefaultModel);
        Assert.Null(connection.Credential);
    }

    [Fact]
    public async Task BedrockApiKeyUsesBearerAuthenticationInsteadOfAwsSigning()
    {
        var configuration = Configuration("Bedrock");
        configuration["Bedrock:ApiKey"] = " test-bedrock-key ";
        using var handler = new ResponseHandler(request =>
        {
            Assert.Equal("bedrock-runtime.eu-north-1.amazonaws.com", request.RequestUri!.Host);
            Assert.Equal("/model/test-model/converse", request.RequestUri.AbsolutePath);
            var authorization = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(",", values) : "";
            Assert.Equal("Bearer test-bedrock-key", authorization);
            return Task.FromResult(JsonResponse("""
                {"output":{"message":{"role":"assistant","content":[{"text":"Hello"}]}},"stopReason":"end_turn",
                 "usage":{"inputTokens":1,"outputTokens":1,"totalTokens":2},"metrics":{"latencyMs":1}}
                """));
        });
        using var client = BedrockClient(handler, configuration);

        Assert.Equal("Hello", (await client.GetResponseAsync("Hi")).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BedrockWithoutAnApiKeyIgnoresAnAmbientBearerTokenVariable(bool streaming)
    {
        const string variable = "AWS_BEARER_TOKEN_BEDROCK";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "ambient-bedrock-key");
        try
        {
            var authorization = "";
            using var handler = new ResponseHandler(request =>
            {
                authorization = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(",", values) : "";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            });
            using var client = BedrockClient(handler);

            await Assert.ThrowsAnyAsync<Exception>(() => streaming
                ? client.GetStreamingResponseAsync("Hi").ToChatResponseAsync()
                : client.GetResponseAsync("Hi"));

            Assert.StartsWith("AWS4-HMAC-SHA256 Credential=test-access/", authorization);
            Assert.DoesNotContain("ambient-bedrock-key", authorization);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void BedrockRejectsAnApiKeyCombinedWithAProfileWithoutExposingTheKey()
    {
        var configuration = Configuration("Bedrock");
        configuration["Bedrock:ApiKey"] = "test-bedrock-key";
        configuration["Bedrock:Profile"] = "work";

        var error = Assert.Throws<InvalidOperationException>(() => new ModelConnectionOptions(configuration));

        Assert.Contains("Bedrock:ApiKey", error.Message);
        Assert.Contains("Bedrock:Profile", error.Message);
        Assert.DoesNotContain("test-bedrock-key", error.Message);
    }

    [Fact]
    public void BedrockReportsAMissingNamedProfileWithoutContactingAws()
    {
        var configuration = Configuration("Bedrock");
        configuration["Bedrock:Profile"] = "agenticlab-test-profile-that-does-not-exist";

        var error = Assert.Throws<InvalidOperationException>(
            () => new ModelClientFactory(new ModelConnectionOptions(configuration)).Create());

        Assert.Contains("Bedrock:Profile", error.Message);
    }

    [Theory]
    [InlineData("AzureOpenAI:Endpoint", "not-an-endpoint")]
    [InlineData("AzureOpenAI:Endpoint", "file:///private/secret")]
    [InlineData("AzureOpenAI:Endpoint", "https://user:secret@example.com/")]
    [InlineData("Models:ForceDefaultModel", "secret")]
    [InlineData("AzureOpenAI:ForceDefaultModel", "secret")]
    [InlineData("Bedrock:Region", "eu-north-1.evil.example")]
    [InlineData("Bedrock:Region", "EU_secret")]
    public void InvalidSettingsReportTheirNameWithoutTheirValue(string setting, string value)
    {
        var configuration = Configuration(setting.StartsWith("Bedrock:", StringComparison.Ordinal) ? "Bedrock" : "AzureOpenAI");
        configuration[setting] = value;

        var error = Assert.Throws<InvalidOperationException>(() => new ModelConnectionOptions(configuration));

        Assert.Contains(setting, error.Message);
        Assert.DoesNotContain(value, error.Message);
        Assert.DoesNotContain("test-key", error.Message);
    }

    private static IConfigurationRoot Configuration(string provider) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Models:Provider"] = provider,
            // Bedrock without an API key signs with AWS credentials; API-key tests opt in explicitly.
            [$"{provider}:ApiKey"] = provider == "Bedrock" ? null : "test-key",
            [$"{provider}:{(provider == "AzureOpenAI" ? "Deployment" : "Model")}"] = "test-model",
            [$"{provider}:Endpoint"] = "https://example.openai.azure.com/",
            ["Bedrock:Region"] = "eu-north-1",
        }).Build();

    private static IChatClient BedrockClient(HttpMessageHandler handler, IConfiguration? configuration = null) =>
        new ModelClientFactory(new ModelConnectionOptions(configuration ?? Configuration("Bedrock")),
            new HandlerHttpClientFactory(handler), new Amazon.Runtime.BasicAWSCredentials("test-access", "test-secret")).Create();

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage ToolResponse(bool streaming) => streaming ? StreamResponse("""
        data: {"id":"tools-1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"EchoValue","arguments":"{\"value\":"},"extra_content":{"google":{"thought_signature":"opaque-signature-1"}}},{"index":1,"id":"call-2","type":"function","function":{"name":"EchoValue","arguments":"{\"value\":"}}]},"finish_reason":null}]}

        data: {"id":"tools-1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"first\"}"}},{"index":1,"function":{"arguments":"\"second\"}"}}]},"finish_reason":null}]}

        data: {"id":"tools-1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":null,"finish_reason":"tool_calls"}]}

        data: [DONE]

        """) : JsonResponse("""
        {"id":"tools-1","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","type":"function","function":{"name":"EchoValue","arguments":"{\"value\":\"first\"}"},"extra_content":{"google":{"thought_signature":"opaque-signature-1"}}},{"id":"call-2","type":"function","function":{"name":"EchoValue","arguments":"{\"value\":\"second\"}"}}]},"finish_reason":"tool_calls"}]}
        """);

    private static HttpResponseMessage FinalResponse(bool streaming) => streaming ? StreamResponse("""
        data: {"id":"reply-2","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"role":"assistant","content":"Do"},"finish_reason":null}]}

        data: {"id":"reply-2","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"content":"ne"},"finish_reason":"stop"}]}

        data: [DONE]

        """) : JsonResponse("""
        {"id":"reply-2","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"Done"},"finish_reason":"stop"}]}
        """);

    private static HttpResponseMessage StreamResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "text/event-stream"),
    };

    private sealed class ResponseHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request).WaitAsync(cancellationToken);
    }

    // Encodes AWS event-stream frames (prelude, string headers, JSON payload, CRC32s) as ConverseStream returns them.
    private static byte[] EventStream(params (string Type, string Json)[] events)
    {
        using var stream = new MemoryStream();
        foreach (var (type, json) in events)
        {
            var headers = new MemoryStream();
            foreach (var (name, value) in new[] { (":event-type", type), (":content-type", "application/json"), (":message-type", "event") })
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                var valueBytes = Encoding.UTF8.GetBytes(value);
                headers.WriteByte((byte)nameBytes.Length);
                headers.Write(nameBytes);
                headers.WriteByte(7);
                headers.Write([(byte)(valueBytes.Length >> 8), (byte)valueBytes.Length]);
                headers.Write(valueBytes);
            }
            var payload = Encoding.UTF8.GetBytes(json);
            var total = 12 + (int)headers.Length + payload.Length + 4;
            var frame = new List<byte>();
            frame.AddRange(BigEndian(total));
            frame.AddRange(BigEndian((int)headers.Length));
            frame.AddRange(BigEndian((int)Crc32(frame.ToArray())));
            frame.AddRange(headers.ToArray());
            frame.AddRange(payload);
            frame.AddRange(BigEndian((int)Crc32(frame.ToArray())));
            stream.Write(frame.ToArray());
        }
        return stream.ToArray();
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    private sealed class HandlerHttpClientFactory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
    {
        public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig) => new(handler, disposeHandler: false);
        public override bool UseSDKHttpClientCaching(Amazon.Runtime.IClientConfig clientConfig) => false;
        public override bool DisposeHttpClientsAfterUse(Amazon.Runtime.IClientConfig clientConfig) => false;
    }

    private sealed class ModelDefinition(string? model) : AgentDefinitionBase
    {
        public override string Name => "Coder";
        public override string Description => "Model routing test";
        protected override string Persona => "Test";
        public override string? ModelId => model;
        public override IList<AITool> Tools => [];
    }
}