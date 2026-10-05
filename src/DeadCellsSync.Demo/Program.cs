using DeadCellsSync.Core;
using DeadCellsSync.Core.Snapshot;

// ============================================================================
// DeadCellsSync — movement interpolation teaching demo
//
// A single remote player moves along the X axis. A "fake server" produces one
// discrete WorldSnapshot every `snapshotEveryTicks` ticks (default 3 => 10 Hz).
// Those snapshots flow through a SimulatedNetworkChannel (latency / jitter /
// packet loss) into a SnapshotInterpolationBuffer, and a 60 Hz render loop
// samples the buffer.
//
// The table shows, per render frame:
//   renderT   delayed render time (now - interpolation delay), seconds
//   serverT   authoritative server time, seconds
//   prevT     tick-time of the previous snapshot
//   nextT     tick-time of the next snapshot
//   alpha     where renderT sits between prevT and nextT, 0..1
//   serverX   authoritative position at serverT
//   prevX     previous snapshot position (discrete)
//   nextX     next snapshot position (discrete)
//   renderX   interpolated render position (continuous)
//   serverV   authoritative velocity at serverT
//   renderV   interpolated velocity (same alpha)
//
// Run `--scenario reversal` to see the player reverse direction, or pass
// `--velocity 1|5|10` to compare speeds. See README.md for the full walkthrough.
// ============================================================================

var scenario = "constant";   // "constant" (move right) or "reversal" (right -> left)
var velocity = 10f;          // world units / second
var latencyTicks = 0;        // fixed transport latency, in ticks (30 ticks/sec)
var jitterTicks = 0;         // extra random latency, in ticks
var loss = 0.0;              // packet loss probability
var snapshotEveryTicks = 3;  // a snapshot every N ticks (3 => 10 Hz)
var interpolationDelayMs = 100;
var seconds = 0.8f;
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
        case "--scenario": scenario = value; break;
        case "--velocity": velocity = float.Parse(value); break;
        case "--latency": latencyTicks = int.Parse(value); break;
        case "--jitter": jitterTicks = int.Parse(value); break;
        case "--loss": loss = double.Parse(value); break;
        case "--snapshot-every": snapshotEveryTicks = int.Parse(value); break;
        case "--interpolation-delay-ms": interpolationDelayMs = int.Parse(value); break;
        case "--seconds": seconds = float.Parse(value); break;
        case "--seed": seed = int.Parse(value); break;
    }
    i++; // consume the value
}

var fixedDt = SimulationConfig.FixedDeltaTime; // 1 / 30 s
var channel = new SimulatedNetworkChannel<WorldSnapshot>(
    seed, loss, minLatencyTicks: latencyTicks, maxLatencyTicks: latencyTicks + jitterTicks);
var buffer = new SnapshotInterpolationBuffer(interpolationDelayMs / 1000f);

Console.WriteLine("=== DeadCellsSync: movement interpolation demo ===");
Console.WriteLine($"  scenario          : {scenario}");
Console.WriteLine($"  velocity          : {velocity} units/s");
Console.WriteLine($"  snapshot          : every {snapshotEveryTicks} tick(s) ({SimulationConfig.TicksPerSecond / snapshotEveryTicks} Hz)");
Console.WriteLine($"  render            : 60 Hz");
Console.WriteLine($"  interpolation delay: {interpolationDelayMs} ms");
Console.WriteLine($"  latency/jitter    : {latencyTicks}/{jitterTicks} tick(s)");
Console.WriteLine($"  packet loss       : {loss:P0}");
Console.WriteLine();
Console.WriteLine(" renderT | serverT |  prevT |  nextT | alpha | serverX |  prevX |  nextX | renderX | serverV | renderV");
Console.WriteLine("---------+---------+--------+--------+-------+---------+--------+--------+---------+---------+---------");

var totalTicks = (int)(seconds * SimulationConfig.TicksPerSecond);
InterpolationSample? bestWorked = null;
PlayerState bestRenderState = default;
var bestRenderTime = 0f;

for (uint tick = 0; tick <= (uint)totalTicks; tick++)
{
    // Fake server: emit a snapshot of the remote player every snapshotEveryTicks ticks.
    if (tick % (uint)snapshotEveryTicks == 0)
    {
        var remote = ServerStateAt(tick);
        var snapshot = new WorldSnapshot(tick, remote, PlayerState.Spawn(Vector2F.Zero));
        channel.Send(snapshot, tick);
    }

    // Client: receive whatever has arrived through the (simulated) transport.
    foreach (var delivered in channel.ReceiveUpTo(tick))
    {
        buffer.AddSnapshot(delivered.ServerTick, delivered.PlayerA);
    }

    if (buffer.Count == 0)
    {
        continue; // still inside the initial latency window; nothing to render yet
    }

    // Render at 60 Hz: two render samples per simulation tick.
    for (var sub = 0; sub < 2; sub++)
    {
        var localTick = tick + sub * 0.5f;
        var localTime = localTick * fixedDt;

        var info = buffer.GetInterpolationSample(localTime);
        var renderState = buffer.Sample(localTime);
        var serverState = ServerStateAt(tick);

        var renderTime = localTime - interpolationDelayMs / 1000f;
        var serverTime = tick * fixedDt;

        Console.WriteLine(
            $"{renderTime,7:F3} | {serverTime,7:F3} | {TickToSeconds(info.PreviousTick),6:F3} | " +
            $"{TickToSeconds(info.NextTick),6:F3} | {info.Alpha,5:F2} | {serverState.Position.X,7:F2} | " +
            $"{info.PreviousState.Position.X,6:F2} | {info.NextState.Position.X,6:F2} | " +
            $"{renderState.Position.X,7:F2} | {serverState.Velocity.X,7:F2} | {renderState.Velocity.X,7:F2}");

        // Track the interpolating frame (prev != next) whose alpha is closest to 0.5; skip
        // "holding" frames where the buffer has only one snapshot to show.
        if (info.PreviousTick != info.NextTick
            && (bestWorked is null || MathF.Abs(info.Alpha - 0.5f) < MathF.Abs(bestWorked.Value.Alpha - 0.5f)))
        {
            bestWorked = info;
            bestRenderState = renderState;
            bestRenderTime = renderTime;
        }
    }
}

Console.WriteLine();
Console.WriteLine($"snapshots sent={channel.Sent} dropped={channel.Dropped} delivered={channel.Delivered}");

// Print one real frame whose alpha is closest to 0.5, as a worked example.
if (bestWorked.HasValue)
{
    var w = bestWorked.Value;
    var prevTime = TickToSeconds(w.PreviousTick);
    var nextTime = TickToSeconds(w.NextTick);
    Console.WriteLine();
    Console.WriteLine("--- worked example (the frame closest to alpha = 0.5) ---");
    Console.WriteLine($"  previous snapshot: time = {prevTime:F3}  position = {w.PreviousState.Position.X:F2}");
    Console.WriteLine($"  next snapshot    : time = {nextTime:F3}  position = {w.NextState.Position.X:F2}");
    Console.WriteLine($"  render time      : {bestRenderTime:F3}");
    Console.WriteLine($"  alpha            : ({bestRenderTime:F3} - {prevTime:F3}) / ({nextTime:F3} - {prevTime:F3}) = {w.Alpha:F2}");
    Console.WriteLine($"  render position  : lerp({w.PreviousState.Position.X:F2}, {w.NextState.Position.X:F2}, {w.Alpha:F2}) = {bestRenderState.Position.X:F2}");
}

// ---------------------------------------------------------------------------
// The fake server: the remote player's authoritative state at a given tick.
// ---------------------------------------------------------------------------
PlayerState ServerStateAt(uint tick)
{
    var x = PositionX(tick);
    var vx = VelocityX(tick);
    return new PlayerState(new Vector2F(x, 0f), new Vector2F(vx, 0f), SimulationConfig.MaxHealth, 0f, tick);
}

float PositionX(uint tick)
{
    if (scenario == "reversal")
    {
        // Move right for the first `flipTick` ticks, then left: 0,1,2,3,2,1,0,-1,...
        const uint flipTick = 9;
        if (tick <= flipTick)
        {
            return velocity * tick * fixedDt;
        }

        var peak = velocity * flipTick * fixedDt;
        return peak - velocity * (tick - flipTick) * fixedDt;
    }

    return velocity * tick * fixedDt; // constant rightward movement
}

float VelocityX(uint tick)
{
    if (scenario == "reversal")
    {
        const uint flipTick = 9;
        return tick < flipTick ? velocity : -velocity;
    }

    return velocity;
}

static float TickToSeconds(float tick) => tick * SimulationConfig.FixedDeltaTime;
