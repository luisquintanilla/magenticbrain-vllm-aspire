using Microsoft.Extensions.Hosting;
using Xunit;

namespace CommunityToolkit.Aspire.VLLM.Tests;

public class VLLMClientSettingsTests
{
    [Fact]
    public void ParseConnectionString_BareUri_SetsEndpoint()
    {
        var settings = new VLLMClientSettings();
        settings.ParseConnectionString("http://localhost:8000");

        Assert.Equal(new Uri("http://localhost:8000"), settings.Endpoint);
    }

    [Fact]
    public void ParseConnectionString_EndpointForm_SetsEndpoint()
    {
        var settings = new VLLMClientSettings();
        settings.ParseConnectionString("Endpoint=http://host:8000");

        Assert.Equal(new Uri("http://host:8000"), settings.Endpoint);
    }

    [Fact]
    public void ParseConnectionString_FullForm_SetsEndpointKeyAndModel()
    {
        var settings = new VLLMClientSettings();
        settings.ParseConnectionString("Endpoint=https://host:8000;Key=secret;Model=my-model");

        Assert.Equal(new Uri("https://host:8000"), settings.Endpoint);
        Assert.Equal("secret", settings.Key);
        Assert.Equal("my-model", settings.Model);
    }

    [Fact]
    public void ParseConnectionString_Empty_LeavesEndpointNull()
    {
        var settings = new VLLMClientSettings();
        settings.ParseConnectionString(null);
        settings.ParseConnectionString("");

        Assert.Null(settings.Endpoint);
    }
}
