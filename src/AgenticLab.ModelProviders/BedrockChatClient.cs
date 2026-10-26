using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgenticLab.ModelProviders;

/// <summary>
/// Adapts shared conversation history to Bedrock Converse rules, which the other providers do not impose.
/// Histories are shared across agents, so a turn may carry earlier tool calls while offering no tools;
/// Converse rejects tool blocks without a tool configuration, so those become plain text. Blank turns
/// are dropped and adjacent same-role turns merged, because Converse requires alternating roles.
/// The caller's messages are never mutated.
/// </summary>
internal sealed class BedrockChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Normalize(messages, options), options, cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(Normalize(messages, options), options, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    internal static List<ChatMessage> Normalize(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var flattenTools = options?.Tools is not { Count: > 0 };
        var result = new List<ChatMessage>();
        ChatMessage? previous = null;
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                result.Add(message);
                continue;
            }

            var contents = message.Contents
                .Select(content => flattenTools ? AsText(content) : content)
                .Where(content => content is not TextContent text || !string.IsNullOrWhiteSpace(text.Text))
                .ToList();
            if (contents.Count == 0) continue;

            // Converse has only user and assistant turns; tool results travel in user turns.
            var role = message.Role == ChatRole.Assistant ? ChatRole.Assistant : ChatRole.User;
            if (previous?.Role == role)
            {
                foreach (var content in contents) previous.Contents.Add(content);
                continue;
            }

            previous = new ChatMessage(role, contents) { AuthorName = message.AuthorName, MessageId = message.MessageId };
            result.Add(previous);
        }
        return result;
    }

    private static AIContent AsText(AIContent content) => content switch
    {
        FunctionCallContent call => new TextContent(
            $"[Earlier tool call {call.Name}: {JsonSerializer.Serialize(call.Arguments, AIJsonUtilities.DefaultOptions)}]"),
        FunctionResultContent result => new TextContent(
            $"[Earlier tool result: {JsonSerializer.Serialize(result.Result, AIJsonUtilities.DefaultOptions)}]"),
        _ => content,
    };
}
