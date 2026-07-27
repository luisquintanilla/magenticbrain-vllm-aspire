using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for adding a chat client backed by a vLLM OpenAI-compatible server to
/// an <see cref="AspireVLLMClientBuilder"/>.
/// </summary>
public static class AspireVLLMChatClientExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IChatClient"/> that talks to vLLM's OpenAI-compatible
    /// API.
    /// </summary>
    /// <param name="builder">The vLLM client builder.</param>
    /// <param name="modelId">
    /// The served model name to target. When <see langword="null"/>, the value from
    /// <see cref="VLLMClientSettings.Model"/> is used.
    /// </param>
    public static ChatClientBuilder AddChatClient(this AspireVLLMClientBuilder builder, string? modelId = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HostBuilder.Services.AddChatClient(
            services => CreateInnerChatClient(builder, services, modelId));
    }

    /// <summary>
    /// Registers a keyed <see cref="IChatClient"/> (keyed by the connection name) that talks
    /// to vLLM's OpenAI-compatible API. Requires the client to have been registered with
    /// <see cref="AspireVLLMExtensions.AddKeyedVLLMClient(IHostApplicationBuilder, string, Action{VLLMClientSettings}?)"/>.
    /// </summary>
    /// <param name="builder">The vLLM client builder.</param>
    /// <param name="modelId">
    /// The served model name to target. When <see langword="null"/>, the value from
    /// <see cref="VLLMClientSettings.Model"/> is used.
    /// </param>
    public static ChatClientBuilder AddKeyedChatClient(this AspireVLLMClientBuilder builder, string? modelId = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.ServiceKey is null)
        {
            throw new InvalidOperationException(
                $"{nameof(AddKeyedChatClient)} requires a keyed client. Register it with " +
                $"{nameof(AspireVLLMExtensions.AddKeyedVLLMClient)}.");
        }

        return builder.HostBuilder.Services.AddKeyedChatClient(
            builder.ServiceKey, services => CreateInnerChatClient(builder, services, modelId));
    }

    private static IChatClient CreateInnerChatClient(
        AspireVLLMClientBuilder builder,
        IServiceProvider services,
        string? modelId)
    {
        var settings = builder.Settings;

        var model = modelId ?? settings.Model
            ?? throw new InvalidOperationException(
                "A vLLM served model name is required. Provide it via AddChatClient(modelId), " +
                "VLLMClientSettings.Model, or 'Aspire:VLLM:<name>:Model'.");

        // settings.Endpoint is guaranteed non-null by AddVLLMClient.
        var endpoint = settings.Endpoint!;
        var apiKey = string.IsNullOrEmpty(settings.Key) ? VLLMClientSettings.PlaceholderApiKey : settings.Key;

        // vLLM exposes the OpenAI API under the /v1 base path.
        var baseAddress = new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/v1");

        IChatClient chatClient = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = baseAddress })
            .GetChatClient(model)
            .AsIChatClient();

        if (builder.DisableTracing)
        {
            return chatClient;
        }

        var loggerFactory = services.GetService<ILoggerFactory>();
        return new OpenTelemetryChatClient(
            chatClient,
            loggerFactory?.CreateLogger(typeof(OpenTelemetryChatClient).FullName!),
            AspireVLLMClientBuilder.TelemetrySourceName)
        {
            EnableSensitiveData = settings.EnableSensitiveTelemetryData,
        };
    }
}
