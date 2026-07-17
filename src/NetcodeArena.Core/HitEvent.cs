namespace NetcodeArena.Core;

/// <summary>Emitted when a fired shot connects. Purely informational output of a tick.</summary>
public readonly struct HitEvent
{
    public readonly PlayerId Shooter;
    public readonly PlayerId Target;
    public readonly float Damage;
    public readonly uint Tick;

    public HitEvent(PlayerId shooter, PlayerId target, float damage, uint tick)
    {
        Shooter = shooter;
        Target = target;
        Damage = damage;
        Tick = tick;
    }
}

public enum PlayerId
{
    PlayerA = 0,
    PlayerB = 1,
}

public static class PlayerIdExtensions
{
    public static PlayerId Opponent(this PlayerId id) =>
        id == PlayerId.PlayerA ? PlayerId.PlayerB : PlayerId.PlayerA;
}
