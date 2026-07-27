using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace CommunityToolkit.Aspire.Hosting.VLLM.Tests;

public class AppHostTests
{
    [RequiresDockerFact]
    public async Task VLLMResource_StartsAndServesHealthCheck()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.CommunityToolkit_Aspire_Hosting_VLLM_AppHost>();

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // vLLM reports healthy only after the model has loaded; give it generous headroom.
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("vllm")
            .WaitAsync(TimeSpan.FromMinutes(15));

        var client = app.CreateHttpClient("vllm", "http");
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        await app.StopAsync();
    }
}
