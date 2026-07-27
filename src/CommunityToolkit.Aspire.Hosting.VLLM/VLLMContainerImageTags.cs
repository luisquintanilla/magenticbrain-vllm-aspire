namespace CommunityToolkit.Aspire.Hosting.VLLM;

/// <summary>
/// Default container image coordinates for the <c>vllm/vllm-openai</c> image.
/// Tags are pinned to a specific release (never <c>latest</c>).
/// </summary>
internal static class VLLMContainerImageTags
{
    /// <summary>The container registry: <c>docker.io</c>.</summary>
    public const string Registry = "docker.io";

    /// <summary>The image repository: <c>vllm/vllm-openai</c>.</summary>
    public const string Image = "vllm/vllm-openai";

    /// <summary>The pinned image tag.</summary>
    public const string Tag = "v0.26.0";

    /// <summary>Suffix appended to <see cref="Tag"/> to select the AMD ROCm image variant.</summary>
    public const string RocmTagSuffix = "-rocm";
}
