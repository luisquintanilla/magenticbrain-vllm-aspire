using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace CommunityToolkit.Aspire.Hosting.VLLM.Tests;

public class AddVLLMTests
{
    [Fact]
    public void AddVLLM_RegistersResourceWithDefaultImage()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        builder.AddVLLM("vllm");

        var resource = Assert.Single(builder.Resources.OfType<VLLMResource>());
        Assert.Equal("vllm", resource.Name);

        var image = Assert.Single(resource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("vllm/vllm-openai", image.Image);
        Assert.Equal("v0.26.0", image.Tag);
        Assert.Equal("docker.io", image.Registry);
    }

    [Fact]
    public void AddVLLM_ConfiguresHttpEndpointOnTargetPort8000()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        builder.AddVLLM("vllm");

        var resource = Assert.Single(builder.Resources.OfType<VLLMResource>());
        var endpoint = Assert.Single(resource.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal("http", endpoint.Name);
        Assert.Equal("http", endpoint.UriScheme);
        Assert.Equal(8000, endpoint.TargetPort);
    }

    [Fact]
    public void AddVLLM_WithExplicitPort_SetsHostPort()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        builder.AddVLLM("vllm", port: 9000);

        var resource = Assert.Single(builder.Resources.OfType<VLLMResource>());
        var endpoint = Assert.Single(resource.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(9000, endpoint.Port);
        Assert.Equal(8000, endpoint.TargetPort);
    }

    [Fact]
    public void VLLMResource_ConnectionStringExpression_ProjectsEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var vllm = builder.AddVLLM("vllm");

        Assert.Contains("Endpoint=", vllm.Resource.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public async Task WithGPUSupport_Nvidia_AddsGpusAllRuntimeArg()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var vllm = builder.AddVLLM("vllm").WithGPUSupport();

        var args = await GetContainerRuntimeArgsAsync(vllm.Resource);
        Assert.Equal(new[] { "--gpus", "all" }, args);
    }

    [Fact]
    public void WithGPUSupport_Amd_SwitchesToRocmImageTag()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var vllm = builder.AddVLLM("vllm").WithGPUSupport(VLLMGpuVendor.AMD);

        var image = Assert.Single(vllm.Resource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("v0.26.0-rocm", image.Tag);
    }

    [Fact]
    public async Task WithModel_And_WithServedModelName_ProduceOrderedArgs()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var vllm = builder.AddVLLM("vllm")
            .WithModel("/models/local")
            .WithServedModelName("my/model")
            .WithArgs("--dtype", "bfloat16");

        var args = await GetArgsAsync(vllm.Resource);
        Assert.Equal(new[] { "/models/local", "--served-model-name", "my/model", "--dtype", "bfloat16" }, args);
    }

    [Fact]
    public void WithDataVolume_MountsHuggingFaceCache()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var vllm = builder.AddVLLM("vllm").WithDataVolume();

        var mount = Assert.Single(vllm.Resource.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal("/root/.cache/huggingface", mount.Target);
        Assert.Equal(ContainerMountType.Volume, mount.Type);
    }

    [Fact]
    public void WithImage_And_ClearedRegistry_OverrideDefaults()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var vllm = builder.AddVLLM("vllm").WithImageRegistry(null!).WithImage("custom", "local");

        var image = Assert.Single(vllm.Resource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("custom", image.Image);
        Assert.Equal("local", image.Tag);
        Assert.Null(image.Registry);
    }

    private static async Task<List<string>> GetArgsAsync(IResource resource)
    {
        var args = new List<object>();
        var context = new CommandLineArgsCallbackContext(args, CancellationToken.None);
        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }
        return args.Select(a => a.ToString()!).ToList();
    }

    private static async Task<List<string>> GetContainerRuntimeArgsAsync(IResource resource)
    {
        var args = new List<object>();
        var context = new ContainerRuntimeArgsCallbackContext(args, CancellationToken.None);
        foreach (var annotation in resource.Annotations.OfType<ContainerRuntimeArgsCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }
        return args.Select(a => a.ToString()!).ToList();
    }
}
