using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommunityToolkit.Aspire.VLLM.ConsumerApp;

/// <summary>
/// A minimal consumer that sends a single prompt to the vLLM-backed <see cref="IChatClient"/>
/// resolved by the client integration, logs the response, and shuts down.
/// </summary>
public sealed class Worker(
    IChatClient chatClient,
    ILogger<Worker> logger,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var response = await chatClient.GetResponseAsync(
                "In one sentence, what is Aspire?", cancellationToken: stoppingToken);
            logger.LogInformation("vLLM response: {Response}", response.Text);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to call the vLLM chat client.");
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
