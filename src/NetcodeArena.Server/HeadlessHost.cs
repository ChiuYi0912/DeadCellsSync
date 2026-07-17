using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetcodeArena.Core;
using NetcodeArena.Core.Server;

namespace NetcodeArena.Server;

/// <summary>
/// The actual dedicated-server process: owns a UDP socket, runs
/// <see cref="ServerWorld"/> at a fixed <see cref="SimulationConfig.TicksPerSecond"/> tick
/// rate on a dedicated loop (decoupled from packet arrival, exactly like a real UDP game
/// server), and broadcasts snapshots to whichever two remote endpoints connected first.
///
/// Seat assignment is intentionally the simplest thing that works for a 2-player arena: the
/// first distinct remote endpoint to send a packet becomes PlayerA, the second becomes
/// PlayerB, further endpoints are ignored. Real matchmaking (queueing, session tokens, more
/// than one concurrent match per process) is a documented stretch goal, not built - see
/// README.
/// </summary>
public sealed class HeadlessHost : IDisposable
{
    private readonly UdpClient _socket;
    private readonly ServerWorld _world = new();
    private readonly ConcurrentDictionary<IPEndPoint, PlayerId> _seats = new();
    private int _nextSeat;

    public HeadlessHost(int port)
    {
        _socket = new UdpClient(port);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var receiveLoop = ReceiveLoopAsync(cancellationToken);
        var tickLoop = TickLoopAsync(cancellationToken);
        await Task.WhenAll(receiveLoop, tickLoop).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (result.Buffer.Length == 0 || result.Buffer[0] != WireCodec.InputPacketType)
            {
                continue; // unrecognized/garbage packet - a real server would rate-limit/log this.
            }

            if (!TryAssignSeat(result.RemoteEndPoint, out var playerId))
            {
                continue; // match is full
            }

            var input = WireCodec.DecodeInput(result.Buffer);
            _world.ReceiveInput(playerId, input);
        }
    }

    private bool TryAssignSeat(IPEndPoint remote, out PlayerId playerId)
    {
        if (_seats.TryGetValue(remote, out playerId))
        {
            return true;
        }

        var seatIndex = Interlocked.Increment(ref _nextSeat) - 1;
        if (seatIndex >= 2)
        {
            playerId = default;
            return false;
        }

        playerId = seatIndex == 0 ? PlayerId.PlayerA : PlayerId.PlayerB;
        return _seats.TryAdd(remote, playerId);
    }

    private async Task TickLoopAsync(CancellationToken cancellationToken)
    {
        var tickInterval = TimeSpan.FromSeconds(SimulationConfig.FixedDeltaTime);
        var snapshotEveryNTicks = Math.Max(1, SimulationConfig.TicksPerSecond / SimulationConfig.SnapshotRateHz);
        using var timer = new PeriodicTimer(tickInterval);
        var stopwatch = Stopwatch.StartNew();

        while (!cancellationToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var snapshot = _world.Tick();

            if (snapshot.ServerTick % (uint)snapshotEveryNTicks == 0)
            {
                await BroadcastSnapshotAsync(snapshot).ConfigureAwait(false);
            }

            if (snapshot.ServerTick % (uint)(SimulationConfig.TicksPerSecond * 5) == 0)
            {
                Console.WriteLine(
                    $"[netcode-arena] tick={snapshot.ServerTick} uptime={stopwatch.Elapsed:mm\\:ss} "
                    + $"seats={_seats.Count}/2 A.hp={snapshot.PlayerA.Health:F0} B.hp={snapshot.PlayerB.Health:F0}");
            }
        }
    }

    private async Task BroadcastSnapshotAsync(Core.Networking.WorldSnapshot snapshot)
    {
        var payload = WireCodec.EncodeSnapshot(snapshot);
        foreach (var endpoint in _seats.Keys)
        {
            await _socket.SendAsync(payload, payload.Length, endpoint).ConfigureAwait(false);
        }
    }

    public void Dispose() => _socket.Dispose();
}
