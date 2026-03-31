using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MatrixRegistratorBot;

internal static class HealthService
{
    internal static Task HeartBeatAsync(CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync("/tmp/heartbeat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), cancellationToken);
    }
}
