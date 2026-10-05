using DeadCellsSync.Core;
using DeadCellsSync.Core.Tests.Assertions;
using Xunit;

namespace DeadCellsSync.Core.Tests;

public class PlayerMovementTests
{
    [Fact]
    public void Step_WithNoInput_DoesNotMove()
    {
        var state = PlayerState.Spawn(Vector2F.Zero);
        var input = PlayerInput.None(0, 0);

        var result = PlayerMovement.Step(state, input, SimulationConfig.FixedDeltaTime);

        Approx.Equal(0f, result.Position.X, 0.0001f);
        Approx.Equal(0f, result.Position.Y, 0.0001f);
    }

    [Fact]
    public void Step_MovingRight_IncreasesXByExpectedDistance()
    {
        var state = PlayerState.Spawn(Vector2F.Zero);
        var input = new PlayerInput(1, 1, new Vector2F(1, 0), new Vector2F(1, 0), false);

        var result = PlayerMovement.Step(state, input, SimulationConfig.FixedDeltaTime);

        var expected = SimulationConfig.MoveSpeed * SimulationConfig.FixedDeltaTime;
        Approx.Equal(expected, result.Position.X, 0.0001f);
        Approx.Equal(0f, result.Position.Y, 0.0001f);
    }

    [Fact]
    public void Step_DiagonalInput_IsNormalizedSoDiagonalSpeedMatchesAxisSpeed()
    {
        var state = PlayerState.Spawn(Vector2F.Zero);
        var input = new PlayerInput(1, 1, new Vector2F(1, 1), new Vector2F(1, 0), false);

        var result = PlayerMovement.Step(state, input, SimulationConfig.FixedDeltaTime);

        var speed = result.Velocity.Length;
        Approx.Equal(SimulationConfig.MoveSpeed, speed, 0.001f);
    }

    [Fact]
    public void Step_RepeatedlyMovingPastArenaEdge_ClampsPositionInsideBounds()
    {
        var state = PlayerState.Spawn(new Vector2F(SimulationConfig.ArenaHalfWidth - 0.1f, 0f));
        var input = new PlayerInput(1, 1, new Vector2F(1, 0), new Vector2F(1, 0), false);

        PlayerState result = state;
        for (var i = 0; i < 100; i++)
        {
            result = PlayerMovement.Step(result, input, SimulationConfig.FixedDeltaTime);
        }

        var maxX = SimulationConfig.ArenaHalfWidth - SimulationConfig.PlayerRadius;
        Assert.True(result.Position.X <= maxX + 1e-4f);
    }

    [Fact]
    public void Step_ClampsAllFourArenaEdges()
    {
        var input = new PlayerInput(1, 1, new Vector2F(-1, -1), new Vector2F(1, 0), false);
        var state = PlayerState.Spawn(new Vector2F(-SimulationConfig.ArenaHalfWidth, -SimulationConfig.ArenaHalfHeight));

        PlayerState result = state;
        for (var i = 0; i < 50; i++)
        {
            result = PlayerMovement.Step(result, input, SimulationConfig.FixedDeltaTime);
        }

        var maxX = SimulationConfig.ArenaHalfWidth - SimulationConfig.PlayerRadius;
        var maxY = SimulationConfig.ArenaHalfHeight - SimulationConfig.PlayerRadius;
        Assert.True(result.Position.X >= -maxX - 1e-4f);
        Assert.True(result.Position.Y >= -maxY - 1e-4f);
    }

    [Fact]
    public void Step_CooldownTicksDownButNeverBelowZero()
    {
        var state = new PlayerState(Vector2F.Zero, Vector2F.Zero, 100f, 0.05f, 0);
        var input = PlayerInput.None(1, 1);

        var afterOneTick = PlayerMovement.Step(state, input, SimulationConfig.FixedDeltaTime);
        var afterTwoTicks = PlayerMovement.Step(afterOneTick, input, SimulationConfig.FixedDeltaTime);

        Assert.True(afterOneTick.FireCooldownRemaining >= 0f);
        Approx.Equal(0f, afterTwoTicks.FireCooldownRemaining, 1e-05f);
    }
}
