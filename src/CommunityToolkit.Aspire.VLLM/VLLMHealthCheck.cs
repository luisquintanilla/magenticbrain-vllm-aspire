using System.Net.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CommunityToolkit.Aspire.VLLM;

/// <summary>
/// A health check that probes the vLLM server's <c>/health</c> endpoint, which returns a
/// 200 response once the engine has finished loading the model and is ready to serve.
/// </summary>
internal sealed class VLLMHealthCheck : IHealthCheck, IDisposable
{
    private readonly HttpClient _httpClient;

    public VLLMHealthCheck(Uri endpoint)
    {
        // Ensure a trailing slash so the relative "health" path is appended rather than
        // replacing the last segment of the base address.
        var baseAddress = endpoint.AbsoluteUri.EndsWith('/') ? endpoint : new Uri(endpoint.AbsoluteUri + "/");
        _httpClient = new HttpClient { BaseAddress = baseAddress };
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("health", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"vLLM '/health' returned status code {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The vLLM '/health' request failed.", ex);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
