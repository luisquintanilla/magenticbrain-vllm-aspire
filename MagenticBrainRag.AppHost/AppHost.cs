using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Resolve host paths relative to the repository root (the AppHost project's parent).
var repoRoot = Directory.GetParent(builder.AppHostDirectory)!.FullName;
var modelsDir = Path.Combine(repoRoot, "models");
var quantizerDir = Path.Combine(repoRoot, "quantizer");
var hfCacheDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "huggingface");

// The quantized checkpoint's directory name. The quantizer writes it under models/ on the
// host; vLLM reads it back from the read-only /models mount. A single constant keeps the
// producer (quantizer) and consumer (vLLM) in agreement.
const string checkpointName = "MagenticBrain-bnb-nf4";

// ---- Configurable pipeline parameters -------------------------------------------------
// Defaults live in code (safe fallbacks) and can be overridden via appsettings.json
// "Parameters", user-secrets, or environment. They surface in the Aspire dashboard.
string Config(string key, string fallback) => builder.Configuration[$"Parameters:{key}"] ?? fallback;

var modelId = builder.AddParameter("model-id", Config("model-id", "microsoft/MagenticBrain"));
var quantMethod = builder.AddParameter("quant-method", Config("quant-method", "nf4"));
var quantDtype = builder.AddParameter("quant-dtype", Config("quant-dtype", "bfloat16"));
var doubleQuant = builder.AddParameter("double-quant", Config("double-quant", "true"));

// Optional Hugging Face token for gated models. Only wired when provided, so the public
// microsoft/MagenticBrain path needs no secret.
var hfTokenValue = builder.Configuration["Parameters:hf-token"];
IResourceBuilder<ParameterResource>? hfToken =
    string.IsNullOrEmpty(hfTokenValue) ? null : builder.AddParameter("hf-token", hfTokenValue, secret: true);

// Execution mode for the quantization job (user-approved hybrid):
//   false (default) -> polyglot dev path: AddPythonApp + uv on the host (no Docker build).
//   true            -> reproducible path: the pinned CUDA vLLM image runs the same script.
var useContainerQuant = bool.TryParse(builder.Configuration["UseContainerQuant"], out var ucq) && ucq;

// ---- Quantization job (idempotent; gates vLLM via WaitForCompletion) -------------------
// Both paths run the same quantizer/quantize.py and write the 4-bit checkpoint to
// models/<checkpointName> on the host. The job is idempotent: if a matching checkpoint +
// manifest already exist it exits 0 immediately, so warm `aspire run`s stay fast. Gating
// vLLM on its completion also keeps the GPU single-tenant (the quantizer releases VRAM
// before the server starts).
IResourceBuilder<IResource> quantizer;
if (useContainerQuant)
{
    var q = builder.AddContainer("quantizer", "magenticbrain-vllm", "local")
        .WithContainerRuntimeArgs("--gpus", "all", "--ipc", "host")
        .WithEnvironment("VLLM_WSL2_ENABLE_PIN_MEMORY", "1")
        .WithEnvironment("MODEL_ID", modelId)
        .WithEnvironment("QUANT_METHOD", quantMethod)
        .WithEnvironment("QUANT_DTYPE", quantDtype)
        .WithEnvironment("DOUBLE_QUANT", doubleQuant)
        .WithEnvironment("OUTPUT_DIR", $"/out/{checkpointName}")
        .WithBindMount(hfCacheDir, "/root/.cache/huggingface")
        .WithBindMount(modelsDir, "/out")
        .WithBindMount(quantizerDir, "/quantizer", isReadOnly: true)
        .WithEntrypoint("python3")
        .WithArgs("/quantizer/quantize.py");
    if (hfToken is not null)
    {
        q.WithEnvironment("HF_TOKEN", hfToken);
    }
    quantizer = q;
}
else
{
    var q = builder.AddPythonApp("quantizer", quantizerDir, "quantize.py")
        .WithUv()
        .WithEnvironment("MODEL_ID", modelId)
        .WithEnvironment("QUANT_METHOD", quantMethod)
        .WithEnvironment("QUANT_DTYPE", quantDtype)
        .WithEnvironment("DOUBLE_QUANT", doubleQuant)
        .WithEnvironment("OUTPUT_DIR", Path.Combine(modelsDir, checkpointName));
    if (hfToken is not null)
    {
        q.WithEnvironment("HF_TOKEN", hfToken);
    }
    quantizer = q;
}

// vLLM serving microsoft/MagenticBrain (14B) 4-bit on the GPU, OpenAI-compatible, via the
// CommunityToolkit.Aspire.Hosting.VLLM integration (builder.AddVLLM). The custom image bakes
// bitsandbytes and a non-thinking chat template, so we override the integration's default
// image (and clear its registry so the local-only image isn't pulled from a registry).
var vllm = builder.AddVLLM("vllm")
    .WithImageRegistry(null!)
    .WithImage("magenticbrain-vllm", "local")
    .WithGPUSupport()
    .WithContainerRuntimeArgs("--ipc", "host")
    // WSL2 disables CUDA pinned memory by default; without this vLLM's v1 engine
    // fails to initialize with "RuntimeError: UVA is not available".
    .WithEnvironment("VLLM_WSL2_ENABLE_PIN_MEMORY", "1")
    .WithBindMount(hfCacheDir, "/root/.cache/huggingface")
    .WithBindMount(modelsDir, "/models", isReadOnly: true)
    // WithModel is the first positional arg to the vLLM entrypoint, so it must precede WithArgs.
    .WithModel($"/models/{checkpointName}")
    .WithServedModelName("microsoft/MagenticBrain")
    .WithArgs(
        "--quantization", "bitsandbytes",
        "--dtype", "bfloat16",
        "--chat-template", "/config/chat_template_no_think.jinja",
        "--enable-auto-tool-choice",
        "--tool-call-parser", "hermes",
        "--max-model-len", "16384",
        "--gpu-memory-utilization", "0.90",
        "--max-num-seqs", "16")
    // Don't start serving until the (idempotent) quantization job has produced the checkpoint.
    .WaitForCompletion(quantizer);

// Embeddings run on CPU via Ollama (nomic-embed-text, 768 dims). Keeping embeddings
// off the GPU leaves all 16 GB of VRAM for MagenticBrain.
var ollama = builder.AddOllama("ollama")
    .WithDataVolume();
var embeddings = ollama.AddModel("embedding", "nomic-embed-text");

// MarkItDown MCP server converts PDFs/Office docs to Markdown during ingestion.
var markitdown = builder.AddContainer("markitdown", "mcp/markitdown")
    .WithArgs("--http", "--host", "0.0.0.0", "--port", "3001")
    .WithHttpEndpoint(targetPort: 3001, name: "http");

builder.AddProject<Projects.MagenticBrainRag_Web>("aichatweb-app")
    .WithReference(vllm)
    .WithReference(embeddings)
    .WithEnvironment("MARKITDOWN_MCP_URL", markitdown.GetEndpoint("http"))
    .WaitFor(vllm)
    .WaitFor(embeddings)
    .WaitFor(markitdown);

builder.Build().Run();
