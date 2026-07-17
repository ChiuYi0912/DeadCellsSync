using System;

namespace NetcodeArena.Core;

/// <summary>
/// Pure movement integration step. This is the single function that both the client
/// predictor and the authoritative server call, tick for tick, with the same
/// SimulationConfig constants - which is the entire reason prediction can converge:
/// given the same (state, input) pair, both sides MUST produce the same output.
/// </summary>
public static class PlayerMovement
{
    public static PlayerState Step(in PlayerState state, in PlayerInput input, float dt)
    {
        var desiredVelocity = input.Move.ClampMagnitude(1f) * SimulationConfig.MoveSpeed;
        var newPosition = state.Position + desiredVelocity * dt;
        newPosition = ClampToArena(newPosition);

        var newCooldown = MathF.Max(0f, state.FireCooldownRemaining - dt);

        return new PlayerState(
            newPosition,
            desiredVelocity,
            state.Health,
            newCooldown,
            state.LastProcessedInputSequence);
    }

    public static Vector2F ClampToArena(Vector2F position)
    {
        var maxX = SimulationConfig.ArenaHalfWidth - SimulationConfig.PlayerRadius;
        var maxY = SimulationConfig.ArenaHalfHeight - SimulationConfig.PlayerRadius;
        var x = Math.Clamp(position.X, -maxX, maxX);
        var y = Math.Clamp(position.Y, -maxY, maxY);
        return new Vector2F(x, y);
    }
}
