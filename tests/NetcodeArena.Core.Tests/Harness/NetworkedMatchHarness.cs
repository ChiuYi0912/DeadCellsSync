using System;
using NetcodeArena.Core;
using NetcodeArena.Core.Client;
using NetcodeArena.Core.Networking;
using NetcodeArena.Core.Server;

namespace NetcodeArena.Core.Tests.Harness;

/// <summary>
/// Drives a full client-predicts / server-authoritative / client-reconciles loop entirely in
/// memory, routing player A's inputs and the server's snapshots through
/// <see cref="SimulatedNetworkChannel{T}"/> so tests can inject latency and packet loss while
/// staying fully deterministic (seeded RNG, no wall-clock, no sockets). Player B is simulated
/// server-side with a scripted, lossless input feed - the object under test is A's prediction
/// and reconciliation, not B's.
/// </summary>
public sealed class NetworkedMatchHarness
{
    private readonly ServerWorld _server = new();
    private readonly ClientPredictor _clientA;
    private readonly SimulatedNetworkChannel<PlayerInput> _inputChannel;
    private readonly SimulatedNetworkChannel<WorldSnapshot> _snapshotChannel;
    private readonly int _snapshotIntervalTicks;

    private uint _tick;

    public int MispredictionCount => _clientA.MispredictionCount;
    public int ReconciliationCount => _clientA.ReconciliationCount;
    public PlayerState ClientPredictedStateForA => _clientA.PredictedState;
    public PlayerState ServerAuthoritativeStateForA => _server.State.A;
    public PlayerState ServerAuthoritativeStateForB => _server.State.B;
    public uint CurrentTick => _tick;

    public NetworkedMatchHarness(
        int seed,
        double packetLossProbability,
        int minLatencyTicks,
        int maxLatencyTicks)
    {
        var initial = ArenaSimulation.InitialState();
        _clientA = new ClientPredictor(initial.A);

        // Two independently-seeded channels (input upstream, snapshot downstream) so loss/
        // latency on one direction is not correlated with the other, matching real asymmetric
        // network behavior.
        _inputChannel = new SimulatedNetworkChannel<PlayerInput>(
            seed, packetLossProbability, minLatencyTicks, maxLatencyTicks);
        _snapshotChannel = new SimulatedNetworkChannel<WorldSnapshot>(
            seed + 1, packetLossProbability, minLatencyTicks, maxLatencyTicks);

        _snapshotIntervalTicks = Math.Max(1, SimulationConfig.TicksPerSecond / SimulationConfig.SnapshotRateHz);
    }

    /// <summary>Runs one full round for both sides: client predicts + sends, server ticks
    /// (consuming whatever inputs have arrived) + broadcasts, client reconciles against
    /// whatever snapshots have arrived. B's input is scripted deterministically from the tick
    /// number so the whole run is reproducible.</summary>
    public void Step()
    {
        _tick++;

        var moveA = OrbitMove(_tick, periodTicks: 90, phase: 0f);
        var fireA = _tick % 12 == 0;
        var inputA = _clientA.ApplyInput(_tick, moveA, aimDirection: new Vector2F(1f, 0f), fireA);
        _inputChannel.Send(inputA, _tick);

        foreach (var deliveredInput in _inputChannel.ReceiveUpTo(_tick))
        {
            _server.ReceiveInput(PlayerId.PlayerA, deliveredInput);
        }

        var moveB = OrbitMove(_tick, periodTicks: 150, phase: 1.5f);
        var inputB = new PlayerInput(_tick, _tick, moveB, new Vector2F(-1f, 0f), fire: _tick % 20 == 0);
        _server.ReceiveInput(PlayerId.PlayerB, inputB);

        var snapshot = _server.Tick();

        if (_tick % (uint)_snapshotIntervalTicks == 0)
        {
            _snapshotChannel.Send(snapshot, _tick);
        }

        foreach (var deliveredSnapshot in _snapshotChannel.ReceiveUpTo(_tick))
        {
            _clientA.Reconcile(deliveredSnapshot.PlayerA);
        }
    }

    public void StepMany(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Step();
        }
    }

    /// <summary>
    /// Advances time with the client holding still (zero movement, no fire) rather than going
    /// silent, purely to let in-flight packets drain so a test can assert final convergence
    /// without an unbounded "still in flight" window. Critically, the client keeps predicting
    /// and sending its (now idle) input every tick here, exactly as a real client does when the
    /// player stops touching controls - it does NOT simply stop calling <see cref="ClientPredictor.ApplyInput"/>.
    /// If it did, the client's cached prediction would freeze at its last (moving) velocity
    /// while the server's <see cref="Server.ServerWorld"/> "held input" fallback (for a
    /// genuinely starved queue) independently zeroes velocity, producing a divergence that is
    /// an artifact of this harness going quiet - not a real misprediction a shipped client would
    /// ever produce, since a shipped client never stops sending.
    /// </summary>
    public void DrainNetwork(int maxTicks = 200)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            _tick++;

            var idleInputA = _clientA.ApplyInput(_tick, Vector2F.Zero, aimDirection: new Vector2F(1f, 0f), fire: false);
            _inputChannel.Send(idleInputA, _tick);

            foreach (var deliveredInput in _inputChannel.ReceiveUpTo(_tick))
            {
                _server.ReceiveInput(PlayerId.PlayerA, deliveredInput);
            }

            _server.ReceiveInput(PlayerId.PlayerB, PlayerInput.None(_tick, _tick));
            var snapshot = _server.Tick();

            if (_tick % (uint)_snapshotIntervalTicks == 0)
            {
                _snapshotChannel.Send(snapshot, _tick);
            }

            foreach (var deliveredSnapshot in _snapshotChannel.ReceiveUpTo(_tick))
            {
                _clientA.Reconcile(deliveredSnapshot.PlayerA);
            }

            if (_inputChannel.InFlightCount == 0 && _snapshotChannel.InFlightCount == 0
                && _clientA.PendingInputCount == 0)
            {
                return;
            }
        }
    }

    private static Vector2F OrbitMove(uint tick, int periodTicks, float phase)
    {
        var angle = phase + 2f * MathF.PI * (tick % periodTicks) / periodTicks;
        return new Vector2F(MathF.Cos(angle), MathF.Sin(angle));
    }
}
