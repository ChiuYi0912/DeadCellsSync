namespace DeadCellsSync.Core;

/// <summary>
/// Full simulate-able state of one player at a given tick. This is intentionally a plain,
/// immutable value type so the simulation is pure (State, Input) -> State, which is what
/// makes replay-based reconciliation and unit testing tractable.
/// </summary>
public readonly struct PlayerState
{
    public readonly Vector2F Position;
    public readonly Vector2F Velocity;
    public readonly float Health;
    public readonly float FireCooldownRemaining;

    /// <summary>
    /// The sequence number of the last input this state incorporates. Used by the server
    /// to tell a client which of its inputs have been applied (for prediction pruning) and
    /// by the client to know how far to rewind before replaying.
    /// </summary>
    public readonly uint LastProcessedInputSequence;

    public bool IsAlive => Health > 0f;

    public PlayerState(
        Vector2F position,
        Vector2F velocity,
        float health,
        float fireCooldownRemaining,
        uint lastProcessedInputSequence)
    {
        Position = position;
        Velocity = velocity;
        Health = health;
        FireCooldownRemaining = fireCooldownRemaining;
        LastProcessedInputSequence = lastProcessedInputSequence;
    }

    public static PlayerState Spawn(Vector2F position) =>
        new(position, Vector2F.Zero, SimulationConfig.MaxHealth, 0f, 0);

    public PlayerState WithLastProcessedInputSequence(uint sequence) =>
        new(Position, Velocity, Health, FireCooldownRemaining, sequence);

    public PlayerState WithHealth(float health) =>
        new(Position, Velocity, health, FireCooldownRemaining, LastProcessedInputSequence);

    /// <summary>
    /// Positional/velocity equality used by the client to decide whether a server correction
    /// actually diverges from its local prediction (small float error tolerance).
    /// </summary>
    public bool ApproximatelyEquals(in PlayerState other, float epsilon = 0.01f)
    {
        return Vector2F.Distance(Position, other.Position) <= epsilon
            && Vector2F.Distance(Velocity, other.Velocity) <= epsilon
            && System.MathF.Abs(Health - other.Health) <= epsilon;
    }
}
