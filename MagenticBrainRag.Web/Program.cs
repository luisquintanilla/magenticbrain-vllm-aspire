using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using MagenticBrainRag.Web.Components;
using MagenticBrainRag.Web.Services;
using MagenticBrainRag.Web.Services.Ingestion;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Chat: microsoft/MagenticBrain served by vLLM over its OpenAI-compatible API.
// vLLM ignores the API key, but the OpenAI client requires a non-empty credential.
// The endpoint is injected by the AppHost (VLLM_ENDPOINT -> the vllm container URL).
var vllmEndpoint = builder.Configuration["VLLM_ENDPOINT"]
    ?? throw new InvalidOperationException(
        "VLLM_ENDPOINT is not configured. Run through the Aspire AppHost, or set it to e.g. http://localhost:8000.");
var chatClient = new OpenAIClient(
        new ApiKeyCredential("not-used"),
        new OpenAIClientOptions { Endpoint = new Uri($"{vllmEndpoint.TrimEnd('/')}/v1") })
    .GetChatClient("microsoft/MagenticBrain")
    .AsIChatClient();

builder.Services.AddChatClient(chatClient)
    // MagenticBrain's model card sampling: temp 0.7, top_p 0.8, presence_penalty 1.0
    // (avoid greedy decoding). Applied as defaults so every call is consistent.
    .ConfigureOptions(options =>
    {
        options.Temperature ??= 0.7f;
        options.TopP ??= 0.8f;
        options.PresencePenalty ??= 1.0f;
    })
    .UseFunctionInvocation()
    .UseOpenTelemetry(configure: c =>
        c.EnableSensitiveData = builder.Environment.IsDevelopment());

// Embeddings: nomic-embed-text served by Ollama on CPU (keeps all VRAM for the LLM).
builder.AddOllamaApiClient("embedding")
    .AddEmbeddingGenerator();

var vectorStorePath = Path.Combine(AppContext.BaseDirectory, "vector-store.db");
var vectorStoreConnectionString = $"Data Source={vectorStorePath}";
builder.Services.AddSqliteVectorStore(_ => vectorStoreConnectionString);
builder.Services.AddSqliteCollection<string, IngestedChunk>(IngestedChunk.CollectionName, vectorStoreConnectionString);
builder.Services.AddSingleton<DataIngestor>();
builder.Services.AddSingleton<SemanticSearch>();
builder.Services.AddKeyedSingleton("ingestion_directory", new DirectoryInfo(Path.Combine(builder.Environment.WebRootPath, "Data")));

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
