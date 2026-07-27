namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A resource representing a <see href="https://github.com/vllm-project/vllm">vLLM</see>
/// OpenAI-compatible inference server running in a container.
/// </summary>
/// <param name="name">The name of the resource in the application model.</param>
public class VLLMResource(string name) : ContainerResource(name), IResourceWithConnectionString, IResourceWithEndpoints
{
    internal const string PrimaryEndpointName = "http";
    internal const int DefaultContainerPort = 8000;

    private EndpointReference? _primaryEndpoint;

    /// <summary>
    /// Gets the primary (HTTP) endpoint that exposes the OpenAI-compatible API.
    /// </summary>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, PrimaryEndpointName);

    /// <summary>
    /// Gets the connection string expression for the vLLM server. vLLM exposes an
    /// OpenAI-compatible API surface; append <c>/v1</c> to build the OpenAI base URL.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"Endpoint={PrimaryEndpoint.Property(EndpointProperty.Scheme)}://{PrimaryEndpoint.Property(EndpointProperty.Host)}:{PrimaryEndpoint.Property(EndpointProperty.Port)}");
}
