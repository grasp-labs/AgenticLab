namespace AgenticLab.Web.Learning;

internal sealed record LearningNode(string Id, string Title, string Detail);

internal sealed record LearningStage(
    string Id,
    string Title,
    string Summary,
    string Takeaway,
    string RealityLabel,
    bool PlatformMap,
    IReadOnlyList<string> ConceptIds,
    IReadOnlyList<string> HighlightedNodes,
    string? ActionLabel = null,
    string? ActionHref = null,
    bool Hidden = false);

internal static class AgentLearningJourney
{
    internal static IReadOnlyList<LearningNode> IntroductionSteps { get; } = Array.AsReadOnly<LearningNode>(
    [
        new("why", "Why", "Agents are becoming part of everyday work. Understanding them helps us see past the hype."),
        new("definition", "What is an agent?", "An agent is a system that can observe its environment, make decisions, and take actions."),
        new("purpose", "Purpose", "An agent is a system that works toward a goal on your behalf, choosing its next steps and adjusting to results within the permissions and limits it has been given."),
        new("what", "What makes this possible?", "An agent combines a model with software that manages context, tools and controls."),
        new("how", "How does it work?", "Follow a task from request to model decision, tool use and result."),
    ]);

    internal static IReadOnlyList<LearningNode> Nodes { get; } = Array.AsReadOnly<LearningNode>(
    [
        new("harness", "Agent host", "Manages context, instructions, tools, memory and execution controls"),
        new("model", "Model", "Reasons, plans and chooses the next step or final answer"),
        new("instructions", "Load instructions", "Load system rules, persona and task-specific guidance"),
        new("harness-context", "Gather context", "Gather the task, relevant information and observations for the next model request"),
        new("available-tools", "Make tools available", "Expose the tool definitions the model can request"),
        new("harness-memory", "Manage memory", "Retain conversation state and select relevant history for context"),
        new("execution-controls", "Enforce execution controls", "Check permissions, limits and approvals before executing actions"),
        new("anatomy-persona", "Agent persona", "Purpose and approach for the selected example"),
        new("anatomy-selected-tools", "Selected tools", "The configured subset exposed to this agent"),
        new("anatomy-settings", "Settings", "Model choice and enforced controls"),
        new("anatomy-task", "Task prompt", "The user's request for this turn"),
        new("anatomy-custom-instructions", "Custom instructions", "Applicable project guidance added to context"),
        new("anatomy-skills", "Skills", "Playbook descriptions, with a body loaded when needed"),
        new("loop-context", "Context", "The current instructions, messages and observations"),
        new("loop-decision", "Model decision", "Answer now or request a tool"),
        new("loop-execute", "Agent host executes", "Checks the request and runs only permitted actions"),
        new("loop-observe", "Observation", "The result becomes context for the next decision"),
        new("loop-answer", "Final answer", "Ends this turn, even without a tool call"),
        new("connected-agent", "Agent", "Uses external capabilities when needed"),
        new("mcp-tools", "Tool server", "Exposes tools the agent can call"),
        new("a2a-agent", "Another agent", "Accepts delegated tasks and returns results"),
        new("hosting-runtime", "Agent host", "Where context is managed and permitted actions execute"),
        new("hosting-model", "Model service", "Where inference happens, independently of the agent host"),
        new("hosting-access", "Tools and data", "What the runtime can reach with its identity"),
        new("hosting-owner", "Operational owner", "Who handles availability, changes, failures and cost"),
        new("foundry-runtime", "Agent runtime", "Runs a prompt agent or your hosted agent code"),
        new("foundry-model", "Model deployment", "Provides the model used for inference"),
        new("foundry-tools", "Connected tools", "Built-in capabilities, custom functions or MCP servers"),
        new("lifecycle-run", "Run", "Invoke a version on real or representative tasks"),
        new("lifecycle-observe", "Observe", "Inspect traces, results and failures"),
        new("lifecycle-evaluate", "Evaluate", "Check quality and safety against repeatable tests"),
        new("lifecycle-improve", "Improve", "Adjust instructions, tools or controls; test the next version"),
    ]);

    private static IReadOnlyList<LearningStage> AllStages { get; } = Array.AsReadOnly<LearningStage>(
    [
        new("why-agents", "Demystify",
            "AI agents, explained.",
            "Understand the parts. See the possibilities and limits.",
            "Introduction", false,
            ["agent", "guardrails"], []),
        new("model-to-agent", "Agent",
            "An agent is a system, not just a model. Its agent host manages context, instructions, tools, memory and execution controls; the model reasons over that input and chooses the next step or final answer.",
            "Agent = Agent host + Model. The model reasons. The agent host acts. Together, they form an agent.",
            "Available today", false,
            ["agent", "llm", "harness"], ["harness", "model"]),
        new("agent-landscape", "The Agentic Landscape",
            "Chat, coding, office and custom agents serve different purposes. Underneath, they share a recognizable foundation.",
            "Different purposes and products. The same underlying agentic principles.",
            "Illustrative", false,
            ["agent", "harness", "tools"], ["harness", "model"]),
        new("inside-the-harness", "Inside the agent host",
            "The agent host gathers context, loads instructions, makes tools available, manages memory and enforces execution controls. This machinery is also called the harness.",
            "The model proposes a next step. The agent host checks and executes it. Instructions guide; execution controls enforce.",
            "Available today", false,
            ["system-prompt", "context", "tools", "guardrails"],
            ["harness-context", "instructions", "available-tools", "harness-memory", "execution-controls"]),
        new("anatomy-of-agent", "Anatomy of an agent",
            "Shared guidance, an agent-specific configuration, and the context for one task. Build up the parts that work with the model.",
            "Instructions guide the model. The agent host manages tools and enforces permissions. Agent = Agent host + Model.",
            "Illustrative", false,
            ["persona", "tools", "custom-instructions", "skills"],
            ["instructions", "available-tools", "anatomy-persona", "anatomy-selected-tools", "anatomy-settings", "anatomy-task", "anatomy-custom-instructions", "anatomy-skills"]),
        new("agent-loop", "The agent loop",
            "User to agent host to model. The model chooses a tool request or an answer; the host checks and executes permitted requests, then returns results to the model.",
            "The model reasons and chooses. The agent host executes. Tool results inform the next decision; a final answer ends the turn.",
            "Available today", false,
            ["reasoning", "tools", "context"], ["loop-context", "loop-decision", "loop-execute", "loop-observe", "loop-answer"],
            "Open live flow", "./"),
        new("agents-everywhere", "Same foundation, different setting",
            "Purpose, hosting and triggers are separate choices. Compare illustrative configurations while the foundation stays the same.",
            "Tools and permissions change with the environment. A local agent host does not mean a local model.",
            "Illustrative", false,
            ["where-agents-run", "environment", "tools", "guardrails"], ["harness", "model"]),
        new("wider-ecosystem", "The wider ecosystem",
            "Agents can call external tools or delegate work to other agents when needed. MCP and A2A standardize these optional connections.",
            "A protocol standardizes a connection. It does not, by itself, make that connection safe.",
            "Available today", false,
            ["mcp", "a2a", "environment"],
            ["connected-agent", "mcp-tools", "a2a-agent"]),
        new("where-to-run", "Where should your agent run?",
            "Tools, data and connections inform where the agent host should run. Compare operating models: a personal runtime, an existing product, your own service, or a managed agent platform.",
            "Production is a change in responsibilities, not just a change of address. Staying local can be the right choice.",
            "Illustrative", false,
            ["where-agents-run", "environment", "guardrails", "securing-agents"],
            ["hosting-runtime", "hosting-model", "hosting-access", "hosting-owner"]),
        new("map-to-foundry", "Map to Microsoft Foundry",
            "A Foundry project organizes agents and connected resources. The familiar concepts map to managed platform capabilities.",
            "Hosting an agent and providing its model are different responsibilities.",
            "Conceptual mapping", true,
            ["where-agents-run", "llm", "tools"],
            ["foundry-runtime", "foundry-model", "foundry-tools"], Hidden: true),
        new("run-and-improve", "Run and improve",
            "Use evidence from runs and repeatable tests to improve the next version. This cycle spans versions, not tool calls within a turn.",
            "A trace shows what happened. An evaluation checks how well it worked.",
            "Today + future integration", false,
            ["guardrails", "securing-agents"],
            ["lifecycle-run", "lifecycle-observe", "lifecycle-evaluate", "lifecycle-improve"],
            "Open live flow", "./"),
    ]);

    internal static IReadOnlyList<LearningStage> Stages { get; } =
        Array.AsReadOnly(AllStages.Where(stage => !stage.Hidden).ToArray());

    internal static LearningNode Node(string id) => Nodes.First(node => node.Id == id);

    internal static LearningStage Resolve(string? id) => Stages[IndexOf(id)];

    internal static int IndexOf(string? id)
    {
        for (var index = 0; index < Stages.Count; index++)
        {
            if (string.Equals(Stages[index].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    internal static LearningStage Move(string? id, int offset) =>
        Stages[(int)Math.Clamp((long)IndexOf(id) + offset, 0, Stages.Count - 1)];

    internal static string Href(string id) => $"learn?stage={Uri.EscapeDataString(id)}";
}