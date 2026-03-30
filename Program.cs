using MatrixRegistratorBot;
using Serilog;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static readonly CancellationTokenSource cts = new();

    static async Task Main(string[] _)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            var matrixService = new MatrixService();
            await matrixService.StartAsync(cts.Token).ConfigureAwait(false);
        }
        finally
        {
            cts.Dispose();
        }
    }
}
