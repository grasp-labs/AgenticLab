# Contributing to Agentic Lab

Contributions can improve code, tests, documentation, lessons, and examples. Discuss substantial
changes in an [issue](https://github.com/o-viken/agenticlab/issues/new/choose) before implementing
them. Use the bug form for reproducible defects and the feature form for proposed improvements.
Report suspected vulnerabilities privately as described in [SECURITY.md](SECURITY.md), not in issues.

## Development Setup

- Install the .NET 10 SDK and Git. See [Run Locally](README.md#run-locally) for application setup.
- For the full application, use Aspire CLI **13.5.4**, matching the pinned AppHost SDK. The
  SDK-paired CLI through `dnx` is the fallback when a compatible CLI is not on `PATH`.
- Docker is only needed when
  validating container builds, not for the standard local setup or .NET tests.
- Credentials for the selected Azure OpenAI, OpenAI, Gemini or Bedrock provider are needed for live model
  runs, not for building or deterministic tests. Keep them in AppHost user-secrets or server-side
  environment variables; never add them to source or frontend assets.
  Live runs can incur charges and transmit context. Read the [security policy](SECURITY.md).

The [learning-only Web guide](README.md#learning-only) works without Azure or Aspire orchestration.
Dependency restore still needs access to package feeds; protocol tests use loopback servers.

## Making a Change

1. Fork the repository and create a topic branch. Use clear commits describing the reason for the
   change; no special commit-message format is required.
2. Read [AGENTS.md](AGENTS.md) and the matching [feature guide](README.md#documentation). Follow the
   existing architecture and keep example-specific code, assets, tests, and docs in its module.
3. Keep pull requests focused. Avoid unrelated refactoring, dependency changes, and formatting.
   Reuse existing tests and fake-model helpers; do not add live Azure calls to ordinary tests.
4. Update tests and documentation alongside behavior changes. Update architecture/convention
   guidance when relevant. Check links, documented commands, and rendered Markdown for docs changes.
5. Open a pull request explaining the problem, scope, and verification. Include sanitized
   screenshots for UI changes, with desktop/mobile coverage where the layout changes. Identify
   checks not run, especially model-dependent behavior; never imply fake-model tests verify it.

Do not include API keys, private workspace content, sensitive captured prompts, personal data,
internal deployment details, or unredacted logs in issues, commits, screenshots, or pull requests.
Use synthetic fixtures. Confirm that contributed code and assets may be redistributed under the
project's [license](LICENSE), retaining any required third-party notices. Record the source,
license, and relevant provenance of new icons, fonts, images, copied snippets, and generated assets.
Vendor names and logos must not imply endorsement.

## Verification

Run from the repository root. The solution includes the main tests and nested example test projects.
The credential-free CI baseline is:

```sh
dotnet restore AgenticLab.slnx
dotnet build AgenticLab.slnx --configuration Release --no-restore
dotnet test AgenticLab.slnx --configuration Release --no-build --no-restore
```

Keep build/test configurations identical when using `--no-build`. For a targeted change, run the
relevant test project with a build first, for example:

```sh
dotnet test tests/AgenticLab.AiService.Tests/AgenticLab.AiService.Tests.csproj
```

The agent and protocol tests use fake models, injected provider HTTP responses and local protocol
servers, without API credentials. `ModelProviderTests` covers provider selection, authentication,
streaming tools, Gemini signature retention, model overrides and cancellation. Live API compatibility
requires separately authorized checks with synthetic input; passing mocks does not establish it.
Do not start AppHost or supply production secrets to reproduce CI. Ordinary .NET builds do not run npm.

For Blazor UI changes, use the [browser smoke guide](tools/README.md) and its Development/backend prerequisites. Its
separate load-test profile can make billable model calls and is not a pull-request check.

The [CI workflow](.github/workflows/ci.yml) runs solution restore, Release build, and tests on every
pull request and main-branch push. Its stable check is `dotnet-build-test`.
[Container validation/publishing](.github/workflows/images.yml) remains separate and runs on main pushes.

## Maintainer Publication Checklist

These are release gates, not claims that an audit has already passed. Making source public is
separate from exposing running services. Keep audit reports private and outside the repository.

- [ ] Verify the actual visibility, default branch, local/remote refs, release assets, packages,
  workflow logs/artifacts, and any LFS or submodule content that will become public.
- [ ] Scan the publication tree and complete history with a local secret scanner such as
  [Gitleaks](https://github.com/gitleaks/gitleaks). Use a full remote mirror and all refs, not a
  shallow default-branch checkout; also cover local unpublished commits and intended working files.
  Record scanner version, scope, exclusions, and results without exposing secret values. Review
  binary/archived content, private prompts, screenshots, logs, and internal identifiers separately.
- [ ] Revoke/rotate any exposed credentials first. Obtain explicit approval for history rewriting
  or artifact deletion, coordinate other copies, and rescan. Ignore rules do not remove tracked
  files or history. Verify Git and Docker exclusions independently while preserving sample config.
- [ ] Confirm contributor/employer/customer publication rights, [LICENSE](LICENSE), [NOTICE](NOTICE),
  asset licenses/provenance, trademark use, and source/API attribution obligations. Include disabled
  modules: their assets may still be distributed. A scanner cannot provide this approval.
- [ ] Validate a clean-clone credential-free build/test, existing container checks, workflow
  syntax, issue forms, policy links, and rendered documentation. Repeat the secret scan on the final
  candidate. Record unverified surfaces; do not treat missing evidence as a passing gate.
- [ ] Obtain owner approval before changing repository visibility. Enable available secret scanning,
  push protection, dependency graph, and Dependabot alerts; verify their availability/settings again
  after publication. Public source does not authorize a public deployment.
- [ ] Immediately after making the repository public, enable
  [private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/working-with-repository-security-advisories/configuring-private-vulnerability-reporting-for-a-repository).
  Verify the signed-in external reporter form and maintainer security-alert notifications before
  announcing availability. Do not route vulnerabilities to public issues if the form is unavailable.
- [ ] Require PRs and the actual successful `dotnet-build-test` check on `main`; prevent force pushes
  and branch deletion. For solo maintenance, do not require independent or code-owner approval until
  another eligible reviewer is available. Do not require checks that skip via workflow path filters.
- [ ] Enable Issues, squash merging, and deletion of merged branches. Leave Wiki/Discussions off
  unless they will be maintained. Set an educational description and relevant .NET, Aspire, agent,
  Azure OpenAI, MCP, A2A, and Blazor topics. Verify public docs/forms and the first public CI.
- [ ] Decide GHCR package visibility separately; main pushes already publish images. Obtain separate
  approval before publishing/changing package access or creating an initial release such as `v0.1.0`
  from the verified commit. State the educational/local-only scope, support policy, prerequisites,
  and known limitations in release notes; verify source archives retain notices.

After the minimum launch, schedule Dependabot for NuGet/npm/Actions/Docker, dependency review,
and CodeQL for C# and JavaScript/TypeScript. Triage their baselines before making new checks required;
do not enable automatic dependency merging by default. Formatting gates, a separate Blazor smoke
workflow, sanitized screenshots/social preview, and CODEOWNERS can follow when maintainers can
support them.