namespace DeadCellsSync.Core;

/// <summary>
/// Tunables shared by client prediction and server simulation. Both sides MUST use the same
/// values or prediction will permanently diverge (a classic real-world netcode bug), which is
/// why this is a single static source of truth rather than duplicated constants.
/// </summary>
public static class SimulationConfig
{
    /// <summary>Fixed simulation tick rate in Hz. Movement/weapon logic runs once per tick.</summary>
    public const int TicksPerSecond = 30;

    public static float FixedDeltaTime => 1f / TicksPerSecond;

    public const float MaxHealth = 100f;
    public const float MoveSpeed = 6.0f; // world units / second
    public const float ArenaHalfWidth = 12f;
    public const float ArenaHalfHeight = 8f;
    public const float PlayerRadius = 0.5f;

    // Weapon: a hitscan "blaster" with a cooldown, fixed range and damage. Simple but real -
    // the reconciliation-worthy part is movement + timing, not weapon variety (see README scope).
    public const float WeaponCooldownSeconds = 0.35f;
    public const float WeaponRange = 14f;
    public const float WeaponDamage = 18f;

    /// <summary>
    /// Snapshot interpolation delay applied to the remote player's rendered position, in
    /// seconds. Must be >= one server broadcast interval to reliably have two snapshots to
    /// interpolate between even under minor jitter.
    /// </summary>
    public const float InterpolationDelaySeconds = 0.1f;

    /// <summary>Rate at which the server broadcasts world snapshots to clients, in Hz.</summary>
    public const int SnapshotRateHz = 20;
}
