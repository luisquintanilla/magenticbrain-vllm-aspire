using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.VLLM;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding and configuring <see cref="VLLMResource"/> resources in an
/// Aspire application model.
/// </summary>
public static class VLLMResourceBuilderExtensions
{
    /// <summary>
    /// Adds a vLLM OpenAI-compatible inference server container to the application model.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="port">The optional host port to bind the container's HTTP endpoint to.</param>
    /// <returns>A resource builder for further configuration.</returns>
    public static IResourceBuilder<VLLMResource> AddVLLM(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? port = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var resource = new VLLMResource(name);

        return builder.AddResource(resource)
            .WithImage(VLLMContainerImageTags.Image, VLLMContainerImageTags.Tag)
            .WithImageRegistry(VLLMContainerImageTags.Registry)
            .WithHttpEndpoint(port: port, targetPort: VLLMResource.DefaultContainerPort, name: VLLMResource.PrimaryEndpointName)
            // vLLM's /health returns 200 only once the model is loaded and serving, so
            // dependents that WaitFor this resource are gated on the model being ready.
            .WithHttpHealthCheck("/health");
    }

    /// <summary>
    /// Enables GPU acceleration for the vLLM container.
    /// </summary>
    /// <param name="builder">The vLLM resource builder.</param>
    /// <param name="vendor">The GPU vendor. Defaults to <see cref="VLLMGpuVendor.Nvidia"/>.</param>
    public static IResourceBuilder<VLLMResource> WithGPUSupport(
        this IResourceBuilder<VLLMResource> builder,
        VLLMGpuVendor vendor = VLLMGpuVendor.Nvidia)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return vendor switch
        {
            VLLMGpuVendor.Nvidia => builder.WithContainerRuntimeArgs("--gpus", "all"),
            VLLMGpuVendor.AMD => builder
                .WithImageTag(VLLMContainerImageTags.Tag + VLLMContainerImageTags.RocmTagSuffix)
                .WithContainerRuntimeArgs("--device", "/dev/kfd", "--device", "/dev/dri"),
            _ => throw new ArgumentOutOfRangeException(nameof(vendor), vendor, "Unsupported GPU vendor.")
        };
    }

    /// <summary>
    /// Adds a named volume for the Hugging Face cache (<c>/root/.cache/huggingface</c>) so
    /// downloaded model weights persist across container restarts.
    /// </summary>
    /// <param name="builder">The vLLM resource builder.</param>
    /// <param name="name">The volume name. A name is generated from the resource name when omitted.</param>
    /// <param name="isReadOnly">Whether the volume is mounted read-only.</param>
    public static IResourceBuilder<VLLMResource> WithDataVolume(
        this IResourceBuilder<VLLMResource> builder,
        string? name = null,
        bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithVolume(name ?? $"{builder.Resource.Name}-huggingface", "/root/.cache/huggingface", isReadOnly);
    }

    /// <summary>
    /// Sets the <c>HF_TOKEN</c> environment variable from a (typically secret) parameter so vLLM
    /// can download gated Hugging Face models.
    /// </summary>
    /// <param name="builder">The vLLM resource builder.</param>
    /// <param name="token">A parameter resource carrying the Hugging Face token.</param>
    public static IResourceBuilder<VLLMResource> WithHuggingFaceToken(
        this IResourceBuilder<VLLMResource> builder,
        IResourceBuilder<ParameterResource> token)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(token);
        return builder.WithEnvironment("HF_TOKEN", token);
    }

    /// <summary>
    /// Specifies the model to serve: a Hugging Face model id (e.g. <c>Qwen/Qwen3-8B</c>) or a
    /// container path to a local checkpoint. This is passed as the first positional argument to
    /// <c>vllm serve</c>, so call it before any additional <c>WithArgs(...)</c> calls.
    /// </summary>
    /// <param name="builder">The vLLM resource builder.</param>
    /// <param name="model">The model id or container path.</param>
    public static IResourceBuilder<VLLMResource> WithModel(
        this IResourceBuilder<VLLMResource> builder,
        string model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(model);
        return builder.WithArgs(model);
    }

    /// <summary>
    /// Sets the public model id that clients use to address the model (<c>--served-model-name</c>),
    /// decoupling it from the underlying model path.
    /// </summary>
    /// <param name="builder">The vLLM resource builder.</param>
    /// <param name="servedModelName">The model id exposed to clients.</param>
    public static IResourceBuilder<VLLMResource> WithServedModelName(
        this IResourceBuilder<VLLMResource> builder,
        string servedModelName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(servedModelName);
        return builder.WithArgs("--served-model-name", servedModelName);
    }
}
