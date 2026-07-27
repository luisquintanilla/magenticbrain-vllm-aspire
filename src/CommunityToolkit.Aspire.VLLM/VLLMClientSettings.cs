using System.Data.Common;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides the client configuration settings for connecting to a vLLM OpenAI-compatible
/// inference server.
/// </summary>
public sealed class VLLMClientSettings
{
    /// <summary>
    /// The API key sent to vLLM. vLLM ignores the value unless it was started with
    /// <c>--api-key</c>, but the OpenAI client requires a non-empty credential, so a
    /// placeholder is used when none is supplied.
    /// </summary>
    internal const string PlaceholderApiKey = "not-used";

    /// <summary>
    /// Gets or sets the base endpoint of the vLLM server, for example
    /// <c>http://localhost:8000</c>. The <c>/v1</c> OpenAI base path is appended automatically.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the API key. Optional — vLLM ignores it unless configured with
    /// <c>--api-key</c>. When empty, a non-empty placeholder is used so the OpenAI client
    /// does not fail its credential check.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets the served model name (the vLLM <c>--served-model-name</c>) to target,
    /// for example <c>microsoft/MagenticBrain</c>.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the vLLM <c>/health</c> health check is
    /// disabled. The default is <see langword="false"/>.
    /// </summary>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether OpenTelemetry tracing of chat calls is
    /// disabled. The default is <see langword="false"/>.
    /// </summary>
    public bool DisableTracing { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether prompts and completions are captured in
    /// telemetry. This can contain sensitive data, so the default is <see langword="false"/>.
    /// </summary>
    public bool EnableSensitiveTelemetryData { get; set; }

    /// <summary>
    /// Parses a connection string emitted by the vLLM hosting resource. Supports both the
    /// keyed form <c>Endpoint=scheme://host:port[;Key=...][;Model=...]</c> and a bare
    /// absolute URI such as <c>http://localhost:8000</c>.
    /// </summary>
    internal void ParseConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        // A bare absolute URI (e.g. "http://localhost:8000").
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var direct) &&
            (direct.Scheme == Uri.UriSchemeHttp || direct.Scheme == Uri.UriSchemeHttps))
        {
            Endpoint = direct;
            return;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        if (builder.TryGetValue("Endpoint", out var endpointValue) &&
            Uri.TryCreate(endpointValue?.ToString(), UriKind.Absolute, out var endpointUri))
        {
            Endpoint = endpointUri;
        }

        if (builder.TryGetValue("Key", out var keyValue) && keyValue?.ToString() is { Length: > 0 } key)
        {
            Key = key;
        }

        if (builder.TryGetValue("Model", out var modelValue) && modelValue?.ToString() is { Length: > 0 } model)
        {
            Model = model;
        }
    }
}
