var builder = DistributedApplication.CreateBuilder(args);

// A minimal vLLM resource serving a small public model on the GPU. vLLM exposes an
// OpenAI-compatible API at "{vllm http endpoint}/v1". Dependents that WaitFor this
// resource are gated on its /health check, which reports healthy only once the model
// has finished loading.
builder.AddVLLM("vllm")
    .WithGPUSupport()
    .WithDataVolume()
    .WithModel("Qwen/Qwen3-0.6B");

builder.Build().Run();
