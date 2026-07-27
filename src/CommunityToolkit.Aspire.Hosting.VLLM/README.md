# CommunityToolkit.Aspire.Hosting.VLLM library

Provides extension methods and resource definitions for an Aspire AppHost to run
[vLLM](https://github.com/vllm-project/vllm) — a high-throughput, OpenAI-compatible LLM
inference server — in a container, with GPU support and Hugging Face model serving.

## Getting Started

### Install the package

```sh
dotnet add package CommunityToolkit.Aspire.Hosting.VLLM
```

### Example usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var vllm = builder.AddVLLM("vllm")
    .WithGPUSupport()          // NVIDIA by default; pass VLLMGpuVendor.AMD for ROCm
    .WithDataVolume()          // persist the Hugging Face cache
    .WithModel("Qwen/Qwen3-8B");

builder.AddProject<Projects.MyApi>("api")
    .WithReference(vllm)
    .WaitFor(vllm);            // gated on vLLM's /health (model loaded)

builder.Build().Run();
```

vLLM exposes an OpenAI-compatible API — point your OpenAI client at `{endpoint}/v1`. The
resource's health check uses `/health`, which returns `200` only once the model is loaded, so
`WaitFor` dependents don't start until the server is actually ready.

## Additional Information

- https://docs.vllm.ai
- https://learn.microsoft.com/dotnet/aspire/community-toolkit/

## Feedback & contributing

https://github.com/CommunityToolkit/Aspire
