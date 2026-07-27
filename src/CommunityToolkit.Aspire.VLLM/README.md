# CommunityToolkit.Aspire.VLLM

An [Aspire](https://aka.ms/dotnet/aspire) **client integration** for consuming a
[vLLM](https://docs.vllm.ai) OpenAI-compatible inference server as a
[`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
`IChatClient`.

It pairs with the `CommunityToolkit.Aspire.Hosting.VLLM` hosting integration: reference the
vLLM resource from your consuming project in the app host and the endpoint flows through as a
connection string. The client applies vLLM-correct defaults so you don't hit the usual traps:

- a **placeholder API key** (vLLM ignores it, but the OpenAI client requires a non-empty credential),
- the **`/v1` base path** appended automatically,
- the **served model name** (`--served-model-name`) resolved from settings,
- a **`/health` health check** that reports readiness of the vLLM engine.

## Usage

In the app host, reference the vLLM resource from the consuming project:

```csharp
var vllm = builder.AddVLLM("vllm");

builder.AddProject<Projects.MyApp>("myapp")
    .WithReference(vllm)
    .WaitFor(vllm);
```

In the consuming project, add the chat client:

```csharp
builder.AddVLLMClient("vllm", settings => settings.Model = "microsoft/MagenticBrain")
    .AddChatClient()
    .UseFunctionInvocation();
```

The connection name (`"vllm"`) resolves the endpoint from `ConnectionStrings:vllm` (the
`Endpoint=scheme://host:port` value emitted by the hosting resource) and settings from the
`Aspire:VLLM:vllm` configuration section.

### Settings

Bind from `Aspire:VLLM:<connectionName>` or configure via the callback:

| Setting | Description |
| --- | --- |
| `Endpoint` | Base endpoint of the vLLM server (e.g. `http://localhost:8000`). Usually injected via the connection string. |
| `Key` | API key. Optional — a placeholder is used when empty. |
| `Model` | Served model name (`--served-model-name`) to target. |
| `DisableHealthChecks` | Disables the `/health` health check. |
| `DisableTracing` | Disables OpenTelemetry tracing of chat calls. |
| `EnableSensitiveTelemetryData` | Captures prompts and completions in telemetry (off by default). |

### Keyed registration

For multiple vLLM servers, use the keyed overloads:

```csharp
builder.AddKeyedVLLMClient("chat", s => s.Model = "microsoft/MagenticBrain")
    .AddKeyedChatClient();
```

## Related

- `CommunityToolkit.Aspire.Hosting.VLLM` — the hosting integration (`builder.AddVLLM(...)`).
- [`iwaitu/vllmchatclient`](https://github.com/iwaitu/vllmchatclient) — a richer, standalone
  multi-provider vLLM `IChatClient` if you need vLLM-specific per-request knobs (thinking
  toggles, guided/structured output defaults) that this thin integration intentionally leaves
  to the server configuration and `Microsoft.Extensions.AI` primitives.
