# Design notes

Rationale and hard-won details behind `magenticbrain-vllm-aspire`. The README covers setup; this
file explains *why* the pieces are shaped the way they are and records the traps encountered on
WSL2 + a 16 GB laptop GPU.

## Serving a 14B model on 16 GB VRAM

`microsoft/MagenticBrain` is Qwen3-14B based. In FP16 the weights alone are ~28 GB, so full
precision is impossible on a 16 GB GPU. We serve it at **4-bit NF4 via bitsandbytes**:

- Weights drop to ~8–9 GB.
- Measured footprint with `--max-model-len 16384 --gpu-memory-utilization 0.90 --max-num-seqs 16`:
  ~14.5 GB total (weights + ~2.9 GB KV cache), leaving headroom on a 16 GB card.
- Context is capped at 16384 (the model card serves 32768) purely to bound KV-cache VRAM. Raise
  it if you have more VRAM.
- **AWQ/GPTQ 4-bit** would be faster than bitsandbytes but needs a pre-made quantized checkpoint;
  left as a future optimization.

The official `vllm/vllm-openai` image does **not** ship bitsandbytes, hence the custom image
(`docker/vllm-magenticbrain/Dockerfile`) that just `pip install`s it and bakes the chat template.

## Pre-quantization: 28 minutes → 10 seconds

vLLM can quantize to bitsandbytes **in-flight**, but that path is CPU-bound and takes **~28 min on
every startup** — unacceptable for an orchestrated dev loop. Instead the checkpoint is produced
**once, on the GPU** (transformers `BitsAndBytesConfig`), and vLLM loads it in **~10 s**.

That quantization is now a **reproducible, configurable, composable pipeline** rather than a one-off
script. The core is `quantizer/quantize.py` — a `uv` project using stdlib `argparse` with lazy ML
imports (so `--help` and up-to-date checks run without importing torch):

- `BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4", bnb_4bit_use_double_quant=True,
  bnb_4bit_compute_dtype=bfloat16)` + `device_map={"": 0}`, then `save_pretrained` →
  `models/MagenticBrain-bnb-nf4/` whose `config.json` carries
  `quantization_config.quant_method = "bitsandbytes"`. GPU quantization ~40 s.
- **Configurable** by env vars / CLI flags: `MODEL_ID`, `QUANT_METHOD`, `QUANT_DTYPE`, `DOUBLE_QUANT`,
  `OUTPUT_DIR`, `FORCE`, and `HF_TOKEN`.
- **Reproducible**: writes a `manifest.json` beside the checkpoint — resolved HF revision, quant
  params, resolved `torch`/`transformers`/`bitsandbytes`/`vllm` versions, per-file SHA-256s.
- **Idempotent**: if a manifest with a matching signature plus `config.json` and a `*.safetensors`
  file exist, it exits 0 without importing torch; `--force` (or `FORCE=1`) rebuilds.

The AppHost runs it as a **one-shot job gated before vLLM** via `WaitForCompletion(quantizer)`, which
also keeps the GPU single-tenant (the quantizer releases VRAM before the server starts). Two
execution modes, selected by the `UseContainerQuant` config flag (read synchronously at build time
so the resource graph is known):

- **Dev (default, `false`)** — polyglot `AddPythonApp("quantizer", …, "quantize.py").WithUv()`; `uv`
  runs the script on the host. No Docker build; the first run `uv sync`s torch.
- **Reproducible (`true`)** — `AddContainer("quantizer", "magenticbrain-vllm", "local")` with
  `--gpus all --ipc host`, bind-mounting the quantizer, `models/`, and the HF cache, entrypoint
  `python3`. The pinned CUDA image pins the whole toolchain.

Both branches pass the same config through `WithEnvironment` and land the same host checkpoint; vLLM
always serves `/models/MagenticBrain-bnb-nf4` (models mounted read-only at `/models`). The retired
"auto-detect the prequant dir" branch is gone — the idempotent job always runs and fast-skips.
`scripts/prequantize.{sh,py}` remain for a manual, Aspire-free pre-warm; the orchestrated job detects
their output and skips.

vLLM invocation note: the base image `ENTRYPOINT` is `["vllm", "serve"]`, so the **model id/path is
the first positional argument**, not `--model` (`WithModel(...)` must precede `WithArgs(...)`).

## vLLM as an Aspire integration (`AddVLLM`)

vLLM is wired through a small, self-contained integration, `CommunityToolkit.Aspire.Hosting.VLLM`
(under `src/`), rather than a raw `AddContainer`. It mirrors the Community Toolkit's Ollama
integration: `AddVLLM(name, port?)` registers the `vllm/vllm-openai` image (pinned `v0.26.0`), an
`http` endpoint on 8000, and a `/health` check that only reports healthy once the model is loaded;
fluent helpers add GPU support (`WithGPUSupport(VLLMGpuVendor.Nvidia|AMD)` → `--gpus all` or the
`-rocm` image tag), `WithDataVolume()` (HF cache), `WithHuggingFaceToken`, `WithModel`, and
`WithServedModelName`.

Because MagenticBrain needs bitsandbytes (absent from the stock image) and a non-thinking template,
the AppHost overrides the integration's image with the local `magenticbrain-vllm:local` build via
`.WithImageRegistry(null!).WithImage("magenticbrain-vllm", "local")` — the `null!` clears the
registry so the local-only image isn't pulled. All prior serving args are preserved through
`WithModel` / `WithServedModelName` / `WithArgs`, byte-identical to the earlier `AddContainer` config
(verified against the published manifest).

The integration is authored to be **upstream-portable** (file/namespace layout matches
CommunityToolkit/Aspire) with unit tests (`tests/`) and a minimal example (`examples/vllm/`); it is
staged for a possible contribution but not yet submitted.

## vLLM client integration (`AddVLLMClient`)

The Web app consumes the server through a matching thin **client** integration,
`CommunityToolkit.Aspire.VLLM` (also under `src/`), rather than a hand-rolled `OpenAIClient`. The
AppHost hands the endpoint over with `.WithReference(vllm)` (the hosting resource is
`IResourceWithConnectionString`, emitting `Endpoint=scheme://host:port`), and
`builder.AddVLLMClient("vllm").AddChatClient()` resolves it: it appends `/v1`, injects a placeholder
API key (vLLM ignores it, but the OpenAI client rejects an empty credential), targets the served
model name, and registers a `/health` check plus OpenTelemetry. Extension methods live in the
`Microsoft.Extensions.Hosting` namespace, per the toolkit's client-integration convention.

It is deliberately **thin**: everything a richer vLLM client might add is handled elsewhere in this
app — non-thinking via the chat template (below), tool-calling via vLLM's server flags consumed by
generic `.UseFunctionInvocation()`, and structured output is unused — so the integration only wraps
the OpenAI SDK with vLLM-correct defaults. Unit tests are in `tests/CommunityToolkit.Aspire.VLLM.Tests/`
and a minimal consumer in `examples/vllm/CommunityToolkit.Aspire.VLLM.ConsumerApp/`; like the hosting
package it is staged for a possible upstream contribution.

## Non-thinking by default (chat template patch)

MagenticBrain is a Qwen3 *hybrid* reasoning model: by default it emits `<think>…</think>` blocks.
The model card calls for **non-thinking** inference. vLLM has no server flag for this — it's driven
by the chat template.

The stock Qwen3 template contains:

```jinja
{%- if enable_thinking is defined and enable_thinking is false %}
```

which only suppresses thinking when a request *explicitly* sets `enable_thinking=false`. Standard
OpenAI-compatible clients (including Microsoft.Extensions.AI) don't send that field, so they'd get
thinking output. `docker/vllm-magenticbrain/chat_template_no_think.jinja` flips the condition to:

```jinja
{%- if enable_thinking is not defined or enable_thinking is false %}
```

so **non-thinking is the default** and a client would have to opt *in* to thinking. Passed via
`--chat-template /config/chat_template_no_think.jinja`. This keeps `Program.cs` free of per-request
hacks.

## Two endpoints, one chat app

The `aichatweb` template assumes a single OpenAI-compatible endpoint for both chat and embeddings.
Here they're split in `MagenticBrainRag.Web/Program.cs`:

- **Chat** → `builder.AddVLLMClient("vllm").AddChatClient()` (the client integration above): resolves
  the vLLM endpoint from the injected `vllm` connection string, appends `/v1`, uses a placeholder
  credential (vLLM ignores it but the client requires non-empty), targets model id
  `microsoft/MagenticBrain`, then `.ConfigureOptions` (model-card sampling defaults) and
  `.UseFunctionInvocation()`.
- **Embeddings** → `AddOllamaApiClient("embedding").AddEmbeddingGenerator()` (Aspire Community
  Toolkit / OllamaSharp).

`IngestedChunk.VectorDimensions` must be **768** to match `nomic-embed-text` (the template default
is 1536 for OpenAI embeddings).

## RAG ingestion is lazy

Ingestion is **not** run at startup. `SemanticSearch.LoadDocumentsAsync()` — exposed to the model
as the `LoadDocuments` tool — triggers `DataIngestor.IngestDataAsync`, so `vector-store.db` is only
populated once a chat turn drives that tool. The pipeline: MarkItDown MCP converts PDFs → Markdown,
a semantic chunker splits text, Ollama embeds each chunk, SqliteVec stores them. Subsequent turns
reuse the populated store.

End-to-end this was verified with the sample survival-kit PDF: the model called `LoadDocuments`
then `Search`, and answered *"removes 99.9999% of waterborne bacteria"* with a citation to
`Example_Emergency_Survival_Kit.pdf` — and correctly answered *"no relevant information"* for a
question the documents don't cover (water storage quantities), i.e. no hallucination.

## Environment gotchas (WSL2 + laptop GPU)

- **WSL2 UVA / pinned memory.** vLLM's v1 engine fails to initialize with
  `RuntimeError: UVA is not available` unless `VLLM_WSL2_ENABLE_PIN_MEMORY=1` is set. The AppHost
  sets it on the container.
- **Dev certificate / HTTPS.** The ASP.NET dev-cert HTTPS bind is unreliable on WSL2. Run with the
  **http launch profile** and `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` (what `scripts/run.sh` does).
  Note `DOTNET_LAUNCH_PROFILE` is not honored by `aspire run`; select the profile with
  `dotnet run --launch-profile http`.
- **Aspire version alignment.** `CommunityToolkit.Aspire.*` 13.4.x transitively pulls
  `Aspire.Hosting` 13.4.x, and DCP requires the AppHost SDK/package to be **≥ 13.4** too — mixing
  13.0 and 13.4 breaks orchestration. All Aspire packages here are pinned to 13.4.0. The **Aspire
  CLI** must also be ≥ 13.4 for `aspire run`; otherwise its JSON-RPC backchannel to the newer
  runtime fails. `scripts/run.sh` sidesteps this by using `dotnet run` directly.
- **Two Ollamas.** A host Ollama may be listening on `127.0.0.1:11434` **without** the embedding
  model; the Aspire-managed Ollama container maps to a random host port and *does* have the model.
  The app always uses the Aspire container via its injected `"embedding"` connection string — only
  manual `curl` testing is affected. Ollama ≥ 0.13 uses `POST /api/embed` (`input`), not the older
  `/api/embeddings` (`prompt`).

## Versions observed during development

vLLM 0.25.1, bitsandbytes 0.49.2, Ollama 0.13.0, .NET SDK 10.0.300, Aspire runtime 13.4.0 / CLI
13.4.6. Model: `microsoft/MagenticBrain` (14B, Qwen3-14B based, MIT, non-gated).
