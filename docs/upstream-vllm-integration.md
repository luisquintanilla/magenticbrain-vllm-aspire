# Upstreaming the vLLM integration to CommunityToolkit/Aspire (staged notes)

**Status: staged — do NOT submit yet.** This maps the in-repo `CommunityToolkit.Aspire.Hosting.VLLM`
(hosting) and `CommunityToolkit.Aspire.VLLM` (client) integrations to the
[CommunityToolkit/Aspire](https://github.com/CommunityToolkit/Aspire) repository
so a PR can be opened later. It follows the toolkit's
[`docs/create-integration.md`](https://github.com/CommunityToolkit/Aspire/blob/main/docs/create-integration.md)
checklist (verified against the repo's current `main`).

Nothing here changes the local repo's behavior — the integration already builds and its unit tests
pass here. These are the deltas required to land it upstream.

## Prerequisites (before any PR)

1. **Open a feature-request issue** in CommunityToolkit/Aspire proposing the vLLM hosting + client
   integrations and get a maintainer 👍 (their contribution flow expects this first).
2. **Sign the .NET Foundation CLA** (the PR bot blocks merge until signed).
3. Read `CONTRIBUTING.md` + `docs/setup.md` for the (somewhat involved) polyglot dev-environment setup.

## File mapping (in-repo → upstream)

The layout already matches the toolkit's `src/` · `tests/` · `examples/` convention, so files move
across essentially unchanged (namespaces are already correct: extensions in `Aspire.Hosting`,
resources in `Aspire.Hosting.ApplicationModel`).

| In this repo | Upstream path | Notes |
| --- | --- | --- |
| `src/CommunityToolkit.Aspire.Hosting.VLLM/VLLMContainerImageTags.cs` | same | mirrors `OllamaContainerImageTags.cs` |
| `src/CommunityToolkit.Aspire.Hosting.VLLM/VLLMResource.cs` | same | `ContainerResource, IResourceWithConnectionString, IResourceWithEndpoints` |
| `src/CommunityToolkit.Aspire.Hosting.VLLM/VLLMGpuVendor.cs` | same | cf. Ollama's `GpuVendor.cs` (see naming note below) |
| `src/CommunityToolkit.Aspire.Hosting.VLLM/VLLMResourceBuilderExtensions.cs` | same | the public `AddVLLM` surface |
| `src/CommunityToolkit.Aspire.Hosting.VLLM/README.md` | same | becomes the NuGet package readme |
| `src/CommunityToolkit.Aspire.Hosting.VLLM/CommunityToolkit.Aspire.Hosting.VLLM.csproj` | same | trim to rely on `Directory.Build.props` (below) |
| `tests/CommunityToolkit.Aspire.Hosting.VLLM.Tests/AddVLLMTests.cs` | same | unit tests, `DistributedApplication.CreateBuilder` — ✅ already matches toolkit style |
| `tests/CommunityToolkit.Aspire.Hosting.VLLM.Tests/AppHostTests.cs` | same | **rework** to the fixture pattern (below) |
| `tests/CommunityToolkit.Aspire.Hosting.VLLM.Tests/RequiresDockerFactAttribute.cs` | **delete** | toolkit ships its own `[RequiresDocker]` |
| `tests/CommunityToolkit.Aspire.Hosting.VLLM.Tests/CommunityToolkit.Aspire.Hosting.VLLM.Tests.csproj` | same | trim to rely on `Directory.Build.props` test deps |
| `examples/vllm/CommunityToolkit.Aspire.Hosting.VLLM.AppHost/*` | same | example AppHost is also the integration-test host |

## Required changes for upstream

### 1. `.csproj` cleanup (both src + tests)
The toolkit's root `Directory.Build.props` already adds the `Aspire.Hosting` package reference,
standard packaging MSBuild properties, and (for `.Tests` class libraries) the xunit/test deps. So
upstream:
- **src csproj:** remove the explicit `Aspire.Hosting` `<PackageReference>` and any boilerplate the
  props supply. **Keep only** the two manual NuGet metadata bits the guide calls out:
  `Description` (e.g. *"An Aspire hosting integration for the vLLM inference server."*) and
  `AdditionalPackageTags` (e.g. `vllm ai llm hosting` — **include `hosting`** so VS tooling
  categorizes it). Keep `InternalsVisibleTo` for the test project.
- **tests csproj:** remove explicit test-framework `<PackageReference>`s that `Directory.Build.props`
  provides; keep the `ProjectReference` to the integration and (for integration tests) to the
  example AppHost.

### 2. Container image tag policy
The guide says pin a **`major.minor`** tag (never `latest`), or a `sha256` digest if the image is
unversioned. We currently pin **`v0.26.0`** (`VLLMContainerImageTags.Tag`). `vllm/vllm-openai`
publishes only immutable `vX.Y.Z` tags (no floating `vX.Y`), so either:
- keep the exact patch tag `v0.26.0` (immutable — arguably better than a floating tag), **or**
- pin the `sha256` digest.

Flag this to reviewers so they can confirm which they prefer; expect to bump the tag to the latest
stable at PR time.

### 3. Integration tests → toolkit fixture pattern
Replace the current `AppHostTests` (which uses `DistributedApplicationTestingBuilder.CreateAsync<…>`
+ our local `RequiresDockerFactAttribute`) with the toolkit's convention:
- Class inherits `IClassFixture<AspireIntegrationTestFixture<Projects.CommunityToolkit_Aspire_Hosting_VLLM_AppHost>>`.
- Use `fixture.CreateHttpClient(...)` / `fixture.App` for assertions; delete `RequiresDockerFactAttribute.cs`
  and annotate Docker-requiring tests with the toolkit's `[RequiresDocker]`.
- vLLM does expose `/health`, so `WaitFor` works; if a log-gate is ever needed, `WaitForTextAsync`
  is available (suppress `CTASPIRE001`).
- Because a real vLLM pull + GPU load is heavy, keep the container test minimal (resource starts +
  `/health` reachable); the substantive coverage stays in the fast unit tests.

### 4. Register in the solution
Add the three projects (src, tests, example AppHost) to the toolkit's `CommunityToolkit.Aspire.slnx`.

### 5. CI test matrix
Add `CommunityToolkit.Aspire.Hosting.VLLM.Tests` to `.github/workflows/tests.yml`. Don't hand-edit —
run `./eng/testing/generate-test-list-for-workflow.sh` and paste its output into the test list (as the
guide instructs). Docker-marked tests are auto-filtered on Windows runners.

### 6. `CODEOWNERS` (repo root, not `.github/`)
Append a block mirroring the existing per-integration entries:
```
# CommunityToolkit.Aspire.Hosting.VLLM
/examples/vllm/ @luisquintanilla
/src/CommunityToolkit.Aspire.Hosting.VLLM/ @luisquintanilla
/tests/CommunityToolkit.Aspire.Hosting.VLLM.Tests/ @luisquintanilla
```

### 7. Root `README.md` integrations table
Add a row for the new package (name → NuGet, short description) in the integrations table, keeping the
alphabetical/section ordering used there.

### 8. Docs PR (separate repo)
Full docs live in [`microsoft/aspire.dev`](https://github.com/microsoft/aspire.dev), **not** this repo.
Open a second PR there; that repo has an agent that scaffolds a docs page from the package `README.md`,
so keep the README a good high-level overview.

### 9. Public API baseline (verify)
Check whether sibling integrations track a public-API baseline (`PublicAPI.Shipped.txt` /
`PublicAPI.Unshipped.txt` via `Microsoft.CodeAnalysis.PublicApiAnalyzers`). If the props enable it,
add the baseline files for the new public surface (`AddVLLM`, `WithGPUSupport`, `WithDataVolume`,
`WithHuggingFaceToken`, `WithModel`, `WithServedModelName`, `VLLMGpuVendor`).

## Naming / API notes for reviewers
- **`VLLMGpuVendor`** — Ollama names its enum `GpuVendor` (no prefix) in `Aspire.Hosting`. Prefixing
  avoids a name clash if an app references both integrations; reviewers may prefer the unprefixed
  name or a shared type. Easy to rename.
- **Casing** — we use `VLLM` (matches the product's own capitalization). Confirm the maintainers'
  acronym-casing preference (`VLLM` vs `Vllm`) for type names and the `AddVLLM`/`AddVllm` method.
- **Resource shape** — `VLLMResource : ContainerResource` (+ `IResourceWith*`). Ollama additionally
  exposes an `IOllamaResource` interface; add an `IVLLMResource` only if reviewers want parity.
- The public surface (endpoint 8000, `/health` health check, connection string
  `Endpoint=scheme://host:port`, `--gpus all` / `-rocm` for the GPU vendors) is covered by
  `AddVLLMTests.cs` — reuse those as the regression guard.

## Client integration (`CommunityToolkit.Aspire.VLLM`)

A thin, OpenAI-compatible **client** integration that pairs with the hosting resource above.
Extension methods live in namespace **`Microsoft.Extensions.Hosting`** (per the guide's
client-integration convention — note the package name has **no `.Hosting`** segment).

### File mapping (in-repo → upstream)

| In this repo | Upstream path | Notes |
| --- | --- | --- |
| `src/CommunityToolkit.Aspire.VLLM/AspireVLLMExtensions.cs` | same | `AddVLLMClient` / `AddKeyedVLLMClient` |
| `src/CommunityToolkit.Aspire.VLLM/AspireVLLMChatClientExtensions.cs` | same | `AddChatClient` / `AddKeyedChatClient` |
| `src/CommunityToolkit.Aspire.VLLM/AspireVLLMClientBuilder.cs` | same | builder returned by `AddVLLMClient` |
| `src/CommunityToolkit.Aspire.VLLM/VLLMClientSettings.cs` | same | bound from `Aspire:VLLM:<name>` |
| `src/CommunityToolkit.Aspire.VLLM/VLLMHealthCheck.cs` | same | `GET {endpoint}/health` |
| `src/CommunityToolkit.Aspire.VLLM/README.md` | same | NuGet package readme |
| `tests/CommunityToolkit.Aspire.VLLM.Tests/*` | same | unit tests (no live server) via `HostApplicationBuilder` |
| `examples/vllm/CommunityToolkit.Aspire.VLLM.ConsumerApp/*` | same | minimal consumer exercising `AddVLLMClient` |

### Required changes for upstream
- **`.csproj` cleanup:** the toolkit's `Directory.Build.props` supplies packaging metadata. Keep the
  `Microsoft.Extensions.AI` + `Microsoft.Extensions.AI.OpenAI` refs (the OpenAI SDK is the wrapped
  client), the health-check / config-binder / hosting-abstractions refs, and
  `OpenTelemetry.Extensions.Hosting` (source registration) — unless a sibling client integration
  already centralizes some of these. Keep `Description` + `AdditionalPackageTags` (**include `client`**)
  and `InternalsVisibleTo` for the test project.
- **CODEOWNERS / root-README row / `tests.yml`:** add the same three deltas as the hosting package, for
  the client `src`, `tests`, and example consumer paths (the `tests.yml` list is regenerated, not
  hand-edited).
- **Public API baseline:** if enabled, add `AddVLLMClient`, `AddKeyedVLLMClient`, `AddChatClient`,
  `AddKeyedChatClient`, `AspireVLLMClientBuilder`, and `VLLMClientSettings` to the baseline files.
- **Solution:** register the client `src`, `tests`, and consumer projects in `CommunityToolkit.Aspire.slnx`.

### Novelty vs. `Aspire.OpenAI`'s `AddOpenAIClient` (reviewer justification)
vLLM is OpenAI-compatible, so a reviewer may reasonably ask "why not just use `AddOpenAIClient`?" What
the vLLM client adds on top of the generic OpenAI client:
- **Connection-string pairing** with the vLLM *hosting* resource: `WithReference(vllm)` → the client
  resolves `Endpoint=scheme://host:port` and appends `/v1` automatically (the hosting resource emits
  the base URL **without** `/v1`, so a raw `AddOpenAIClient` would 404 until manually fixed up).
- **vLLM-correct defaults:** a **placeholder API key** (vLLM ignores it, but the OpenAI SDK rejects an
  empty credential — a common first-run trap) and the **served model name** (`--served-model-name`).
- **A vLLM `/health` health check** (vLLM's readiness endpoint), which the generic OpenAI client lacks.

If maintainers prefer, this could instead ship as a **defaults layer over `AddOpenAIClient`** rather
than a standalone package — flag for discussion.

### Naming / API notes for reviewers
- **Shape:** `AddVLLMClient(name).AddChatClient()` mirrors `AddOllamaApiClient(name).AddChatClient()`
  and leaves room for `.AddEmbeddingGenerator()` (vLLM can serve embedding models too). A single-call
  `AddVLLMChatClient(name)` is a viable alternative — reviewer's choice.
- **Casing:** `VLLM` throughout (matches the hosting package); confirm `VLLM` vs `Vllm`.
- **Thin by design:** vLLM-specific per-request knobs (thinking toggle, guided/structured-output
  defaults) are intentionally **out of scope** — they are handled server-side (chat template, tool-call
  parser) or via generic `Microsoft.Extensions.AI` primitives. Cite
  [`iwaitu/vllmchatclient`](https://github.com/iwaitu/vllmchatclient) (MPL-2.0) in docs as the richer
  alternative; do **not** vendor it.

## Not part of the upstream PR
The MagenticBrain-specific bits stay in **this** repo and must not leak into the toolkit PR: the local
`magenticbrain-vllm:local` image override, the non-thinking chat template, bitsandbytes/NF4 serving
args, the quantizer pipeline, and the WSL2 pin-memory env var. Upstream ships only the generic
`AddVLLM` (hosting) and `AddVLLMClient` (client) building blocks.
