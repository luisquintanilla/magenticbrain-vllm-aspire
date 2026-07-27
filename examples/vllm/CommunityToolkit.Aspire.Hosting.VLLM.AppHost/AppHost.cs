var builder = DistributedApplication.CreateBuilder(args);

// A minimal vLLM resource serving a small public model on the GPU. vLLM exposes an
// OpenAI-compatible API at "{vllm http endpoint}/v1". Dependents that WaitFor this
// resource are gated on its /health check, which reports healthy only once the model
// has finished loading.
var vllm = builder.AddVLLM("vllm")
    .WithGPUSupport()
    .WithDataVolume()
    .WithModel("Qwen/Qwen3-0.6B");

// A consumer that uses the client integration (AddVLLMClient). WithReference injects the
// vLLM endpoint as the "vllm" connection string; WaitFor gates startup on /health.
builder.AddProject<Projects.CommunityToolkit_Aspire_VLLM_ConsumerApp>("consumer")
    .WithReference(vllm)
    .WaitFor(vllm);

builder.Build().Run();
