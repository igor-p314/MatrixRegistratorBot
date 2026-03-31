using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MatrixRegistratorBot;

internal static class HealthService
{
    internal static async ValueTask HeartBeatAsync(CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync("/tmp/heartbeat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), cancellationToken).ConfigureAwait(false);
    }
}
