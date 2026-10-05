using System;
using System.Collections.Generic;

namespace DeadCellsSync.Core;

/// <summary>
/// A deterministic, in-process stand-in for a UDP-like link: messages sent at tick T are
/// delivered no earlier than T + latencyTicks, with jitter and packet loss both driven by a
/// seeded <see cref="Random"/> so tests are reproducible. This is what lets the test suite
/// assert reconciliation correctness "under injected latency/packet loss" without opening a
/// real socket or depending on wall-clock timing.
/// </summary>
public sealed class SimulatedNetworkChannel<T>
{
    private readonly Random _rng;
    private readonly double _packetLossProbability;
    private readonly int _minLatencyTicks;
    private readonly int _maxLatencyTicks;
    private readonly List<InFlightMessage> _inFlight = new();

    public int Sent { get; private set; }
    public int Dropped { get; private set; }
    public int Delivered { get; private set; }

    private readonly struct InFlightMessage
    {
        public readonly uint DeliverAtTick;
        public readonly T Payload;

        public InFlightMessage(uint deliverAtTick, T payload)
        {
            DeliverAtTick = deliverAtTick;
            Payload = payload;
        }
    }

    public SimulatedNetworkChannel(
        int seed,
        double packetLossProbability = 0.0,
        int minLatencyTicks = 0,
        int maxLatencyTicks = 0)
    {
        if (packetLossProbability is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(packetLossProbability));
        }

        if (maxLatencyTicks < minLatencyTicks)
        {
            throw new ArgumentException("maxLatencyTicks must be >= minLatencyTicks");
        }

        _rng = new Random(seed);
        _packetLossProbability = packetLossProbability;
        _minLatencyTicks = minLatencyTicks;
        _maxLatencyTicks = maxLatencyTicks;
    }

    /// <summary>Enqueues a message sent at <paramref name="currentTick"/>. May be dropped.</summary>
    public void Send(T payload, uint currentTick)
    {
        Sent++;
        if (_rng.NextDouble() < _packetLossProbability)
        {
            Dropped++;
            return;
        }

        var latency = _minLatencyTicks == _maxLatencyTicks
            ? _minLatencyTicks
            : _rng.Next(_minLatencyTicks, _maxLatencyTicks + 1);

        _inFlight.Add(new InFlightMessage(currentTick + (uint)latency, payload));
    }

    /// <summary>
    /// Returns (and removes) every message whose delivery tick has arrived, ordered by
    /// delivery tick ascending - jitter means these can arrive in a different order than
    /// they were sent, exactly as with real out-of-order UDP delivery.
    /// </summary>
    public List<T> ReceiveUpTo(uint currentTick)
    {
        var ready = new List<(uint tick, T payload)>();
        for (var i = _inFlight.Count - 1; i >= 0; i--)
        {
            if (_inFlight[i].DeliverAtTick <= currentTick)
            {
                ready.Add((_inFlight[i].DeliverAtTick, _inFlight[i].Payload));
                _inFlight.RemoveAt(i);
            }
        }

        ready.Sort((a, b) => a.tick.CompareTo(b.tick));
        Delivered += ready.Count;

        var result = new List<T>(ready.Count);
        foreach (var (_, payload) in ready)
        {
            result.Add(payload);
        }

        return result;
    }

    public int InFlightCount => _inFlight.Count;
}
