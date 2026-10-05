using System.Collections.Generic;

namespace DeadCellsSync.Core;

/// <summary>
/// The complete, deterministic, two-player world simulation for a single tick. Both
/// <see cref="Client.ClientPredictor"/> and <see cref="Server.ServerWorld"/> delegate to this
/// exact function so that, given identical inputs, they produce bit-for-bit (well,
/// float-for-float) identical results. That equivalence is what makes reconciliation work:
/// the client can replay this function locally and trust it will match what the server did.
/// </summary>
public static class ArenaSimulation
{
    public readonly record struct WorldState(PlayerState A, PlayerState B)
    {
        public PlayerState Get(PlayerId id) => id == PlayerId.PlayerA ? A : B;

        public WorldState With(PlayerId id, PlayerState state) =>
            id == PlayerId.PlayerA ? this with { A = state } : this with { B = state };
    }

    public static WorldState InitialState() => new(
        PlayerState.Spawn(new Vector2F(-4f, 0f)),
        PlayerState.Spawn(new Vector2F(4f, 0f)));

    /// <summary>
    /// Advances the world by exactly one fixed tick given both players' inputs for that tick.
    /// Order of operations: movement integration for both players, then weapon resolution for
    /// both players (movement always resolves before shooting within the same tick so a shot
    /// uses this tick's post-move positions, matching what each side already rendered).
    /// </summary>
    public static WorldState Step(
        in WorldState state,
        in PlayerInput inputA,
        in PlayerInput inputB,
        uint tick,
        List<HitEvent>? hitEventsOut = null)
    {
        var movedA = PlayerMovement.Step(state.A, inputA, SimulationConfig.FixedDeltaTime);
        var movedB = PlayerMovement.Step(state.B, inputB, SimulationConfig.FixedDeltaTime);

        movedA = movedA.WithLastProcessedInputSequence(inputA.Sequence);
        movedB = movedB.WithLastProcessedInputSequence(inputB.Sequence);

        var (afterFireA, hitFromA) = WeaponSystem.TryFire(
            PlayerId.PlayerA, movedA, PlayerId.PlayerB, movedB, inputA, tick);
        var (afterFireB, hitFromB) = WeaponSystem.TryFire(
            PlayerId.PlayerB, movedB, PlayerId.PlayerA, afterFireA, inputB, tick);

        var finalA = afterFireA;
        var finalB = afterFireB;

        if (hitFromA is { } hitA)
        {
            finalB = WeaponSystem.ApplyHit(finalB, hitA);
            hitEventsOut?.Add(hitA);
        }

        if (hitFromB is { } hitB)
        {
            finalA = WeaponSystem.ApplyHit(finalA, hitB);
            hitEventsOut?.Add(hitB);
        }

        return new WorldState(finalA, finalB);
    }
}
