namespace NetcodeArena.Core.Networking;

/// <summary>
/// What the server broadcasts to both clients each snapshot tick: the full authoritative
/// world state plus, per player, which input sequence number the server has processed so
/// far (so each client knows how much of its own prediction history it can discard).
/// </summary>
public readonly struct WorldSnapshot
{
    public readonly uint ServerTick;
    public readonly PlayerState PlayerA;
    public readonly PlayerState PlayerB;

    public WorldSnapshot(uint serverTick, PlayerState playerA, PlayerState playerB)
    {
        ServerTick = serverTick;
        PlayerA = playerA;
        PlayerB = playerB;
    }

    public PlayerState Get(PlayerId id) => id == PlayerId.PlayerA ? PlayerA : PlayerB;
}
