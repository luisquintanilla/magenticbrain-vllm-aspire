using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommunityToolkit.Aspire.VLLM.Tests;

public class AddVLLMClientTests
{
    private const string ConnectionName = "vllm";
    private const string Endpoint = "http://localhost:8000";
    private const string Model = "microsoft/MagenticBrain";

    private static HostApplicationBuilder CreateBuilder(
        IEnumerable<KeyValuePair<string, string?>>? config = null,
        string? connectionString = Endpoint)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        var values = new Dictionary<string, string?>();
        if (connectionString is not null)
        {
            values[$"ConnectionStrings:{ConnectionName}"] = $"Endpoint={connectionString}";
        }
        if (config is not null)
        {
            foreach (var kvp in config)
            {
                values[kvp.Key] = kvp.Value;
            }
        }

        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }

    private static IChatClient ResolveChatClient(IHost host, object? serviceKey = null)
    {
        using var scope = host.Services.CreateScope();
        return serviceKey is null
            ? scope.ServiceProvider.GetRequiredService<IChatClient>()
            : scope.ServiceProvider.GetRequiredKeyedService<IChatClient>(serviceKey);
    }

    [Fact]
    public void AddVLLMClient_RegistersResolvableChatClient()
    {
        var builder = CreateBuilder();
        builder.AddVLLMClient(ConnectionName, s => s.Model = Model).AddChatClient();

        using var host = builder.Build();
        var chatClient = ResolveChatClient(host);

        Assert.NotNull(chatClient);
    }

    [Fact]
    public void AddVLLMClient_AppendsV1AndUsesServedModel()
    {
        var builder = CreateBuilder();
        builder.AddVLLMClient(ConnectionName, s =>
        {
            s.Model = Model;
            s.DisableTracing = true; // resolve the raw OpenAI client for metadata inspection
        }).AddChatClient();

        using var host = builder.Build();
        var chatClient = ResolveChatClient(host);
        var metadata = chatClient.GetService<ChatClientMetadata>();

        Assert.NotNull(metadata);
        Assert.EndsWith("/v1", metadata!.ProviderUri!.AbsoluteUri.TrimEnd('/'));
        Assert.Equal(Model, metadata.DefaultModelId);
    }

    [Fact]
    public void AddVLLMClient_WithoutApiKey_DoesNotThrow()
    {
        // vLLM ignores the key, but the OpenAI client requires a non-empty credential;
        // the integration supplies a placeholder so construction succeeds.
        var builder = CreateBuilder();
        builder.AddVLLMClient(ConnectionName, s => s.Model = Model).AddChatClient();

        using var host = builder.Build();
        var exception = Record.Exception(() => ResolveChatClient(host));

        Assert.Null(exception);
    }

    [Fact]
    public void AddVLLMClient_ResolvesEndpointFromConnectionString()
    {
        var builder = CreateBuilder();
        var clientBuilder = builder.AddVLLMClient(ConnectionName, s => s.Model = Model);

        Assert.Equal(new Uri(Endpoint), clientBuilder.Settings.Endpoint);
        Assert.Equal(Model, clientBuilder.Settings.Model);
        Assert.Null(clientBuilder.Settings.Key);
    }

    [Fact]
    public void AddVLLMClient_BindsSettingsFromConfigurationSection()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            [$"Aspire:VLLM:{ConnectionName}:Model"] = Model,
            [$"Aspire:VLLM:{ConnectionName}:DisableTracing"] = "true",
            [$"Aspire:VLLM:{ConnectionName}:Key"] = "from-config",
        });

        var clientBuilder = builder.AddVLLMClient(ConnectionName);

        Assert.Equal(Model, clientBuilder.Settings.Model);
        Assert.True(clientBuilder.Settings.DisableTracing);
        Assert.Equal("from-config", clientBuilder.Settings.Key);
    }

    [Fact]
    public void AddVLLMClient_ConfigureCallback_OverridesConfiguration()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            [$"Aspire:VLLM:{ConnectionName}:Model"] = "from-config",
        });

        var clientBuilder = builder.AddVLLMClient(ConnectionName, s => s.Model = "from-callback");

        Assert.Equal("from-callback", clientBuilder.Settings.Model);
    }

    [Fact]
    public void AddVLLMClient_RegistersHealthCheckByDefault()
    {
        var builder = CreateBuilder();
        builder.AddVLLMClient(ConnectionName, s => s.Model = Model);

        using var host = builder.Build();
        var registrations = host.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Contains(registrations, r => r.Name == "VLLM");
    }

    [Fact]
    public void AddVLLMClient_DisableHealthChecks_SkipsRegistration()
    {
        var builder = CreateBuilder();
        builder.AddVLLMClient(ConnectionName, s =>
        {
            s.Model = Model;
            s.DisableHealthChecks = true;
        });

        using var host = builder.Build();
        var registrations = host.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.DoesNotContain(registrations, r => r.Name == "VLLM");
    }

    [Fact]
    public void AddKeyedVLLMClient_RegistersKeyedChatClientAndHealthCheck()
    {
        var builder = CreateBuilder();
        builder.AddKeyedVLLMClient(ConnectionName, s => s.Model = Model).AddKeyedChatClient();

        using var host = builder.Build();
        var chatClient = ResolveChatClient(host, ConnectionName);
        var registrations = host.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.NotNull(chatClient);
        Assert.Contains(registrations, r => r.Name == $"VLLM_{ConnectionName}");
    }

    [Fact]
    public void AddVLLMClient_MissingEndpoint_Throws()
    {
        var builder = CreateBuilder(connectionString: null);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddVLLMClient(ConnectionName, s => s.Model = Model));

        Assert.Contains(ConnectionName, exception.Message);
    }

    [Fact]
    public void AddKeyedChatClient_OnNonKeyedClient_Throws()
    {
        var builder = CreateBuilder();
        var clientBuilder = builder.AddVLLMClient(ConnectionName, s => s.Model = Model);

        Assert.Throws<InvalidOperationException>(() => clientBuilder.AddKeyedChatClient());
    }
}
