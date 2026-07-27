using CommunityToolkit.Aspire.VLLM.ConsumerApp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// The vLLM client integration resolves the endpoint from the "vllm" connection string the
// AppHost injects via WithReference, appends /v1, and supplies a placeholder API key so the
// OpenAI client's credential check passes.
builder.AddVLLMClient("vllm", settings => settings.Model = "Qwen/Qwen3-0.6B")
    .AddChatClient()
    .UseFunctionInvocation();

builder.Services.AddHostedService<Worker>();

builder.Build().Run();
