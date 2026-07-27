namespace Microsoft.Extensions.Hosting;

/// <summary>
/// A builder for configuring a vLLM client. Returned by
/// <see cref="AspireVLLMExtensions.AddVLLMClient(IHostApplicationBuilder, string, Action{VLLMClientSettings}?)"/>
/// and its keyed counterpart so that chat (and, in future, embedding) clients can be added
/// with the resolved connection settings.
/// </summary>
public sealed class AspireVLLMClientBuilder
{
    /// <summary>
    /// The OpenTelemetry <see cref="System.Diagnostics.ActivitySource"/> name used by the
    /// Microsoft.Extensions.AI instrumentation.
    /// </summary>
    internal const string TelemetrySourceName = "Experimental.Microsoft.Extensions.AI";

    internal AspireVLLMClientBuilder(
        IHostApplicationBuilder hostBuilder,
        VLLMClientSettings settings,
        string connectionName,
        object? serviceKey)
    {
        HostBuilder = hostBuilder;
        Settings = settings;
        ConnectionName = connectionName;
        ServiceKey = serviceKey;
    }

    /// <summary>
    /// Gets the host application builder the client is registered on.
    /// </summary>
    public IHostApplicationBuilder HostBuilder { get; }

    /// <summary>
    /// Gets the resolved client settings (endpoint, key, model, and toggles).
    /// </summary>
    public VLLMClientSettings Settings { get; }

    /// <summary>
    /// Gets the connection name used to resolve the vLLM endpoint.
    /// </summary>
    public string ConnectionName { get; }

    /// <summary>
    /// Gets the service key for keyed registrations, or <see langword="null"/> when the
    /// client is registered without a key.
    /// </summary>
    public object? ServiceKey { get; }

    /// <summary>
    /// Gets a value indicating whether OpenTelemetry tracing is disabled for this client.
    /// </summary>
    public bool DisableTracing => Settings.DisableTracing;
}
