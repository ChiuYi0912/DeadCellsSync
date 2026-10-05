namespace DeadCellsSync.Core;

/// <summary>
/// A single tick's worth of client input. Sent client -> server, and also stored locally
/// in the client's prediction history so it can be replayed during reconciliation.
/// </summary>
public readonly struct PlayerInput
{
    /// <summary>Monotonically increasing per-client sequence number. Never resets.</summary>
    public readonly uint Sequence;

    /// <summary>The simulation tick this input was generated for (client's local clock).</summary>
    public readonly uint ClientTick;

    /// <summary>Movement axis, expected pre-clamped to unit length by the input source.</summary>
    public readonly Vector2F Move;

    /// <summary>Aim direction (normalized) used for weapon fire, independent of movement.</summary>
    public readonly Vector2F AimDirection;

    /// <summary>True if the fire button was held/pressed during this tick.</summary>
    public readonly bool Fire;

    public PlayerInput(uint sequence, uint clientTick, Vector2F move, Vector2F aimDirection, bool fire)
    {
        Sequence = sequence;
        ClientTick = clientTick;
        Move = move;
        AimDirection = aimDirection;
        Fire = fire;
    }

    public static PlayerInput None(uint sequence, uint clientTick) =>
        new(sequence, clientTick, Vector2F.Zero, new Vector2F(1, 0), false);
}
