using System;
using System.Threading;
using NetcodeArena.Server;

var port = args.Length > 0 && int.TryParse(args[0], out var parsedPort) ? parsedPort : 7777;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"[netcode-arena] headless server starting on udp/{port} "
    + $"({NetcodeArena.Core.SimulationConfig.TicksPerSecond} tick/s, "
    + $"{NetcodeArena.Core.SimulationConfig.SnapshotRateHz} snapshot/s)");

using var host = new HeadlessHost(port);
try
{
    await host.RunAsync(cts.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C / SIGTERM shutdown.
}

Console.WriteLine("[netcode-arena] headless server stopped");
