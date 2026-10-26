# Sample Workspace

Use this folder as an example workspace for Agentic Lab's coding agents, available under the
**Default** host. It includes sample agents and skills.

1. [Run Agentic Lab locally](../README.md#run-locally).
2. In Live Flow, select **Default** and **Coder**.
3. Open **Settings** and set **Workspace** to this folder's absolute path on the AiService machine
   (for example, `C:\code\agenticlab\sample-workspace`).
4. Try: "List the files in this workspace and explain the available sample agents and skills."

This uses your configured model. Use only trusted files;
workspace tools are [not sandboxed](../SECURITY.md). See the [workspace guide](../docs/workspace.md) for more.

The **Codex** agent declares `model: gpt-5.3-codex`: an OpenAI model ID, or on Azure a deployment you
must have created with exactly that name. On other setups (including Bedrock and Gemini), use **Guide**
or **Scaffolder**, or set `Models__ForceDefaultModel=true` in `.env` (Azure also honours
`AzureOpenAI__ForceDefaultModel`) so every agent runs on the configured default model.
