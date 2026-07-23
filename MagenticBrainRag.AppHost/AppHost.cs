var builder = DistributedApplication.CreateBuilder(args);

// Resolve host paths relative to the repository root (the AppHost project's parent).
var repoRoot = Directory.GetParent(builder.AppHostDirectory)!.FullName;
var modelsDir = Path.Combine(repoRoot, "models");
var prequantDir = Path.Combine(modelsDir, "MagenticBrain-bnb-nf4");
var hfCacheDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "huggingface");

// Prefer the fast pre-quantized NF4 checkpoint produced by scripts/prequantize.sh
// (~10s load). If it is absent, fall back to in-flight bitsandbytes quantization
// straight from the Hugging Face cache (correct, but ~28 min on first start).
var usePrequant = Directory.Exists(prequantDir);
var modelArg = usePrequant ? "/models/MagenticBrain-bnb-nf4" : "microsoft/MagenticBrain";

// vLLM serving microsoft/MagenticBrain (14B) 4-bit on the GPU, OpenAI-compatible.
// The custom image bakes bitsandbytes and a non-thinking chat template.
var vllm = builder.AddContainer("vllm", "magenticbrain-vllm", "local")
    .WithContainerRuntimeArgs("--gpus", "all", "--ipc", "host")
    // WSL2 disables CUDA pinned memory by default; without this vLLM's v1 engine
    // fails to initialize with "RuntimeError: UVA is not available".
    .WithEnvironment("VLLM_WSL2_ENABLE_PIN_MEMORY", "1")
    .WithBindMount(hfCacheDir, "/root/.cache/huggingface")
    .WithBindMount(modelsDir, "/models", isReadOnly: true)
    .WithArgs(
        modelArg,
        "--served-model-name", "microsoft/MagenticBrain",
        "--quantization", "bitsandbytes",
        "--dtype", "bfloat16",
        "--chat-template", "/config/chat_template_no_think.jinja",
        "--enable-auto-tool-choice",
        "--tool-call-parser", "hermes",
        "--max-model-len", "16384",
        "--gpu-memory-utilization", "0.90",
        "--max-num-seqs", "16")
    .WithHttpEndpoint(targetPort: 8000, name: "http")
    // vLLM's /health returns 200 only once the model is loaded and serving, so gate
    // dependents on it. Model load can take a couple of minutes even when pre-quantized.
    .WithHttpHealthCheck("/health");

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
    .WithEnvironment("VLLM_ENDPOINT", vllm.GetEndpoint("http"))
    .WithReference(embeddings)
    .WithEnvironment("MARKITDOWN_MCP_URL", markitdown.GetEndpoint("http"))
    .WaitFor(vllm)
    .WaitFor(embeddings)
    .WaitFor(markitdown);

builder.Build().Run();
