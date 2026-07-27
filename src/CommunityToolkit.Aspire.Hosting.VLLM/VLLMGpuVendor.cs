namespace Aspire.Hosting;

/// <summary>
/// Identifies the GPU vendor to configure for a <see cref="ApplicationModel.VLLMResource"/> container.
/// </summary>
public enum VLLMGpuVendor
{
    /// <summary>
    /// NVIDIA GPUs via the NVIDIA Container Toolkit (adds <c>--gpus all</c>).
    /// </summary>
    Nvidia,

    /// <summary>
    /// AMD GPUs via ROCm (uses the <c>-rocm</c> image variant and exposes
    /// <c>/dev/kfd</c> and <c>/dev/dri</c>).
    /// </summary>
    AMD
}
