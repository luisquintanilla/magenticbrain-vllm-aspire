# magenticbrain-vllm-aspire

Run [`microsoft/MagenticBrain`](https://huggingface.co/microsoft/MagenticBrain) — a 14B,
Qwen3-14B-based **tool-orchestration** model — **locally on a single 16 GB GPU** with
**vLLM (Docker)**, orchestrated by **Aspire**, behind the **.NET AI Chat Web App**
(`aichatweb`) template configured for **full RAG** (retrieval-augmented generation with
citations).

Everything runs on your machine: the LLM, the embedding model, document ingestion, and the
vector store. No cloud endpoints, no API keys.

> Staging experiment — unproven by default. See `~/dev/experiments/README.md`.

## Architecture

<p align="center">
  <img src="docs/architecture.svg" width="820"
       alt="An Aspire AppHost orchestrates four containers: a Blazor chat web app, a vLLM GPU container serving MagenticBrain over an OpenAI-compatible /v1 endpoint, an Ollama CPU container for nomic-embed-text embeddings, and a MarkItDown MCP container for PDF-to-Markdown conversion. The web app reads and writes a local SqliteVec vector store.">
</p>

<details>
<summary>Text version of the architecture</summary>

```
Aspire AppHost  (orchestrates everything, dashboard for logs/health/traces)
│
├── quantizer       one-shot job (uv on host, or pinned CUDA container) — gates vLLM
│                   GPU, writes models/MagenticBrain-bnb-nf4 (NF4), idempotent (manifest)
│
├── vllm            custom image: vllm/vllm-openai + bitsandbytes + non-thinking template
│                   GPU (--gpus all --ipc host), serves MagenticBrain 4-bit (NF4)
│                   OpenAI-compatible /v1  →  IChatClient (tool-calling, non-thinking)
│
├── ollama          CPU, model nomic-embed-text (768-dim)
│                   →  IEmbeddingGenerator   (keeps all 16 GB of VRAM for the 14B model)
│
├── markitdown      mcp/markitdown container, converts PDFs/Office docs → Markdown at ingest
│
└── aichatweb-app   Blazor Server chat UI (Microsoft.Extensions.AI)
                    chat       → vLLM      (model id "microsoft/MagenticBrain")
                    embeddings → Ollama    (nomic-embed-text)
                    vector DB  → SqliteVec (local file vector-store.db, 768-dim)
                    ingestion  → MarkItDown MCP + wwwroot/Data
```

</details>

Why two model servers? MagenticBrain is a chat/orchestration model, not an embedding model,
and vLLM serves one model per process. Embeddings therefore run separately on Ollama (CPU) so
they don't compete with the 14B model for VRAM.

## Prerequisites

- **NVIDIA GPU with ~16 GB VRAM** (developed on an RTX 3080 Laptop, 16 GB).
- **Docker** with the **NVIDIA Container Toolkit** (an `nvidia` Docker runtime). On WSL2 this
  works through the Windows NVIDIA driver.
  - Smoke test: `docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi`
- **.NET 10 SDK** (projects target `net10.0`).
- **[uv](https://docs.astral.sh/uv/)** — for the default quantization path (the Aspire-orchestrated
  quantizer runs as a `uv` project on the host). Not needed if you set `UseContainerQuant=true`
  (the reproducible container path) or pre-quantize manually. Install:
  `curl -LsSf https://astral.sh/uv/install.sh | sh`.
- **Aspire CLI ≥ 13.4** (optional — `scripts/run.sh` uses `dotnet run` and does not need
  the CLI). Install/update: `curl -sSL https://aspire.dev/install.sh | bash`.
- **~30 GB free disk**: the FP16 weights (~28 GB) download once to the Hugging Face cache, plus
  the ~10 GB vLLM base image and the ~9 GB pre-quantized checkpoint.
- MagenticBrain is public (MIT) and **not gated**; `HF_TOKEN` is optional (avoids rate limits).

## Setup

From the repository root:

### 1. Build the custom vLLM image

```bash
./scripts/build-image.sh          # tags magenticbrain-vllm:local
```

Layers `bitsandbytes` and a non-thinking chat template onto `vllm/vllm-openai`. First run pulls
the ~10 GB base image.

### 2. Quantize the model

Quantization is an **automatic, idempotent step in the stack** — an Aspire-orchestrated job runs
`quantizer/quantize.py`, writes a 4-bit NF4 checkpoint to `models/MagenticBrain-bnb-nf4/`, and
**gates vLLM** (`WaitForCompletion`) so the server only starts once the checkpoint exists. It runs
**once**: a matching checkpoint + reproducibility manifest make later starts exit in ~2 s.

Two execution modes (see [Quantization pipeline](#quantization-pipeline)):

- **Dev (default):** a polyglot `uv` job on the host (`AddPythonApp(...).WithUv()`). First run
  `uv sync`s torch/transformers and quantizes on the GPU (~40 s). Needs `uv`.
- **Reproducible:** set `UseContainerQuant=true` to run the same script inside the pinned vLLM CUDA
  image instead (no host Python toolchain).

The first run downloads the ~28 GB FP16 weights to `~/.cache/huggingface`. You can still pre-warm
manually with `./scripts/prequantize.sh`; the orchestrated job then detects the checkpoint and skips.

> Free the GPU before the first quantization (stop any running vLLM container) — it needs the full
> 16 GB.

### 3. Run the stack

```bash
./scripts/run.sh                  # dotnet run on the AppHost (http profile)
# or, with Aspire CLI >= 13.4:  ASPIRE_ALLOW_UNSECURED_TRANSPORT=true aspire run
```

Watch the console (or the Aspire dashboard) for URLs. When the `vllm` resource reports healthy
(model loaded), open the **aichatweb-app** URL and ask a question about the sample documents in
`MagenticBrainRag.Web/wwwroot/Data`, e.g.:

> *What percentage of waterborne bacteria does the emergency kit's water purification filter remove?*

The model calls its `LoadDocuments` tool (first turn ingests the PDF via MarkItDown → embeds
chunks with Ollama → writes to SqliteVec), then `Search`, then answers **grounded in the
retrieved text with a citation**. If the documents don't contain the answer, it says so instead
of hallucinating.

## How it works (key decisions)

<p align="center">
  <img src="docs/rag-flow.svg" width="900"
       alt="Sequence diagram of the RAG tool loop. The user asks the Blazor web app a question. MagenticBrain on vLLM first calls the LoadDocuments tool: MarkItDown converts the PDF to Markdown, Ollama embeds the chunks into 768-dimensional vectors, and the vectors are stored in SqliteVec. MagenticBrain then calls the Search tool: the query is embedded and the top-k similar chunks are retrieved from SqliteVec. Finally MagenticBrain returns a grounded answer with a citation, which the web app renders to the user.">
</p>

The answer is produced by a two-tool loop the model drives itself — `LoadDocuments` (lazy,
first-run ingestion) then `Search` (retrieval) — before it composes a grounded, cited reply.

- **4-bit to fit 16 GB.** A 14B model is ~28 GB in FP16. Served at bitsandbytes NF4 the weights
  are ~8–9 GB; with `--max-model-len 16384 --gpu-memory-utilization 0.90 --max-num-seqs 16` the
  whole thing (weights + KV cache) fits in ~14.5 GB.
- **Non-thinking by default.** MagenticBrain (a Qwen3 hybrid) emits `<think>…</think>` blocks by
  default; its model card calls for non-thinking inference. A patched chat template flips the
  default so standard OpenAI-compatible clients need no special per-request fields.
- **Model-card sampling** (temp 0.7, top_p 0.8, presence_penalty 1.0, no greedy decoding) is
  applied as client defaults in `MagenticBrainRag.Web/Program.cs`.
- **Tool-calling** uses vLLM's `--enable-auto-tool-choice --tool-call-parser hermes`, which the
  RAG loop (`LoadDocuments`, `Search`) depends on.

See [`docs/design-notes.md`](docs/design-notes.md) for the full rationale, VRAM tuning, and
gotchas (WSL2, dev certs, Ollama, lazy ingestion).

## Quantization pipeline

The 4-bit checkpoint is produced by a small, reproducible CLI in [`quantizer/`](quantizer/) — a
`uv` project (`quantize.py`, stdlib `argparse`, lazy ML imports) that the AppHost orchestrates as a
one-shot job gated before vLLM.

- **Configurable** via Aspire parameters (defaults in `MagenticBrainRag.AppHost/appsettings.json`
  under `Parameters`, overridable by user-secrets/env): `model-id`, `quant-method` (`nf4`/`fp4`),
  `quant-dtype`, `double-quant`, and optional `hf-token` (secret; only wired when set).
- **Reproducible.** Beside the checkpoint it writes a `manifest.json` — the resolved Hugging Face
  revision, quant settings, resolved `torch`/`transformers`/`bitsandbytes`/`vllm` versions, and
  per-file SHA-256s.
- **Idempotent.** If the checkpoint plus a manifest with a matching signature already exist, the job
  exits almost immediately (no GPU load); `--force` / `FORCE=1` rebuilds.
- **Hybrid execution.** `UseContainerQuant=false` (default) runs it via `uv` on the host;
  `true` runs the identical script inside the pinned vLLM CUDA image. Both write the same host
  checkpoint and both gate vLLM.

See [`quantizer/README.md`](quantizer/README.md) for the standalone CLI (flags, env vars, examples).

## Aspire vLLM integration (`AddVLLM`)

The AppHost serves vLLM through a self-contained, upstream-ready integration in
[`src/CommunityToolkit.Aspire.Hosting.VLLM/`](src/CommunityToolkit.Aspire.Hosting.VLLM/), mirroring
the shape of the Community Toolkit's Ollama integration:

```csharp
var vllm = builder.AddVLLM("vllm")
    .WithGPUSupport()          // NVIDIA (default) or VLLMGpuVendor.AMD (ROCm)
    .WithDataVolume()          // persist the Hugging Face cache
    .WithModel("Qwen/Qwen3-8B");
```

`AddVLLM` wires the `vllm/vllm-openai` image, an `http` endpoint on 8000, and a `/health` check
(healthy only once the model is loaded, so `WaitFor` dependents block correctly). This repo's
AppHost overrides the image with the local `magenticbrain-vllm:local` build and passes the
MagenticBrain serving args. Unit tests live in
[`tests/CommunityToolkit.Aspire.Hosting.VLLM.Tests/`](tests/CommunityToolkit.Aspire.Hosting.VLLM.Tests/)
and a minimal demo in [`examples/vllm/`](examples/vllm/). It's staged for a future
[CommunityToolkit/Aspire](https://github.com/CommunityToolkit/Aspire) contribution.

## Troubleshooting

- **`RuntimeError: UVA is not available` on WSL2.** WSL2 disables CUDA pinned memory; the AppHost
  sets `VLLM_WSL2_ENABLE_PIN_MEMORY=1` on the container to fix it.
- **Model load looks stuck for ~28 min.** vLLM is quantizing in-flight because no NF4 checkpoint was
  found. The orchestrated quantization job normally prevents this — check that the `quantizer`
  resource completed (the default dev path needs `uv`), or pre-warm with `./scripts/prequantize.sh`.
- **HTTPS / dev-cert errors, or the dashboard won't bind on WSL2.** Use `./scripts/run.sh` (http
  profile + `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`).
- **`aspire run` fails with a JSON-RPC/backchannel error.** Your Aspire CLI is older than the
  13.4 runtime. Update it, or just use `./scripts/run.sh`.
- **Embeddings look wrong / dimension mismatch.** Embeddings must be 768-dim
  (`IngestedChunk.VectorDimensions = 768`) to match `nomic-embed-text`. A separate host Ollama on
  `:11434` (without the model) can confuse manual testing — the app uses the Aspire-managed
  container via its injected connection string.

## Repository layout

```
docker/vllm-magenticbrain/   Dockerfile + non-thinking chat template (magenticbrain-vllm:local)
scripts/                     build-image.sh, prequantize.sh/.py (manual pre-warm), run.sh
quantizer/                   reproducible uv-packaged quantization CLI (quantize.py + manifest)
models/                      pre-quantized NF4 checkpoint (gitignored, created by the quantizer)
src/CommunityToolkit.Aspire.Hosting.VLLM/   upstream-ready AddVLLM integration
MagenticBrainRag.AppHost/    Aspire orchestration (AppHost.cs)
MagenticBrainRag.Web/        Blazor chat UI, RAG ingestion/search, client wiring (Program.cs)
MagenticBrainRag.ServiceDefaults/   shared Aspire service defaults
tests/                       AddVLLM integration unit + AppHost tests
examples/vllm/               minimal AddVLLM demo AppHost
docs/                        design-notes.md, aichatweb-template.md, upstream-vllm-integration.md
```

## License

MIT — see [`LICENSE`](LICENSE).
