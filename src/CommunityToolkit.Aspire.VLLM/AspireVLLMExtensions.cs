using CommunityToolkit.Aspire.VLLM;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for registering a vLLM client that talks to a vLLM OpenAI-compatible
/// inference server. Pairs with the <c>CommunityToolkit.Aspire.Hosting.VLLM</c> hosting
/// integration: <c>WithReference</c> a vLLM resource in the app host and the endpoint flows
/// through automatically as a connection string.
/// </summary>
public static class AspireVLLMExtensions
{
    private const string DefaultConfigSectionName = "Aspire:VLLM";

    /// <summary>
    /// Registers a vLLM client for the given <paramref name="connectionName"/>. Call
    /// <see cref="AspireVLLMChatClientExtensions.AddChatClient(AspireVLLMClientBuilder, string?)"/>
    /// on the returned builder to add an <see cref="Microsoft.Extensions.AI.IChatClient"/>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">
    /// The connection name used to resolve the endpoint from
    /// <c>ConnectionStrings:{connectionName}</c> and settings from
    /// <c>Aspire:VLLM:{connectionName}</c>.
    /// </param>
    /// <param name="configureSettings">An optional callback to configure the settings.</param>
    public static AspireVLLMClientBuilder AddVLLMClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<VLLMClientSettings>? configureSettings = null)
        => builder.AddVLLMClientInternal(connectionName, serviceKey: null, configureSettings);

    /// <summary>
    /// Registers a keyed vLLM client for the given <paramref name="name"/>, keyed by the
    /// same name. Call
    /// <see cref="AspireVLLMChatClientExtensions.AddKeyedChatClient(AspireVLLMClientBuilder, string?)"/>
    /// on the returned builder to add a keyed <see cref="Microsoft.Extensions.AI.IChatClient"/>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="name">The connection name, also used as the service key.</param>
    /// <param name="configureSettings">An optional callback to configure the settings.</param>
    public static AspireVLLMClientBuilder AddKeyedVLLMClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<VLLMClientSettings>? configureSettings = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddVLLMClientInternal(name, serviceKey: name, configureSettings);
    }

    private static AspireVLLMClientBuilder AddVLLMClientInternal(
        this IHostApplicationBuilder builder,
        string connectionName,
        object? serviceKey,
        Action<VLLMClientSettings>? configureSettings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);

        var settings = new VLLMClientSettings();
        builder.Configuration.GetSection($"{DefaultConfigSectionName}:{connectionName}").Bind(settings);
        settings.ParseConnectionString(builder.Configuration.GetConnectionString(connectionName));
        configureSettings?.Invoke(settings);

        if (settings.Endpoint is null)
        {
            throw new InvalidOperationException(
                $"A vLLM endpoint could not be resolved for the connection '{connectionName}'. " +
                $"Provide a 'ConnectionStrings:{connectionName}' value (for example 'Endpoint=http://localhost:8000'), " +
                $"set '{DefaultConfigSectionName}:{connectionName}:Endpoint', or configure it via the callback.");
        }

        if (!settings.DisableHealthChecks)
        {
            var healthCheckName = serviceKey is null ? "VLLM" : $"VLLM_{connectionName}";
            builder.Services.AddHealthChecks()
                .AddCheck(healthCheckName, new VLLMHealthCheck(settings.Endpoint));
        }

        if (!settings.DisableTracing)
        {
            // Ensure the Microsoft.Extensions.AI activity source is collected even when the
            // consumer has not configured it (this is idempotent when it already has).
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddSource(AspireVLLMClientBuilder.TelemetrySourceName));
        }

        return new AspireVLLMClientBuilder(builder, settings, connectionName, serviceKey);
    }
}
