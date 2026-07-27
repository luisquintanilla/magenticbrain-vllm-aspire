using System.Diagnostics;
using Xunit;

namespace CommunityToolkit.Aspire.Hosting.VLLM.Tests;

/// <summary>
/// An xUnit <see cref="FactAttribute"/> that is skipped unless a Docker daemon is reachable.
/// Mirrors the CommunityToolkit's <c>[RequiresDocker]</c> gating for container integration tests.
/// </summary>
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerAvailability.IsAvailable)
        {
            Skip = "Docker is not available; skipping container integration test.";
        }
    }
}

internal static class DockerAvailability
{
    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
