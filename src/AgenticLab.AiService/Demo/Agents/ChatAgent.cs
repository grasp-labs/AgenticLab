using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Demo.Agents;

/// <summary>
/// A plain conversational agent with no tools. It answers from the model's own knowledge and
/// the conversation so far, with no Wikipedia lookups or calculator access. Because it has no
/// tools, it overrides the shared <see cref="AgentDefinitionBase.Harness"/> to drop the
/// tool-oriented operating rules.
/// </summary>
public sealed class ChatAgent : AgentDefinitionBase
{
    /// <summary>The catalog name this agent is registered and selected under.</summary>
    public const string AgentName = SharedAgentNames.Chat;

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Friendly conversational agent that chats from its own knowledge, with no tools.";

    /// <summary>
    /// Tool-free operating rules. Unlike the shared harness, this version makes no promises about
    /// grounding answers in tool results, since this agent has no tools to call.
    /// </summary>
    protected override string Harness =>
        "You run inside an automated agent harness that relays your replies back to the user. " +
        "You have no tools available, so answer from your own knowledge and the conversation so far. " +
        "Follow these rules on every turn: " +
        "be helpful, accurate and concise; " +
        "if you are unsure or do not know something, say so plainly instead of guessing; " +
        "do not claim to look things up or cite sources you cannot access; " +
        "keep your formatting clean and easy to read.";

    /// <inheritdoc />
    protected override string Persona =>
        "You are ChatAgent, a warm and friendly conversational companion. " +
        "You enjoy casual chat and answering general questions in a relaxed, approachable tone. " +
        "Keep replies natural and conversational, and feel free to ask a follow-up question to keep " +
        "the conversation going.";

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts =>
    [
        "What is an AI agent, in one paragraph?",
        "Explain the difference between a harness and a model.",
    ];

    /// <inheritdoc />
    public override IList<AITool> Tools => [];
}
