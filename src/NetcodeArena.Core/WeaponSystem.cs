namespace NetcodeArena.Core;

/// <summary>
/// Authoritative-capable hitscan weapon resolution. Deterministic and pure so it can run
/// identically inside client prediction (for immediate muzzle-flash / cooldown feedback)
/// and inside the server simulation (for actual damage, which the client never applies
/// locally - only the server's reconciliation snapshot can lower an opponent's health).
/// </summary>
public static class WeaponSystem
{
    /// <summary>
    /// Attempts to fire. Returns the shooter's updated state (cooldown reset on success) and,
    /// if the shot lands on the target, a HitEvent. Hit test is a simple forward-cone + range
    /// check against the target's current authoritative position - deliberately simple (see
    /// README stretch goals for lag-compensated raycasting against a rewound target position).
    /// </summary>
    public static (PlayerState shooter, HitEvent? hit) TryFire(
        PlayerId shooterId,
        in PlayerState shooter,
        PlayerId targetId,
        in PlayerState target,
        in PlayerInput input,
        uint tick)
    {
        if (!input.Fire || shooter.FireCooldownRemaining > 0f || !shooter.IsAlive)
        {
            return (shooter, null);
        }

        var updatedShooter = new PlayerState(
            shooter.Position,
            shooter.Velocity,
            shooter.Health,
            SimulationConfig.WeaponCooldownSeconds,
            shooter.LastProcessedInputSequence);

        if (!target.IsAlive)
        {
            return (updatedShooter, null);
        }

        var toTarget = target.Position - shooter.Position;
        var distance = toTarget.Length;
        if (distance > SimulationConfig.WeaponRange)
        {
            return (updatedShooter, null);
        }

        var aim = input.AimDirection.Normalized();
        if (aim.LengthSquared < 1e-6f)
        {
            return (updatedShooter, null);
        }

        var toTargetDir = toTarget.Normalized();
        // Dot product cone check: accept hits within ~25 degrees of aim direction (cos(25deg) ~= 0.906).
        var alignment = aim.X * toTargetDir.X + aim.Y * toTargetDir.Y;
        const float coneThreshold = 0.906f;
        if (alignment < coneThreshold)
        {
            return (updatedShooter, null);
        }

        var hit = new HitEvent(shooterId, targetId, SimulationConfig.WeaponDamage, tick);
        return (updatedShooter, hit);
    }

    public static PlayerState ApplyHit(in PlayerState target, in HitEvent hit)
    {
        var newHealth = System.MathF.Max(0f, target.Health - hit.Damage);
        return target.WithHealth(newHealth);
    }
}
