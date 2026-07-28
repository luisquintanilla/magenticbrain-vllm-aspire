# Integration Proposal (draft): CommunityToolkit/Aspire

> **Filed 2026-07-28 as [CommunityToolkit/Aspire#1484](https://github.com/CommunityToolkit/Aspire/issues/1484).**
> Kept here as the source of the issue body.

Ready-to-file issue for <https://github.com/CommunityToolkit/Aspire/issues/new/choose>
using the **Integration Proposal** template
([`integration_proposal.yml`](https://github.com/CommunityToolkit/Aspire/blob/main/.github/ISSUE_TEMPLATE/integration_proposal.yml)).

- **Title:** `vLLM hosting and client integration`
- **Template fields (in order):** Aspire issue link · Overview · Usage example · Additional context · Help us help you
- **Label:** the template applies `integration request :mailbox_with_mail:` automatically; maintainers also tag `integration`.

> The body below is copy-paste ready. Leave the first field blank in the form (there is no prior
> dotnet/aspire discussion) or paste "_No response_".

---

### Aspire issue link

_No response_ (not previously discussed in dotnet/aspire)

### Overview

vLLM is a popular, OpenAI-compatible inference server for running open-weight models on your own
GPU. An Aspire app host has no first-class way to run one today. You drop down to a raw container
resource and hand-write the image, the port, the GPU runtime args, a readiness health check, and
then the OpenAI client wiring on the consuming side. Every app repeats that setup, and the first run
has a few sharp edges: the health check has to gate on the model actually loading, and the OpenAI
client needs the `/v1` path plus a non-empty API key even though vLLM ignores the key.

The Community Toolkit already covers this shape for Ollama. vLLM is the natural companion for
GPU-served models, higher throughput, and larger context windows, so I'd like to add it as a
matching pair of packages:

- **`CommunityToolkit.Aspire.Hosting.VLLM`** (hosting). `AddVLLM("vllm")` adds the
  `vllm/vllm-openai` container with an HTTP endpoint (container port 8000) and a `/health` check
  that reports healthy only once the model is loaded, so `WaitFor` dependents block until the server
  can actually answer. `WithGPUSupport()` adds `--gpus all` for NVIDIA or switches to the ROCm image
  with the right device mounts for AMD. `WithDataVolume()` persists the Hugging Face cache across
  restarts, `WithModel(...)` and `WithServedModelName(...)` choose and name the model, and
  `WithHuggingFaceToken(...)` wires a secret parameter for gated models.
- **`CommunityToolkit.Aspire.VLLM`** (client). `AddVLLMClient("vllm").AddChatClient()` reads the
  hosting resource's connection string and registers an `IChatClient` (through
  `Microsoft.Extensions.AI`) pointed at the server. It appends `/v1`, supplies the placeholder API
  key the OpenAI client requires, targets the served model name, and registers a matching `/health`
  check plus OpenTelemetry. Because the surface is `Microsoft.Extensions.AI`, the same app code keeps
  working against any OpenAI-compatible endpoint later.

The two mirror `CommunityToolkit.Aspire.Hosting.Ollama` and `CommunityToolkit.Aspire.OllamaSharp`:
add the server in the app host, reference it from a project, and consume it as an `IChatClient`.

### Usage example

Add the server in the app host and gate a project on it:

```csharp
// AppHost.cs
var builder = DistributedApplication.CreateBuilder(args);

var vllm = builder.AddVLLM("vllm")
    .WithGPUSupport()                // NVIDIA by default; VLLMGpuVendor.AMD for ROCm
    .WithDataVolume()                // persist the Hugging Face cache
    .WithModel("Qwen/Qwen3-8B");

builder.AddProject<Projects.Web>("web")
    .WithReference(vllm)             // injects the connection string
    .WaitFor(vllm);                  // blocks until the model is serving

builder.Build().Run();
```

Consume it as an `IChatClient` in the referencing project:

```csharp
// Web Program.cs
builder.AddVLLMClient("vllm", settings => settings.Model = "Qwen/Qwen3-8B")
    .AddChatClient()
    .UseFunctionInvocation();
```

### Additional context

Both packages already exist, build, and run. I built them for
[`magenticbrain-vllm-aspire`](https://github.com/luisquintanilla/magenticbrain-vllm-aspire), a local
RAG app, and shaped them from the start to match this repo's conventions so they can move here with
little change.

**Proven running.** This is not a sketch. The same two packages run
[`microsoft/MagenticBrain`](https://huggingface.co/microsoft/MagenticBrain), a 14B model quantized to
4-bit, on a single 16 GB laptop GPU (RTX 3080), under Aspire, behind the `aichatweb` RAG template,
with tool-calling. I verified the full client path against a live GPU server: the placeholder key,
the `/v1` suffix, and the served model name all work, and the app returns grounded answers with
citations.

**What's already there** (links to `main`):

- Hosting integration:
  [`src/CommunityToolkit.Aspire.Hosting.VLLM`](https://github.com/luisquintanilla/magenticbrain-vllm-aspire/tree/main/src/CommunityToolkit.Aspire.Hosting.VLLM)
  (namespace `Aspire.Hosting`, the `AddVLLM` surface, `VLLMResource : ContainerResource,
  IResourceWithConnectionString, IResourceWithEndpoints`).
- Client integration:
  [`src/CommunityToolkit.Aspire.VLLM`](https://github.com/luisquintanilla/magenticbrain-vllm-aspire/tree/main/src/CommunityToolkit.Aspire.VLLM)
  (extensions in `Microsoft.Extensions.Hosting`, per the client-integration convention).
- Tests: 9 hosting unit tests and 15 client unit tests, plus a Docker-gated app host test.
- Examples: an example app host and an example consumer under
  [`examples/vllm`](https://github.com/luisquintanilla/magenticbrain-vllm-aspire/tree/main/examples/vllm).
- A porting checklist that maps every file to this repo's layout and reconciles it against
  [`docs/create-integration.md`](https://github.com/CommunityToolkit/Aspire/blob/main/docs/create-integration.md):
  [`docs/upstream-vllm-integration.md`](https://github.com/luisquintanilla/magenticbrain-vllm-aspire/blob/main/docs/upstream-vllm-integration.md).

**Why a dedicated client and not just `AddOpenAIClient`?** vLLM is OpenAI-compatible, so a reviewer
may reasonably ask. The client adds three vLLM-specific things on top of the generic OpenAI client:
it pairs with the hosting resource over the connection string and appends `/v1` (the hosting
resource emits the base URL without it, so a raw OpenAI client 404s until you fix it up by hand); it
fills in vLLM-correct defaults (the placeholder API key and the served model name); and it adds
vLLM's `/health` readiness check. If you'd prefer, the same behavior could ship as a defaults layer
over `AddOpenAIClient` rather than a standalone package. Happy to go either way.

**Alternatives.** Today you write `AddContainer("vllm/vllm-openai", ...)` by hand and pair it with
`AddOpenAIClient`, reproducing the GPU args, the readiness health check, and the `/v1` and API-key
fix-ups in every app. That works, but it is the boilerplate an integration should own.

**Relationship to Ollama.** This complements the existing Ollama integration rather than replacing
it. Ollama is the easy CPU/GGUF path; vLLM is the GPU-first, high-throughput, OpenAI-native path for
larger models and longer context. Many apps will want vLLM for chat and Ollama for embeddings.

**A few choices for maintainers.** I'm flexible on all of these:

- Acronym casing: `VLLM` (matches the product) vs `Vllm` (matches .NET acronym casing), for both
  type names and `AddVLLM`/`AddVllm`.
- The `VLLMGpuVendor` enum name (Ollama uses an unprefixed `GpuVendor`; I prefixed it to avoid a
  clash if both integrations are referenced from the same app host).
- Client shape: `AddVLLMClient(name).AddChatClient()` (leaves room for `.AddEmbeddingGenerator()`,
  since vLLM can serve embedding models too) vs a single-call `AddVLLMChatClient(name)`.
- One combined proposal vs separate hosting and client issues, whichever you track better.

I'll sign the .NET Foundation CLA and can open the companion docs PR to `aspire.dev`. I'd love to
bring this in.

### Help us help you

Yes, I'd like to be assigned to work on this item
