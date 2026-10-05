using DeadCellsSync.Core;
using DeadCellsSync.Core.Server;
using DeadCellsSync.Core.Snapshot;

// Local synchronization demo: runs the whole sync pipeline with no real network.
//
//   ServerWorld (authoritative) -> WorldSnapshot -> SimulatedNetworkChannel (latency/jitter/loss)
//        -> SnapshotInterpolationBuffer -> sampled "remote hero" render position
//
// A scripted "remote hero" (Player A) orbits its spawn; Player B idles. Snapshots flow through
// the simulated transport, and the render position is sampled from the interpolation buffer at
// 60 Hz so smoothing under latency/jitter/loss is directly observable in the printed table.

var latencyTicks = 2;
var jitterTicks = 1;
var loss = 0.1;
var snapshotHz = 20;
var interpolationDelayMs = 150;
var seconds = 5;
var seed = 1234;

for (var i = 0; i < args.Length; i++)
{
    if (!args[i].StartsWith("--") || i + 1 >= args.Length)
    {
        continue;
    }

    var value = args[i + 1];
    switch (args[i])
    {
        case "--latency": latencyTicks = int.Parse(value); break;
        case "--jitter": jitterTicks = int.Parse(value); break;
        case "--loss": loss = double.Parse(value); break;
        case "--snapshot-hz": snapshotHz = int.Parse(value); break;
        case "--interpolation-delay-ms": interpolationDelayMs = int.Parse(value); break;
        case "--seconds": seconds = int.Parse(value); break;
        case "--seed": seed = int.Parse(value); break;
    }
    i++; // consume the value
}

var channel = new SimulatedNetworkChannel<WorldSnapshot>(
    seed, loss, minLatencyTicks: latencyTicks, maxLatencyTicks: latencyTicks + jitterTicks);
var server = new ServerWorld();
var buffer = new SnapshotInterpolationBuffer(interpolationDelayMs / 1000f);

var tickHz = SimulationConfig.TicksPerSecond;
var snapshotEvery = Math.Max(1, tickHz / snapshotHz);
var totalTicks = seconds * tickHz;

Console.WriteLine("[deadcells-sync] local sync demo (no real network)");
Console.WriteLine($"  tick rate          : {tickHz} Hz");
Console.WriteLine($"  snapshot           : every {snapshotEvery} tick(s)");
Console.WriteLine($"  render             : 60 Hz");
Console.WriteLine($"  latency            : {latencyTicks} tick(s) + {jitterTicks} tick(s) jitter");
Console.WriteLine($"  interpolation delay: {interpolationDelayMs} ms");
Console.WriteLine($"  packet loss        : {loss:P0}");
Console.WriteLine();
Console.WriteLine(" tick | serverA.X | renderA.X | |delta|");
Console.WriteLine("------+-----------+-----------+--------");

for (uint tick = 1; tick <= (uint)totalTicks; tick++)
{
    // Fake "remote hero" (A) orbits its spawn; B idles in place.
    var inputA = new PlayerInput(tick, tick, OrbitMove(tick, 90, 0f), new Vector2F(1f, 0f), fire: false);
    var inputB = PlayerInput.None(tick, tick);

    server.ReceiveInput(PlayerId.PlayerA, inputA);
    server.ReceiveInput(PlayerId.PlayerB, inputB);
    var snapshot = server.Tick();

    if (tick % (uint)snapshotEvery == 0)
    {
        channel.Send(snapshot, tick);
    }

    foreach (var delivered in channel.ReceiveUpTo(tick))
    {
        buffer.AddSnapshot(delivered.ServerTick, delivered.PlayerA);
    }

    if (buffer.Count == 0)
    {
        continue; // still inside the initial latency window; nothing to render yet
    }

    // Sample the remote hero at 60 Hz (two render samples per simulation tick).
    for (var sub = 0; sub < 2; sub++)
    {
        var renderTick = tick + sub * 0.5f;
        var renderState = buffer.Sample(renderTick * SimulationConfig.FixedDeltaTime);
        var serverA = server.State.A;
        var delta = MathF.Abs(renderState.Position.X - serverA.Position.X);
        Console.WriteLine($"{renderTick,5:F1} | {serverA.Position.X,9:F3} | {renderState.Position.X,9:F3} | {delta,6:F3}");
    }
}

Console.WriteLine();
Console.WriteLine($"snapshots sent={channel.Sent} dropped={channel.Dropped} delivered={channel.Delivered}");

static Vector2F OrbitMove(uint tick, int periodTicks, float phase)
{
    var angle = phase + 2f * MathF.PI * (tick % periodTicks) / periodTicks;
    return new Vector2F(MathF.Cos(angle), MathF.Sin(angle));
}
