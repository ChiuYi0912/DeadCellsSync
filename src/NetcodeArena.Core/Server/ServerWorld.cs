using System.Collections.Generic;
using NetcodeArena.Core.Networking;

namespace NetcodeArena.Core.Server;

/// <summary>
/// The authoritative, dedicated-server simulation for both players. Owns the one true
/// <see cref="ArenaSimulation.WorldState"/>; clients only ever predict ahead of it and
/// reconcile back to whatever this class decides happened.
///
/// Input scheduling policy: each server tick consumes exactly one buffered input per player
/// (clients send at the same rate the server ticks, so under a healthy connection this stays
/// in lockstep). If a player's queue is empty - because their input hasn't arrived yet or was
/// dropped - the server "holds": it keeps that player's last known aim direction but zeroes
/// their movement and suppresses Fire, i.e. the player pauses in place rather than being
/// blindly extrapolated forward on stale movement data. This mirrors the same "don't
/// extrapolate past what you know" philosophy as <see cref="Networking.SnapshotInterpolationBuffer"/>'s
/// buffer-underrun handling, and it does NOT advance that player's acknowledged input
/// sequence, so a genuinely lost input can never be replayed as extra movement or an extra
/// shot once the connection recovers.
/// </summary>
public sealed class ServerWorld
{
    private readonly Queue<PlayerInput> _queueA = new();
    private readonly Queue<PlayerInput> _queueB = new();
    private PlayerInput _lastConsumedA = PlayerInput.None(0, 0);
    private PlayerInput _lastConsumedB = PlayerInput.None(0, 0);
    private readonly List<HitEvent> _lastTickHits = new();

    public uint CurrentTick { get; private set; }
    public ArenaSimulation.WorldState State { get; private set; }
    public IReadOnlyList<HitEvent> LastTickHits => _lastTickHits;

    public ServerWorld()
    {
        State = ArenaSimulation.InitialState();
    }

    /// <summary>Enqueues an inbound input for later consumption. Order of arrival may differ
    /// from send order (the network layer may reorder); we requeue by arrival, matching how a
    /// real UDP socket hands packets to the application.</summary>
    public void ReceiveInput(PlayerId player, PlayerInput input)
    {
        (player == PlayerId.PlayerA ? _queueA : _queueB).Enqueue(input);
    }

    public int QueuedInputCount(PlayerId player) =>
        (player == PlayerId.PlayerA ? _queueA : _queueB).Count;

    /// <summary>Advances the authoritative simulation by exactly one tick and returns the
    /// resulting broadcastable snapshot.</summary>
    public WorldSnapshot Tick()
    {
        CurrentTick++;

        var inputA = NextInput(_queueA, ref _lastConsumedA);
        var inputB = NextInput(_queueB, ref _lastConsumedB);

        _lastTickHits.Clear();
        State = ArenaSimulation.Step(State, inputA, inputB, CurrentTick, _lastTickHits);

        return new WorldSnapshot(CurrentTick, State.A, State.B);
    }

    private static PlayerInput NextInput(Queue<PlayerInput> queue, ref PlayerInput lastConsumed)
    {
        if (queue.Count > 0)
        {
            lastConsumed = queue.Dequeue();
            return lastConsumed;
        }

        // Held input: pause movement (do not extrapolate), keep last known aim, no fire.
        return new PlayerInput(
            lastConsumed.Sequence,
            lastConsumed.ClientTick,
            Vector2F.Zero,
            lastConsumed.AimDirection,
            fire: false);
    }
}
